using AutoUpdaterDotNET;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using static Kneeboard_Server.Simbrief;
using static Kneeboard_Server.Waypoints;
using Kneeboard_Server.Logging;
using Kneeboard_Server.Navigraph;

using Path = System.IO.Path;

namespace Kneeboard_Server
{
    public partial class Kneeboard_Server : Form
    {
        private static Kneeboard_Server instance;
        private bool _dragging = false;
        private Point _start_point = new Point(0, 0);
        public static string folderpath = "";
        public static string communityFolderPath = "";
        public string port = "815";
        // REMOVED: public static int filesShowed = 0; (was unused)
        public static string flightplan;
        public static string simbriefOFPData;
        public static string cachedSimbriefTimeGenerated = null;

        // SimBrief AppData-Verzeichnis für OFP-Downloads (Schreibrechte ohne Admin)
        public static readonly string simbriefDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Gsimulations", "Kneeboard Server", "Simbrief");

        // Background SimBrief sync
        private static System.Threading.Timer simbriefBackgroundTimer;
        private static readonly object simbriefSyncLock = new object();
        private static bool isBackgroundSyncRunning = false;
        private const int SIMBRIEF_CHECK_INTERVAL_MS = 180000; // 3 minutes

        // SimConnect Manager
        private SimConnectManager simConnectManager;

        /// <summary>
        /// Gets the SimConnect manager instance for procedure queries
        /// </summary>
        public SimConnectManager SimConnect => simConnectManager;

        /// <summary>
        /// Converts a PLN waypoint to an object with proper type markers
        /// </summary>
        private static object ConvertWaypointToObject(SimBaseDocumentFlightPlanFlightPlanATCWaypoint wp, bool isSid, bool isStar)
        {
            // Parse WorldPosition (format: "N48° 18' 15.64",E14° 17' 48.13",+000000.00")
            double lat = 0, lng = 0;
            if (!string.IsNullOrEmpty(wp.WorldPosition))
            {
                var coords = ParseWorldPosition(wp.WorldPosition);
                lat = coords.Item1;
                lng = coords.Item2;
            }

            string waypointType = wp.ATCWaypointType ?? "User";
            if (isSid) waypointType = "DEP " + waypointType;
            else if (isStar) waypointType = "ARR " + waypointType;

            return new
            {
                name = wp.id ?? "",
                lat = lat,
                lng = lng,
                altitude = (int)(wp.Alt1FPSpecified ? wp.Alt1FP : 0),
                waypointType = waypointType,
                atcWaypointType = wp.ATCWaypointType,
                departureProcedure = wp.DepartureFP ?? "",
                arrivalProcedure = wp.ArrivalFP ?? "",
                airway = wp.ATCAirway ?? "",
                runwayNumber = wp.RunwayNumberFPSpecified ? wp.RunwayNumberFP.ToString() : "",
                runwayDesignator = wp.RunwayDesignatorFP ?? ""
            };
        }

        /// <summary>
        /// Parses MSFS WorldPosition format to lat/lng
        /// </summary>
        private static Tuple<double, double> ParseWorldPosition(string worldPos)
        {
            try
            {
                // Format: "N48° 18' 15.64",E14° 17' 48.13",+000000.00"
                var parts = worldPos.Split(',');
                if (parts.Length < 2) return Tuple.Create(0.0, 0.0);

                double lat = ParseCoordinate(parts[0].Trim('"'));
                double lng = ParseCoordinate(parts[1].Trim('"'));
                return Tuple.Create(lat, lng);
            }
            catch
            {
                return Tuple.Create(0.0, 0.0);
            }
        }

        /// <summary>
        /// Parses a single coordinate in DMS format
        /// </summary>
        private static double ParseCoordinate(string coord)
        {
            // Format: N48° 18' 15.64" or E14° 17' 48.13"
            bool isNegative = coord.StartsWith("S") || coord.StartsWith("W");
            coord = coord.TrimStart('N', 'S', 'E', 'W');

            // Remove degree, minute, second symbols
            coord = coord.Replace("°", " ").Replace("'", " ").Replace("\"", " ");
            var parts = coord.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 3) return 0;

            double degrees = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            double minutes = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            double seconds = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);

            double result = degrees + (minutes / 60.0) + (seconds / 3600.0);
            return isNegative ? -result : result;
        }

        /// <summary>
        /// Extracts SID waypoints from SimBrief navlog
        /// SID waypoints have Stage="CLB" and Is_sid_star="1"
        /// </summary>
        public static object GetSidWaypointsFromSimbrief()
        {
            try
            {
                if (string.IsNullOrEmpty(simbriefOFPData))
                {
                    return new { error = "No SimBrief data loaded", waypoints = new List<object>() };
                }

                var ofp = JsonConvert.DeserializeObject<Simbrief.OFP>(simbriefOFPData);
                if (ofp?.Navlog?.Fix == null)
                {
                    return new { error = "No navlog in SimBrief data", waypoints = new List<object>() };
                }

                // Extract SID name from route (first element before space, e.g. "OBOKA1F/25C ...")
                string sidName = "";
                if (!string.IsNullOrEmpty(ofp.General?.Route))
                {
                    var routeParts = ofp.General.Route.Split(' ');
                    if (routeParts.Length > 0)
                    {
                        sidName = routeParts[0].Split('/')[0];
                    }
                }

                string departureIcao = ofp.Origin?.Icao_code;
                var waypoints = new List<object>();

                // Extract from SimBrief
                bool foundFirstSidWaypoint = false;
                foreach (var fix in ofp.Navlog.Fix)
                {
                    if (fix.Stage == "CLB")
                    {
                        if (fix.Type == "apt" && fix.Ident == ofp.Origin?.Icao_code)
                            continue;

                        if (fix.Is_sid_star == "1")
                            foundFirstSidWaypoint = true;

                        if (fix.Is_sid_star == "1" || foundFirstSidWaypoint)
                        {
                            double.TryParse(fix.Pos_lat, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lat);
                            double.TryParse(fix.Pos_long, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lon);
                            double.TryParse(fix.Altitude_feet ?? "0", System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double alt);

                            waypoints.Add(new
                            {
                                ident = fix.Ident,
                                name = fix.Name,
                                type = "DEP " + (fix.Type ?? "wpt"),
                                lat = lat,
                                lon = lon,
                                alt = alt,
                                isSidStar = fix.Is_sid_star == "1"
                            });
                        }
                    }
                }

                return new
                {
                    source = "SimBrief",
                    departure = departureIcao,
                    runway = ofp.Origin?.Plan_rwy,
                    sidName = sidName,
                    waypointCount = waypoints.Count,
                    waypoints = waypoints
                };
            }
            catch (Exception ex)
            {
                KneeboardLogger.SimBriefError($"Error extracting SID waypoints: {ex.Message}");
                return new { error = ex.Message, waypoints = new List<object>() };
            }
        }

        /// <summary>
        /// Extracts STAR waypoints from SimBrief navlog
        /// STAR waypoints have Stage="DSC" and Is_sid_star="1"
        /// </summary>
        public static object GetStarWaypointsFromSimbrief()
        {
            try
            {
                if (string.IsNullOrEmpty(simbriefOFPData))
                {
                    return new { error = "No SimBrief data loaded", waypoints = new List<object>() };
                }

                var ofp = JsonConvert.DeserializeObject<Simbrief.OFP>(simbriefOFPData);
                if (ofp?.Navlog?.Fix == null)
                {
                    return new { error = "No navlog in SimBrief data", waypoints = new List<object>() };
                }

                // Extract STAR name from route (last element, e.g. "... SOMAX1A")
                string starName = "";
                if (!string.IsNullOrEmpty(ofp.General?.Route))
                {
                    var routeParts = ofp.General.Route.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (routeParts.Length > 0)
                    {
                        starName = routeParts[routeParts.Length - 1];
                    }
                }

                string arrivalIcao = ofp.Destination?.Icao_code;
                var waypoints = new List<object>();

                // Extract from SimBrief
                bool foundFirstStarWaypoint = false;
                foreach (var fix in ofp.Navlog.Fix)
                {
                    if (fix.Stage == "DSC")
                    {
                        if (fix.Is_sid_star == "1")
                            foundFirstStarWaypoint = true;

                        if (foundFirstStarWaypoint || fix.Is_sid_star == "1")
                        {
                            double.TryParse(fix.Pos_lat, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lat);
                            double.TryParse(fix.Pos_long, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lon);
                            double.TryParse(fix.Altitude_feet ?? "0", System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double alt);

                            waypoints.Add(new
                            {
                                ident = fix.Ident,
                                name = fix.Name,
                                type = "ARR " + (fix.Type ?? "wpt"),
                                lat = lat,
                                lon = lon,
                                alt = alt,
                                isSidStar = fix.Is_sid_star == "1"
                            });
                        }
                    }
                }

                return new
                {
                    source = "SimBrief",
                    arrival = arrivalIcao,
                    runway = ofp.Destination?.Plan_rwy,
                    starName = starName,
                    waypointCount = waypoints.Count,
                    waypoints = waypoints
                };
            }
            catch (Exception ex)
            {
                KneeboardLogger.SimBriefError($"Error extracting STAR waypoints: {ex.Message}");
                return new { error = ex.Message, waypoints = new List<object>() };
            }
        }

        /// <summary>
        /// Gets combined SID and STAR data from SimBrief
        /// </summary>
        public static object GetSimbriefProcedures()
        {
            try
            {
                if (string.IsNullOrEmpty(simbriefOFPData))
                {
                    return new { error = "No SimBrief data loaded" };
                }

                var ofp = JsonConvert.DeserializeObject<Simbrief.OFP>(simbriefOFPData);
                if (ofp?.Navlog?.Fix == null)
                {
                    return new { error = "No navlog in SimBrief data" };
                }

                // Parse route for SID/STAR names
                string sidName = "", starName = "";
                if (!string.IsNullOrEmpty(ofp.General?.Route))
                {
                    var routeParts = ofp.General.Route.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (routeParts.Length > 0)
                    {
                        sidName = routeParts[0].Split('/')[0];
                        starName = routeParts[routeParts.Length - 1];
                    }
                }

                string departureIcao = ofp.Origin?.Icao_code;
                string arrivalIcao = ofp.Destination?.Icao_code;

                var sidWaypoints = new List<object>();
                var starWaypoints = new List<object>();

                // Extract SID/STAR waypoints from SimBrief navlog
                bool foundFirstSidWaypoint = false;

                foreach (var fix in ofp.Navlog.Fix)
                {
                    double.TryParse(fix.Pos_lat, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lat);
                    double.TryParse(fix.Pos_long, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lon);
                    double.TryParse(fix.Altitude_feet ?? "0", System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double alt);

                    // SID: All CLB waypoints from first Is_sid_star onwards
                    if (fix.Stage == "CLB")
                    {
                        if (fix.Type == "apt" && fix.Ident == ofp.Origin?.Icao_code)
                            continue;

                        if (fix.Is_sid_star == "1")
                            foundFirstSidWaypoint = true;

                        if (foundFirstSidWaypoint || fix.Is_sid_star == "1")
                        {
                            sidWaypoints.Add(new
                            {
                                ident = fix.Ident,
                                name = fix.Name,
                                type = "DEP " + (fix.Type ?? "wpt"),
                                lat = lat,
                                lon = lon,
                                alt = alt,
                                isSidStar = fix.Is_sid_star == "1"
                            });
                        }
                    }
                    // STAR: Only waypoints that are actually part of the STAR procedure
                    else if (fix.Stage == "DSC")
                    {
                        // NUR echte STAR-Waypoints als ARR markieren (Is_sid_star == "1")
                        // Andere DSC-Waypoints bleiben ENROUTE (werden nicht hinzugefügt)
                        if (fix.Is_sid_star == "1")
                        {
                            starWaypoints.Add(new
                            {
                                ident = fix.Ident,
                                name = fix.Name,
                                type = "ARR " + (fix.Type ?? "wpt"),
                                lat = lat,
                                lon = lon,
                                alt = alt,
                                isSidStar = true
                            });
                        }
                    }
                }

                return new
                {
                    source = "SimBrief",
                    airac = ofp.Params?.Airac,
                    departure = new
                    {
                        icao = departureIcao,
                        runway = ofp.Origin?.Plan_rwy,
                        name = ofp.Origin?.Name,
                        lat = double.TryParse(ofp.Origin?.Pos_lat, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double depLat) ? depLat : 0,
                        lon = double.TryParse(ofp.Origin?.Pos_long, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double depLon) ? depLon : 0
                    },
                    arrival = new
                    {
                        icao = arrivalIcao,
                        runway = ofp.Destination?.Plan_rwy,
                        name = ofp.Destination?.Name,
                        lat = double.TryParse(ofp.Destination?.Pos_lat, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double arrLat) ? arrLat : 0,
                        lon = double.TryParse(ofp.Destination?.Pos_long, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double arrLon) ? arrLon : 0
                    },
                    sid = new
                    {
                        name = sidName,
                        waypointCount = sidWaypoints.Count,
                        waypoints = sidWaypoints
                    },
                    star = new
                    {
                        name = starName,
                        waypointCount = starWaypoints.Count,
                        waypoints = starWaypoints
                    }
                };
            }
            catch (Exception ex)
            {
                KneeboardLogger.SimBriefError($"Error getting procedures: {ex.Message}");
                return new { error = ex.Message };
            }
        }

        /// <summary>
        /// Loads persisted flightplan data from Settings on startup
        /// </summary>
        public static void LoadPersistedFlightplanData()
        {
            try
            {
                string persistedFlightplan = Properties.Settings.Default.cachedFlightplan;
                string persistedTimestamp = Properties.Settings.Default.cachedSimbriefTimeGenerated;
                string persistedOFPData = Properties.Settings.Default.cachedSimbriefOFPData;

                if (!string.IsNullOrEmpty(persistedFlightplan))
                {
                    flightplan = persistedFlightplan;
                    KneeboardLogger.PersistenceDebug($"Loaded flightplan from settings (length: {flightplan.Length})");
                }

                if (!string.IsNullOrEmpty(persistedTimestamp))
                {
                    cachedSimbriefTimeGenerated = persistedTimestamp;
                    KneeboardLogger.PersistenceDebug($"Loaded SimBrief timestamp: {cachedSimbriefTimeGenerated}");
                }

                if (!string.IsNullOrEmpty(persistedOFPData))
                {
                    simbriefOFPData = persistedOFPData;
                    KneeboardLogger.PersistenceDebug($"Loaded OFP data from settings (length: {simbriefOFPData.Length})");
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.PersistenceError($"Error loading persisted data: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves flightplan data to Settings for persistence
        /// </summary>
        public static void SaveFlightplanDataToSettings()
        {
            try
            {
                Properties.Settings.Default.cachedFlightplan = flightplan ?? "";
                Properties.Settings.Default.cachedSimbriefTimeGenerated = cachedSimbriefTimeGenerated ?? "";
                Properties.Settings.Default.cachedSimbriefOFPData = simbriefOFPData ?? "";
                Properties.Settings.Default.Save();
                KneeboardLogger.Persistence("Flightplan data saved to settings");
            }
            catch (Exception ex)
            {
                KneeboardLogger.PersistenceError($"Error saving data: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears all persisted flightplan data
        /// </summary>
        public static void ClearPersistedFlightplanData()
        {
            try
            {
                Properties.Settings.Default.cachedFlightplan = "";
                Properties.Settings.Default.cachedSimbriefTimeGenerated = "";
                Properties.Settings.Default.cachedSimbriefOFPData = "";
                Properties.Settings.Default.Save();
                KneeboardLogger.Persistence("Flightplan data cleared from settings");

                // OFP PDF aus Dokumentenliste entfernen
                if (instance != null) // Prüfen ob Instanz existiert
                {
                    if (instance.RemoveSimbriefOFPFromDocumentList())
                    {
                        instance.UpdateFileList();
                        instance.SaveDocumentState();
                        KneeboardLogger.SimBrief("OFP PDF removed from document list");
                    }
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.PersistenceError($"Error clearing data: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes OFP from document list without clearing SimBrief cache
        /// Called when user deletes flightplan from map (soft clear)
        /// </summary>
        public static void RemoveOFPFromDocumentListOnly()
        {
            try
            {
                if (instance != null)
                {
                    if (instance.InvokeRequired)
                    {
                        instance.BeginInvoke(new Action(() =>
                        {
                            if (instance.RemoveSimbriefOFPFromDocumentList())
                            {
                                instance.UpdateFileList();
                                instance.SaveDocumentState();
                                KneeboardLogger.SimBrief("OFP removed from document list (soft clear)");
                            }
                        }));
                    }
                    else
                    {
                        if (instance.RemoveSimbriefOFPFromDocumentList())
                        {
                            instance.UpdateFileList();
                            instance.SaveDocumentState();
                            KneeboardLogger.SimBrief("OFP removed from document list (soft clear)");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.SimBriefError($"Error removing OFP from list: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts the background SimBrief sync timer
        /// </summary>
        public static void StartBackgroundSimbriefSync()
        {
            if (string.IsNullOrEmpty(Properties.Settings.Default.simbriefId))
            {
                KneeboardLogger.Warn("SimBrief", "No SimBrief ID configured, skipping background sync");
                return;
            }

            // Stop existing timer if any
            StopBackgroundSimbriefSync();

            KneeboardLogger.SimBrief("Starting background sync timer (interval: 3 min)");

            // Initial sync immediately, then every 3 minutes
            simbriefBackgroundTimer = new System.Threading.Timer(
                BackgroundSimbriefSyncCallback,
                null,
                0, // Start immediately
                SIMBRIEF_CHECK_INTERVAL_MS
            );
        }

        /// <summary>
        /// Stops the background SimBrief sync timer
        /// </summary>
        public static void StopBackgroundSimbriefSync()
        {
            if (simbriefBackgroundTimer != null)
            {
                simbriefBackgroundTimer.Dispose();
                simbriefBackgroundTimer = null;
                KneeboardLogger.SimBrief("Background sync timer stopped");
            }
        }



        /// <summary>
        /// Background callback that checks and loads SimBrief data
        /// </summary>
        private static void BackgroundSimbriefSyncCallback(object state)
        {
            // Prevent concurrent syncs
            lock (simbriefSyncLock)
            {
                if (isBackgroundSyncRunning) return;
                isBackgroundSyncRunning = true;
            }

            try
            {
                if (string.IsNullOrEmpty(Properties.Settings.Default.simbriefId))
                {
                    return;
                }

                KneeboardLogger.SimBriefDebug("Background: Checking for updates...");

                var url = GetSimbriefApiUrl();
                string xmlStr;
                using (var wc = new WebClient())
                {
                    xmlStr = wc.DownloadString(url);
                }

                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xmlStr);

                // Get current time_generated
                var timeNode = xmlDoc.SelectSingleNode("/OFP/params/time_generated");
                string currentTimeGenerated = (timeNode?.InnerText ?? "").Trim();

                // Check if this is a new/updated flightplan
                if (!string.IsNullOrEmpty(cachedSimbriefTimeGenerated) &&
                    currentTimeGenerated == cachedSimbriefTimeGenerated)
                {
                    KneeboardLogger.SimBriefDebug($"Background: No update (timestamp unchanged: {cachedSimbriefTimeGenerated})");
                    return;
                }

                KneeboardLogger.SimBrief($"Background: New flightplan detected! (old: {cachedSimbriefTimeGenerated}, new: {currentTimeGenerated})");

                // Parse OFP data
                using (StringReader stringReader = new StringReader(xmlStr))
                {
                    XmlSerializer ofpSerializer = new XmlSerializer(typeof(Simbrief.OFP));
                    Simbrief.OFP ofpData = (Simbrief.OFP)ofpSerializer.Deserialize(stringReader);
                    simbriefOFPData = Newtonsoft.Json.JsonConvert.SerializeObject(ofpData);
                    KneeboardLogger.SimBriefDebug($"Background: OFP data loaded (length: {simbriefOFPData.Length})");
                }

                // Get PLN download URL and load waypoints
                var plnNode = xmlDoc.DocumentElement.SelectSingleNode("/OFP/fms_downloads/mfs/link");
                if (plnNode != null)
                {
                    string plnUrl = plnNode.InnerText;
                    using (var wc = new WebClient())
                    using (XmlReader reader = XmlReader.Create(new MemoryStream(wc.DownloadData(plnUrl))))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(SimBaseDocument));
                        SimBaseDocument waypoints = (SimBaseDocument)serializer.Deserialize(reader);

                        var combinedData = new
                        {
                            pln = waypoints,
                            ofp = simbriefOFPData != null ? Newtonsoft.Json.JsonConvert.DeserializeObject(simbriefOFPData) : null
                        };
                        flightplan = Newtonsoft.Json.JsonConvert.SerializeObject(combinedData);
                        KneeboardLogger.SimBriefDebug($"Background: Flightplan loaded (length: {flightplan.Length})");
                    }
                }

                // Update cached timestamp
                cachedSimbriefTimeGenerated = currentTimeGenerated;

                // Persist to settings
                SaveFlightplanDataToSettings();

                // OFP PDF herunterladen und zur Liste hinzufügen
                var ofpNode = xmlDoc.DocumentElement.SelectSingleNode("/OFP/files/pdf/link");
                if (ofpNode != null)
                {
                    string simbriefOFP = ofpNode.InnerText;

                    // Simbrief Ordner erstellen falls nicht vorhanden
                    if (!Directory.Exists(simbriefDataDir))
                    {
                        Directory.CreateDirectory(simbriefDataDir);
                    }

                    // PDF herunterladen
                    using (var client = new WebClient())
                    {
                        client.DownloadFile(
                            new Uri("https://www.simbrief.com/ofp/flightplans/" + simbriefOFP),
                            Path.Combine(simbriefDataDir, "OFP.pdf")
                        );
                    }

                    // Zur Dokumentenliste hinzufügen (Thread-safe UI-Update)
                    if (instance != null)
                    {
                        if (instance.InvokeRequired)
                        {
                            instance.BeginInvoke(new Action(() =>
                            {
                                if (instance.AddSimbriefOFPToDocumentList())
                                {
                                    instance.UpdateFileList();
                                    instance.SaveDocumentState();
                                    KneeboardLogger.SimBrief("Background: OFP PDF updated in document list");
                                }
                            }));
                        }
                        else
                        {
                            if (instance.AddSimbriefOFPToDocumentList())
                            {
                                instance.UpdateFileList();
                                instance.SaveDocumentState();
                                KneeboardLogger.SimBrief("Background: OFP PDF updated in document list");
                            }
                        }
                    }
                }

                KneeboardLogger.SimBrief("Background sync complete - data ready for instant use");
            }
            catch (Exception ex)
            {
                KneeboardLogger.SimBriefError($"Background: Error during sync: {ex.Message}");
            }
            finally
            {
                lock (simbriefSyncLock)
                {
                    isBackgroundSyncRunning = false;
                }
            }
        }

        // Enum to track the source of the last imported flightplan
        private enum FlightplanSource { None, SimBrief, LocalPLN }
        private static FlightplanSource lastFlightplanSource = FlightplanSource.None;
        bool serverRun = Properties.Settings.Default.serverRun;
        SimpleHTTPServer myServer;
        bool imagesProcessing = false;
        public List<KneeboardFolder> foldersList = new List<KneeboardFolder>();
        public List<KneeboardFile> filesList = new List<KneeboardFile>();

        public class KneeboardFile
        {
            public string Name { get; set; }
            public int Pages { get; set; }
            public string Path { get; set; }
            public KneeboardFile()
            {
            }
            public KneeboardFile(string name, int pages, string path)
            {
                Name = name;
                Pages = pages;
                Path = path;
            }
        }

        public class KneeboardFolder
        {
            public string Name { get; set; }
            public List<KneeboardFile> Files { get; set; } = new List<KneeboardFile>();
            public KneeboardFolder()
            {
            }
            public KneeboardFolder(string name, List<KneeboardFile> files)
            {
                Name = name;
                Files = files ?? new List<KneeboardFile>();
            }
        }

        private class DocumentState
        {
            public List<KneeboardFile> Files { get; set; } = new List<KneeboardFile>();
            public List<KneeboardFolder> Folders { get; set; } = new List<KneeboardFolder>();
        }

        private void LoadDocumentState()
        {
            try
            {
                string serialized = Properties.Settings.Default.documentState;
                if (string.IsNullOrWhiteSpace(serialized))
                {
                    filesList = filesList ?? new List<KneeboardFile>();
                    foldersList = foldersList ?? new List<KneeboardFolder>();
                    return;
                }

                DocumentState snapshot = JsonConvert.DeserializeObject<DocumentState>(serialized);
                filesList = snapshot?.Files ?? new List<KneeboardFile>();
                foldersList = snapshot?.Folders ?? new List<KneeboardFolder>();
            }
            catch (Exception ex)
            {
                KneeboardLogger.ServerError("Failed to load document state: " + ex.Message);
                filesList = filesList ?? new List<KneeboardFile>();
                foldersList = foldersList ?? new List<KneeboardFolder>();
            }
        }

        private void SaveDocumentState()
        {
            try
            {
                DocumentState snapshot = new DocumentState
                {
                    Files = filesList ?? new List<KneeboardFile>(),
                    Folders = foldersList ?? new List<KneeboardFolder>()
                };

                Properties.Settings.Default.documentState = JsonConvert.SerializeObject(snapshot);
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                KneeboardLogger.ServerError("Failed to save document state: " + ex.Message);
            }
        }

        private bool AddSimbriefOFPToDocumentList()
        {
            try
            {
                // 1. Pfad zum OFP PDF definieren
                string ofpPath = Path.Combine(simbriefDataDir, "OFP.pdf");

                // 2. Überprüfen ob Datei existiert
                if (!System.IO.File.Exists(ofpPath))
                    return false;

                // 3. "Simbrief" Ordner in foldersList finden oder erstellen
                KneeboardFolder simbriefFolder = foldersList.FirstOrDefault(f => f.Name == "Simbrief");
                if (simbriefFolder == null)
                {
                    simbriefFolder = new KneeboardFolder("Simbrief", new List<KneeboardFile>());
                    foldersList.Add(simbriefFolder);
                }

                // 4. Alte OFP Datei entfernen (falls vorhanden)
                simbriefFolder.Files.RemoveAll(x => x.Name == "OFP");

                // 5. Neue OFP Datei hinzufügen
                KneeboardFile ofpFile = new KneeboardFile(
                    name: "OFP",
                    pages: 0,  // Wird von CreateImages() berechnet
                    path: ofpPath
                );
                simbriefFolder.Files.Add(ofpFile);

                return true;
            }
            catch (Exception ex)
            {
                KneeboardLogger.SimBriefError($"Error adding Simbrief OFP to document list: {ex.Message}");
                return false;
            }
        }

        private bool RemoveSimbriefOFPFromDocumentList()
        {
            try
            {
                // Simbrief Ordner finden
                KneeboardFolder simbriefFolder = foldersList.FirstOrDefault(f => f.Name == "Simbrief");
                if (simbriefFolder == null)
                    return false; // Kein Simbrief Ordner vorhanden

                // OFP Datei entfernen
                int removed = simbriefFolder.Files.RemoveAll(x => x.Name == "OFP");

                // Wenn Ordner jetzt leer ist, auch Ordner entfernen
                if (simbriefFolder.Files.Count == 0)
                {
                    foldersList.Remove(simbriefFolder);
                }

                KneeboardLogger.SimBrief($"Removed {removed} OFP file(s) from document list");
                return removed > 0;
            }
            catch (Exception ex)
            {
                KneeboardLogger.SimBriefError($"Error removing Simbrief OFP from document list: {ex.Message}");
                return false;
            }
        }

        private void EnsureDefaultManualsExist()
        {
            try
            {
                // Check if manuals have already been initialized
                // If yes, skip this process (respects user's decision to remove manuals)
                if (Properties.Settings.Default.manualsInitialized)
                {
                    return;
                }

                string manualsPath = Path.Combine(folderpath, "data", "manuals");

                if (!Directory.Exists(manualsPath))
                {
                    KneeboardLogger.Warn("Server", "Manuals directory does not exist: " + manualsPath);
                    // Mark as initialized even if directory doesn't exist to avoid repeated checks
                    Properties.Settings.Default.manualsInitialized = true;
                    Properties.Settings.Default.Save();
                    return;
                }

                // Find all PDF files in the manuals directory
                string[] manualFiles = Directory.GetFiles(manualsPath, "*.pdf", SearchOption.TopDirectoryOnly);

                if (manualFiles.Length == 0)
                {
                    KneeboardLogger.Warn("Server", "No manual PDF files found in: " + manualsPath);
                    // Mark as initialized to avoid repeated checks
                    Properties.Settings.Default.manualsInitialized = true;
                    Properties.Settings.Default.Save();
                    return;
                }

                bool stateChanged = false;

                foreach (string manualPath in manualFiles)
                {
                    string manualName = Path.GetFileNameWithoutExtension(manualPath);

                    // Check if manual already exists in the list
                    bool alreadyExists = filesList.Any(f =>
                        f.Name.Equals(manualName, StringComparison.OrdinalIgnoreCase) ||
                        f.Path.Equals(manualPath, StringComparison.OrdinalIgnoreCase));

                    if (!alreadyExists)
                    {
                        // Add manual to the documents list
                        KneeboardFile manualFile = new KneeboardFile(
                            name: manualName,
                            pages: 0,
                            path: manualPath
                        );

                        filesList.Add(manualFile);
                        stateChanged = true;
                        KneeboardLogger.Server("Added manual to documents list: " + manualName);
                    }
                }

                // Mark manuals as initialized (first run completed)
                Properties.Settings.Default.manualsInitialized = true;
                Properties.Settings.Default.Save();

                // Save document state if any manuals were added
                if (stateChanged)
                {
                    SaveDocumentState();
                    KneeboardLogger.Server("Document state saved with new manuals");
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.ServerError("Error ensuring default manuals exist: " + ex.Message);
            }
        }


        public Kneeboard_Server()
        {
            instance = this;
            InitializeComponent();

            // Load persisted flightplan data from previous session
            LoadPersistedFlightplanData();

            // Start background SimBrief sync to keep OFP data current
            StartBackgroundSimbriefSync();

            // Initialize SimConnect (optional - only works if MSFS is installed)
            try
            {
                simConnectManager = new SimConnectManager(this.Handle);
                simConnectManager.ConnectionStatusChanged += OnSimConnectStatusChanged;
                simConnectManager.Start();
                KneeboardLogger.Server("SimConnect manager started");
            }
            catch (FileNotFoundException)
            {
                KneeboardLogger.Warn("Server", "SimConnect DLL not found - MSFS not installed, continuing without SimConnect");
                simConnectManager = null;
            }
            catch (Exception ex)
            {
                KneeboardLogger.ServerError($"SimConnect initialization failed: {ex.Message}");
                simConnectManager = null;
            }

            // First-start: combined setup wizard (Panel, Autostart, SimBrief)
            bool isFirstStart = Properties.Settings.Default.firstStart
                             || !Properties.Settings.Default.firstAutoStart
                             || !Properties.Settings.Default.firstSimbriefAsk;
            if (isFirstStart)
            {
                ShowFirstStartWizard();
                Properties.Settings.Default.firstStart = false;
                Properties.Settings.Default.firstAutoStart = true;
                Properties.Settings.Default.firstSimbriefAsk = true;
                Properties.Settings.Default.Save();
            }

            //delete hover color
            loadButton.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Transparent;
            addFileButton.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Transparent;
            addFolderButton.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Transparent;
            deleteFileButton.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Transparent;
            deleteFolderButton.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Transparent;
            saveButton.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Transparent;
            editButton.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Transparent;

            // Check for updates via GitHub Releases API, use AutoUpdater for download/install
            if (Properties.Settings.Default.autoUpdateCheck)
            {
                CheckForGitHubUpdate();
            }
        }

        //Update check

        bool updateAvailable = false;
        bool simConnectConnected = false;
        private string pendingUpdateUrl = null;

        public async void CheckForGitHubUpdate(bool manual = false)
        {
            try
            {
                if (manual)
                {
                    Invoke(new Action(() =>
                    {
                        UpdateMessage.Visible = true;
                        UpdateMessage.Text = " Checking for updates...";
                        UpdateMessage.BackColor = SystemColors.MenuHighlight;
                    }));
                }

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("KneeboardServer");

                    var response = await client.GetStringAsync(
                        "https://api.github.com/repos/G-Simulation/G-Sim-Kneeboard-Server/releases/latest");

                    dynamic release = Newtonsoft.Json.JsonConvert.DeserializeObject(response);
                    string tagName = release.tag_name;
                    string latestVersion = ((string)tagName).TrimStart('v');

                    var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    var remoteVersion = new Version(latestVersion);

                    if (remoteVersion > new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build))
                    {
                        // Find the Setup.zip download URL from release assets
                        string downloadUrl = null;
                        foreach (var asset in release.assets)
                        {
                            string name = asset.name.ToString();
                            if (name.EndsWith("-Setup.zip"))
                            {
                                downloadUrl = asset.browser_download_url;
                                break;
                            }
                        }

                        if (downloadUrl != null)
                        {
                            pendingUpdateUrl = $"https://github.com/G-Simulation/G-Sim-Kneeboard-Server/releases/tag/v{latestVersion}";

                            // Generate temporary XML for AutoUpdater and start download
                            Invoke(new Action(() => StartAutoUpdater(latestVersion, downloadUrl)));
                        }
                    }
                    else if (manual)
                    {
                        Invoke(new Action(() =>
                        {
                            UpdateMessage.Visible = true;
                            UpdateMessage.Text = $" You are up to date (v{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build})";
                            UpdateMessage.BackColor = Color.Green;
                        }));

                        // Hide after 3 seconds
                        await System.Threading.Tasks.Task.Delay(3000);
                        Invoke(new Action(() =>
                        {
                            if (!updateAvailable)
                            {
                                UpdateMessage.Visible = false;
                                UpdateStatusBar();
                            }
                        }));
                    }
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.ServerError($"Update check failed: {ex.Message}");
                if (manual)
                {
                    Invoke(new Action(() =>
                    {
                        UpdateMessage.Visible = true;
                        UpdateMessage.Text = " Update check failed. No internet?";
                        UpdateMessage.BackColor = Color.Red;
                    }));

                    await System.Threading.Tasks.Task.Delay(3000);
                    Invoke(new Action(() =>
                    {
                        if (!updateAvailable)
                        {
                            UpdateMessage.Visible = false;
                            UpdateStatusBar();
                        }
                    }));
                }
            }
        }

        private void StartAutoUpdater(string version, string downloadUrl)
        {
            // Create temporary XML that AutoUpdater expects
            string xmlContent = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                $"<item>\n" +
                $"    <version>{version}</version>\n" +
                $"    <url>{downloadUrl}</url>\n" +
                $"    <mandatory>false</mandatory>\n" +
                $"    <executable>Kneeboard Server.msi</executable>\n" +
                $"</item>";

            string xmlPath = Path.Combine(Path.GetTempPath(), "Kneeboard_version.xml");
            System.IO.File.WriteAllText(xmlPath, xmlContent);

            AutoUpdater.CheckForUpdateEvent += AutoUpdaterOnCheckForUpdateEvent;
            AutoUpdater.RunUpdateAsAdmin = true;
            AutoUpdater.DownloadPath = Path.GetTempPath();
            AutoUpdater.Mandatory = true;
            AutoUpdater.UpdateMode = Mode.ForcedDownload;
            AutoUpdater.Start("file://" + xmlPath);
        }

        private void AutoUpdaterOnCheckForUpdateEvent(UpdateInfoEventArgs args)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => AutoUpdaterOnCheckForUpdateEvent(args)));
                return;
            }

            if (args.Error == null)
            {
                if (args.IsUpdateAvailable)
                {
                    UpdateMessage.Visible = true;
                    UpdateMessage.Text = $" Downloading update {args.CurrentVersion}...";
                    UpdateMessage.BackColor = Color.Orange;
                    updateAvailable = true;

                    try
                    {
                        if (AutoUpdater.DownloadUpdate(args))
                        {
                            Application.Exit();
                        }
                    }
                    catch (Exception ex)
                    {
                        KneeboardLogger.ServerError($"Auto-update failed: {ex.Message}");
                        UpdateMessage.Text = $" Update {args.CurrentVersion} available. Click here for download.";
                        UpdateMessage.BackColor = Color.Red;
                    }
                }
                else
                {
                    UpdateMessage.Visible = false;
                    UpdateStatusBar();
                    statusBox.BackColor = SystemColors.MenuHighlight;
                    updateAvailable = false;
                }
            }
            else
            {
                KneeboardLogger.ServerError($"Update check failed: {args.Error.Message}");
                UpdateMessage.Visible = false;
                UpdateStatusBar();
                updateAvailable = false;
            }
        }

        /// <summary>
        /// Public method to update the status box text from other classes (like SimpleHTTPServer)
        /// </summary>
        public void SetStatusText(string text)
        {
            if (statusBox.InvokeRequired)
            {
                statusBox.Invoke(new Action(() => statusBox.Text = text));
            }
            else
            {
                statusBox.Text = text;
            }
        }

        /// <summary>
        /// Zeigt beim ersten Start einen kombinierten Setup-Dialog.
        /// Enthält: Kneeboard Panel Installation, Autostart, SimBrief-Integration.
        /// </summary>
        private void ShowFirstStartWizard()
        {
            try
            {
                var installations = MsfsPathDetector.DetectMsfsInstallations();
                bool hasSource = PanelDeploymentService.SourceExists();

                using (var form = new Form())
                {
                    form.Text = "Kneeboard Server Setup";
                    form.StartPosition = FormStartPosition.CenterScreen;
                    form.FormBorderStyle = FormBorderStyle.None;
                    form.BackColor = SystemColors.Window;
                    form.Paint += (s, ev) =>
                    {
                        ControlPaint.DrawBorder(ev.Graphics, form.ClientRectangle,
                            SystemColors.Highlight, ButtonBorderStyle.Solid);
                    };

                    int yPos = 8;

                    // ── Titel ──
                    var titleLabel = new System.Windows.Forms.Label
                    {
                        Text = "Kneeboard Server Setup",
                        Location = new Point(12, yPos),
                        Size = new Size(440, 28),
                        Font = new Font("Segoe UI", 14.25F, FontStyle.Bold),
                        ForeColor = SystemColors.Highlight
                    };
                    form.Controls.Add(titleLabel);
                    yPos += 30;

                    var welcomeLabel = new System.Windows.Forms.Label
                    {
                        Text = "Willkommen! Bitte konfiguriere die folgenden Einstellungen:",
                        Location = new Point(12, yPos),
                        Size = new Size(440, 18),
                        Font = new Font("Segoe UI", 8.25F),
                        ForeColor = SystemColors.ControlDarkDark
                    };
                    form.Controls.Add(welcomeLabel);
                    yPos += 28;

                    // ── 1. Autostart ──
                    var autostartGroupBox = new System.Windows.Forms.GroupBox
                    {
                        Text = "Autostart",
                        Location = new Point(12, yPos),
                        Size = new Size(446, 45),
                        ForeColor = SystemColors.Highlight,
                        Font = new Font("Segoe UI", 8.25F)
                    };
                    var autostartCb = new System.Windows.Forms.CheckBox
                    {
                        Text = "Kneeboard Server mit MSFS automatisch starten",
                        Location = new Point(10, 18),
                        Size = new Size(420, 20),
                        Checked = true,
                        Font = new Font("Segoe UI", 8.5F),
                        ForeColor = SystemColors.ControlText
                    };
                    autostartGroupBox.Controls.Add(autostartCb);
                    form.Controls.Add(autostartGroupBox);
                    yPos += 52;

                    // ── 2. SimBrief ──
                    var simbriefGroupBox = new System.Windows.Forms.GroupBox
                    {
                        Text = "SimBrief",
                        Location = new Point(12, yPos),
                        Size = new Size(446, 48),
                        ForeColor = SystemColors.Highlight,
                        Font = new Font("Segoe UI", 8.25F)
                    };
                    var simbriefLabel = new System.Windows.Forms.Label
                    {
                        Text = "Pilot ID / Username:",
                        Location = new Point(10, 20),
                        Size = new Size(120, 18),
                        Font = new Font("Segoe UI", 8.5F),
                        ForeColor = SystemColors.ControlText
                    };
                    var simbriefInput = new System.Windows.Forms.TextBox
                    {
                        Location = new Point(135, 17),
                        Size = new Size(300, 22),
                        Font = new Font("Segoe UI", 8.5F),
                        Text = Properties.Settings.Default.simbriefId ?? ""
                    };
                    simbriefGroupBox.Controls.Add(simbriefLabel);
                    simbriefGroupBox.Controls.Add(simbriefInput);
                    form.Controls.Add(simbriefGroupBox);
                    yPos += 55;

                    // ── 3. Kneeboard Panel Installation ──
                    var panelCheckboxes = new System.Collections.Generic.List<System.Windows.Forms.CheckBox>();
                    System.Windows.Forms.CheckBox manualCb = null;
                    System.Windows.Forms.TextBox manualPathBox = null;

                    if (hasSource)
                    {
                        var panelGroupBox = new System.Windows.Forms.GroupBox
                        {
                            Text = "Kneeboard Panel Installation",
                            Location = new Point(12, yPos),
                            ForeColor = SystemColors.Highlight,
                            Font = new Font("Segoe UI", 8.25F)
                        };

                        int innerY = 18;

                        foreach (var inst in installations)
                        {
                            var info = MsfsPathDetector.GetInstalledPackageInfo(inst.CommunityPath);
                            string status = info.IsInstalled ? $" [v{info.Version} installiert]" : "";

                            var cb = new System.Windows.Forms.CheckBox
                            {
                                Text = $"{inst.Version} ({inst.Variant}){status}",
                                Tag = inst.CommunityPath,
                                Location = new Point(10, innerY),
                                Size = new Size(420, 20),
                                Checked = !info.IsInstalled,
                                Font = new Font("Segoe UI", 8.5F),
                                ForeColor = SystemColors.ControlText
                            };
                            panelCheckboxes.Add(cb);
                            panelGroupBox.Controls.Add(cb);

                            var pathLabel = new System.Windows.Forms.Label
                            {
                                Text = inst.CommunityPath,
                                Location = new Point(26, innerY + 19),
                                Size = new Size(410, 14),
                                Font = new Font("Segoe UI", 7F),
                                ForeColor = SystemColors.ControlDarkDark
                            };
                            panelGroupBox.Controls.Add(pathLabel);
                            innerY += 36;
                        }

                        // Manuell-Option
                        manualCb = new System.Windows.Forms.CheckBox
                        {
                            Text = "Anderer Pfad:",
                            Location = new Point(10, innerY),
                            Size = new Size(100, 20),
                            Checked = false,
                            Font = new Font("Segoe UI", 8.5F),
                            ForeColor = SystemColors.ControlText
                        };
                        panelGroupBox.Controls.Add(manualCb);

                        manualPathBox = new System.Windows.Forms.TextBox
                        {
                            Location = new Point(115, innerY),
                            Size = new Size(270, 22),
                            Enabled = false
                        };
                        panelGroupBox.Controls.Add(manualPathBox);

                        var browseButton = new System.Windows.Forms.Button
                        {
                            Text = "...",
                            Location = new Point(390, innerY - 1),
                            Size = new Size(35, 24),
                            FlatStyle = FlatStyle.Flat,
                            ForeColor = SystemColors.Highlight,
                            Enabled = false
                        };
                        browseButton.Click += (s, ev) =>
                        {
                            using (var fbd = new FolderBrowserDialog())
                            {
                                fbd.Description = "MSFS Community-Ordner auswählen";
                                if (fbd.ShowDialog() == DialogResult.OK)
                                    manualPathBox.Text = fbd.SelectedPath;
                            }
                        };
                        panelGroupBox.Controls.Add(browseButton);

                        manualCb.CheckedChanged += (s, ev) =>
                        {
                            manualPathBox.Enabled = manualCb.Checked;
                            browseButton.Enabled = manualCb.Checked;
                        };

                        innerY += 30;
                        panelGroupBox.Size = new Size(446, innerY + 5);
                        form.Controls.Add(panelGroupBox);
                        yPos += innerY + 12;
                    }

                    // ── Buttons ──
                    yPos += 5;
                    var okButton = new System.Windows.Forms.Button
                    {
                        Text = "OK",
                        DialogResult = DialogResult.OK,
                        Location = new Point(290, yPos),
                        Size = new Size(80, 28),
                        FlatStyle = FlatStyle.Flat,
                        ForeColor = SystemColors.Highlight
                    };
                    var cancelButton = new System.Windows.Forms.Button
                    {
                        Text = "Überspringen",
                        DialogResult = DialogResult.Cancel,
                        Location = new Point(378, yPos),
                        Size = new Size(80, 28),
                        FlatStyle = FlatStyle.Flat,
                        ForeColor = SystemColors.ControlDarkDark
                    };
                    form.Controls.Add(okButton);
                    form.Controls.Add(cancelButton);
                    form.AcceptButton = okButton;
                    form.CancelButton = cancelButton;
                    form.ClientSize = new Size(470, yPos + 42);

                    if (form.ShowDialog(this) == DialogResult.OK)
                    {
                        // Autostart speichern
                        Properties.Settings.Default.simStart = autostartCb.Checked;

                        // SimBrief speichern
                        string simbriefId = simbriefInput.Text.Trim();
                        if (!string.IsNullOrEmpty(simbriefId))
                        {
                            Properties.Settings.Default.simbriefId = simbriefId;
                            StartBackgroundSimbriefSync();
                        }

                        // Kneeboard Panel deployen
                        if (hasSource)
                        {
                            int deployed = 0;
                            string lastPath = null;

                            foreach (var cb in panelCheckboxes)
                            {
                                if (cb.Checked)
                                {
                                    string path = (string)cb.Tag;
                                    PanelDeploymentService.DeployPanel(path, null);
                                    lastPath = path;
                                    deployed++;
                                    KneeboardLogger.Server($"Deployed Kneeboard panel to {path}");
                                }
                            }

                            if (manualCb != null && manualCb.Checked && !string.IsNullOrEmpty(manualPathBox.Text))
                            {
                                string path = manualPathBox.Text.Trim();
                                PanelDeploymentService.DeployPanel(path, null);
                                lastPath = path;
                                deployed++;
                                KneeboardLogger.Server($"Deployed Kneeboard panel to {path}");
                            }

                            if (lastPath != null)
                            {
                                Properties.Settings.Default.communityFolderPath = lastPath;
                            }
                        }

                        Properties.Settings.Default.Save();
                    }
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.ServerError($"First-start wizard error: {ex.Message}");
            }
        }

        /// <summary>
        /// Event handler for SimConnect connection status changes
        /// </summary>
        private void OnSimConnectStatusChanged(bool connected)
        {
            simConnectConnected = connected;
            UpdateStatusBar();


        }

        /// <summary>
        /// Updates the status bar with current server and SimConnect status
        /// </summary>
        private void UpdateStatusBar()
        {
            string status;
            if (serverRun)
            {
                status = simConnectConnected
                    ? "Status: Server is running... | MSFS Connected"
                    : "Status: Server is running...";
            }
            else
            {
                status = "Status: Server is not running...";
            }

            if (statusBox.InvokeRequired)
            {
                statusBox.Invoke(new Action(() => statusBox.Text = status));
            }
            else
            {
                statusBox.Text = status;
            }
        }

        /// <summary>
        /// Returns the SimBrief API URL based on whether user entered a numeric ID or username
        /// </summary>
        private static string GetSimbriefApiUrl()
        {
            string input = Properties.Settings.Default.simbriefId;
            string baseUrl = "https://www.simbrief.com/api/xml.fetcher.php?";

            // Check if input is numeric (Pilot ID) or alphanumeric (Username)
            if (int.TryParse(input, out _))
            {
                return baseUrl + "userid=" + input;
            }
            else
            {
                return baseUrl + "username=" + input;
            }
        }

        private readonly int tolerance = 16;
        private const int WM_NCHITTEST = 132;
        private const int HTBOTTOMRIGHT = 17;
        private System.Drawing.Rectangle sizeGripRectangle;

        /// <summary>
        /// Add application to Startup of windows
        /// </summary>
        /// <param name="appName"></param>
        /// <param name="path"></param>

        public static void WriteExeXML()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // Alle möglichen exe.xml Pfade für MSFS 2020 und 2024 (Store und Steam)
            var exeXmlPaths = new List<string>
            {
                // MSFS 2024 Store
                Path.Combine(localAppData, @"Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\exe.xml"),
                // MSFS 2024 Steam
                Path.Combine(roamingAppData, @"Microsoft Flight Simulator 2024\exe.xml"),
                // MSFS 2020 Store
                Path.Combine(localAppData, @"Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\exe.xml"),
                // MSFS 2020 Steam
                Path.Combine(roamingAppData, @"Microsoft Flight Simulator\exe.xml")
            };

            // Falls ein benutzerdefinierter Pfad gesetzt ist, diesen zuerst prüfen
            if (!string.IsNullOrEmpty(Properties.Settings.Default.exeXmlPath) &&
                Properties.Settings.Default.exeXmlPath != "Path to exe.xml")
            {
                exeXmlPaths.Insert(0, Properties.Settings.Default.exeXmlPath);
            }

            foreach (string exeXmlPath in exeXmlPaths)
            {
                if (System.IO.File.Exists(exeXmlPath))
                {
                    try
                    {
                        var doc = XDocument.Load(exeXmlPath);

                        // Bestehenden Kneeboard-Eintrag immer entfernen (wird danach neu geschrieben)
                        var existingEntries = doc.Descendants("Launch.Addon")
                            .Where(x => x.Element("Name")?.Value == "Kneeboard Server")
                            .ToList();
                        foreach (var entry in existingEntries)
                        {
                            entry.Remove();
                        }

                        // Neuen Eintrag mit aktuellem Pfad hinzufügen wenn simStart aktiviert
                        if (Properties.Settings.Default.simStart == true)
                        {
                            var newElement = new XElement("Launch.Addon",
                                new XElement("Name", "Kneeboard Server"),
                                new XElement("Disabled", "false"),
                                new XElement("Path", Application.ExecutablePath));
                            doc.Element("SimBase.Document").Add(newElement);
                        }

                        doc.Save(exeXmlPath);

                        KneeboardLogger.ServerDebug($"Processed exe.xml: {exeXmlPath}");
                    }
                    catch (Exception ex)
                    {
                        KneeboardLogger.ServerError($"Error processing exe.xml at {exeXmlPath}: {ex.Message}");
                    }
                }
            }
        }

        protected override void WndProc(ref Message m)
        {
            // Handle SimConnect messages first
            if (simConnectManager != null)
            {
                try
                {
                    simConnectManager.HandleWindowMessage(ref m);
                }
                catch (FileNotFoundException)
                {
                    // SimConnect DLL not found - MSFS not installed, ignore silently
                }
                catch (Exception ex)
                {
                    KneeboardLogger.SimConnectError($"WndProc error: {ex.Message}");
                }
            }

            switch (m.Msg)
            {
                case WM_NCHITTEST:
                    base.WndProc(ref m);
                    var hitPoint = this.PointToClient(new Point(m.LParam.ToInt32() & 0xffff, m.LParam.ToInt32() >> 16));
                    if (sizeGripRectangle.Contains(hitPoint))
                        m.Result = new IntPtr(HTBOTTOMRIGHT);
                    break;
                default:
                    base.WndProc(ref m);
                    break;
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            var region = new Region(new System.Drawing.Rectangle(0, 0, this.ClientRectangle.Width, this.ClientRectangle.Height));
            sizeGripRectangle = new System.Drawing.Rectangle(this.ClientRectangle.Width - tolerance, this.ClientRectangle.Height - tolerance, tolerance, tolerance);
            region.Exclude(sizeGripRectangle);
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ControlPaint.DrawBorder(e.Graphics, ClientRectangle, SystemColors.Highlight, ButtonBorderStyle.Solid);
        }


        private void KneeboardServer_Load(object sender, EventArgs e)
        {
            // Pfad ermitteln: Debug nutzt IMMER Source-Ordner (JS/CSS sofort sichtbar ohne Rebuild).
            // Release nutzt exe-Verzeichnis (installierte Version).
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
#if DEBUG
            folderpath = Path.GetFullPath(Path.Combine(baseDir, @"..\..\.."));
#else
            folderpath = baseDir;
#endif
            KneeboardLogger.Server($"Data directory: {folderpath}\\data");

            KneeboardLogger.Initialize(consoleOutput: true, fileOutput: true, minLevel: KneeboardLogger.Level.INFO);
            KneeboardLogger.Server("Kneeboard Server starting...");
            KneeboardLogger.CleanupOldLogs(keepDays: 7);
            
            //MessageBox.Show(folderpath);
            LoadDocumentState();

            // OFP ist ein Runtime-Dokument - IMMER aus dem persistierten State entfernen.
            // Es wird automatisch wieder hinzugefügt wenn der User einen SimBrief-Sync startet
            // oder der Background-Sync einen neuen Flugplan findet.
            if (RemoveSimbriefOFPFromDocumentList())
            {
                SaveDocumentState();
                KneeboardLogger.SimBrief("OFP removed from restored document state (runtime document)");
            }
            EnsureDefaultManualsExist();

            // Autostart und SimBrief werden jetzt im First-Start Wizard abgefragt



            if (Properties.Settings.Default.simStart == true)
            {
                WriteExeXML();
            }

            if (Properties.Settings.Default.minimized == true)
            {
                if (WindowState == FormWindowState.Normal)
                {
                    WindowState = FormWindowState.Minimized;
                }
                else if (WindowState == FormWindowState.Maximized)
                {
                    WindowState = FormWindowState.Minimized;
                }
                notifyIcon.ShowBalloonTip(1000);
            }

            // Spenden-Dialog 1x pro Tag anzeigen (wenn keine gültige Seriennummer)
            bool serialValid = InformationForm.IsSerialValid(Properties.Settings.Default.serialNumber);
            DateTime lastShown;
            try
            {
                lastShown = Properties.Settings.Default.lastDonationShown;
            }
            catch
            {
                lastShown = DateTime.MinValue;
            }
            if (!serialValid && (lastShown == DateTime.MinValue || lastShown.Date < DateTime.Now.Date))
            {
                var spendenForm = new SpendenForm();
                spendenForm.ShowDialog(this);
                Properties.Settings.Default.lastDonationShown = DateTime.Now;
                Properties.Settings.Default.Save();
            }

            // Panel-Check nach dem Laden: Shown-Event nutzen, damit das Fenster sichtbar ist
            this.Shown += (s, ev2) => CheckAndPromptPanelInstall();

            myServer = new SimpleHTTPServer(folderpath + @"\data", Convert.ToInt32(port), this);
            KneeboardLogger.Server("Server is running on this port: " + myServer.Port.ToString());
            statusBox.BackColor = SystemColors.MenuHighlight;
            serverRun = true;
            UpdateStatusBar();
            UpdateFileList();
        }

        /// <summary>
        /// Prüft ob das MSFS Panel installiert ist. Falls nicht, wird das Settings-Fenster geöffnet.
        /// </summary>
        private void CheckAndPromptPanelInstall()
        {
            try
            {
                if (!PanelDeploymentService.SourceExists())
                    return; // Keine Quelldateien vorhanden, nichts zu tun

                var installations = MsfsPathDetector.DetectMsfsInstallations();
                bool panelInstalled = false;

                foreach (var install in installations)
                {
                    var info = MsfsPathDetector.GetInstalledPackageInfo(install.CommunityPath);
                    if (info.IsInstalled)
                    {
                        panelInstalled = true;
                        break;
                    }
                }

                if (!panelInstalled)
                {
                    KneeboardLogger.Server("MSFS Panel not installed - opening settings...");
                    var settingsForm = new InformationForm();
                    settingsForm.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.Error("PanelCheck", $"Error checking panel status: {ex.Message}");
            }
        }

        private void Close_Click(object sender, EventArgs e)
        {
            if (serverRun == true)
            {
                serverRun = false;
                myServer.Stop();
                statusBox.BackColor = SystemColors.MenuHighlight;
                statusBox.Text = "Status: Server is not running...";
                Properties.Settings.Default.serverRun = true;
                Properties.Settings.Default.Save();
            }
            else
            {
                Properties.Settings.Default.serverRun = false;
                Properties.Settings.Default.Save();
            }
            Environment.Exit(0);
        }

        private void KneeboardServer_MouseDown(object sender, MouseEventArgs e)
        {
            _dragging = true;
            _start_point = new Point(e.X, e.Y);
        }

        private void KneeboardServer_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging)
            {
                Point p = PointToScreen(e.Location);
                Location = new Point(p.X - this._start_point.X, p.Y - this._start_point.Y);
            }
        }

        private void KneeboardServer_MouseUp(object sender, MouseEventArgs e)
        {
            _dragging = false;
        }

        private void Maximize_Click(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Normal)
            {
                WindowState = FormWindowState.Maximized;
            }
            else if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Normal;
            }
        }

        private void Minimize_Click(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Normal)
            {
                WindowState = FormWindowState.Minimized;
            }
            else if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Minimized;
            }
            notifyIcon.ShowBalloonTip(1000);
        }

        public void UpdateFileList()
        {
            treeView1.Nodes.Clear();

            foreach (KneeboardFile file in filesList)
            {
                var fileNode = treeView1.Nodes.Add("📄 " + file.Name);
                fileNode.Tag = "file";
                fileNode.Name = file.Name;
            }

            foreach (KneeboardFolder folder in foldersList)
            {
                System.Windows.Forms.TreeNode folderNode = treeView1.Nodes.Add("📁 " + folder.Name);
                folderNode.Tag = "folder";
                folderNode.Name = folder.Name;

                foreach (KneeboardFile folderFile in folder.Files)
                {
                    var subFileNode = folderNode.Nodes.Add("📄 " + folderFile.Name);
                    subFileNode.Tag = "file";
                    subFileNode.Name = folderFile.Name;
                }
            }
            CreateImages();
            treeView1.ExpandAll();
        }

        // REMOVED: ReplaceGermanUmlauts() - was unused (GetCleanName exists for similar purpose)

        bool imagesCreated = false;

        // Helper-Methode für bereinigten Dateinamen
        private string GetCleanName(string name)
        {
            return Regex.Replace(name, "[^0-9A-Za-z]+", "_");
        }

        // Helper-Methode für Dateiendungs-Prüfung
        private bool IsPdfFile(string path)
        {
            string ext = Path.GetExtension(path);
            return ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path);
            return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".png", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsTextFile(string path)
        {
            string ext = Path.GetExtension(path);
            return ext.Equals(".txt", StringComparison.OrdinalIgnoreCase);
        }

        private void CreateTextImage(string textFilePath, string outputPath)
        {
            try
            {
                Directory.CreateDirectory(outputPath);
                string cleanFilename = GetCleanName(Path.GetFileNameWithoutExtension(textFilePath));
                string text = System.IO.File.ReadAllText(textFilePath);

                // Bildgröße (A4-ähnlich bei 150 DPI)
                int width = 1240;
                int height = 1754;
                int margin = 60;
                int lineHeight = 28;

                using (var bitmap = new Bitmap(width, height))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                    // Schriftart
                    using (var font = new Font("Segoe UI", 14, FontStyle.Regular))
                    using (var brush = new SolidBrush(Color.Black))
                    {
                        // Text umbrechen
                        string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                        int y = margin;
                        int pageNum = 1;
                        int maxLines = (height - 2 * margin) / lineHeight;
                        int lineCount = 0;

                        foreach (string line in lines)
                        {
                            // Zeile ggf. umbrechen
                            string remainingText = line;
                            while (!string.IsNullOrEmpty(remainingText))
                            {
                                // Messe wie viel Text in eine Zeile passt
                                int charsFit = remainingText.Length;
                                SizeF size = graphics.MeasureString(remainingText, font);

                                while (size.Width > width - 2 * margin && charsFit > 1)
                                {
                                    charsFit--;
                                    size = graphics.MeasureString(remainingText.Substring(0, charsFit), font);
                                }

                                string lineToDraw = remainingText.Substring(0, charsFit);
                                remainingText = charsFit < remainingText.Length ? remainingText.Substring(charsFit) : "";

                                graphics.DrawString(lineToDraw, font, brush, margin, y);
                                y += lineHeight;
                                lineCount++;

                                // Neue Seite wenn voll
                                if (lineCount >= maxLines && (!string.IsNullOrEmpty(remainingText) || lines.Length > lineCount))
                                {
                                    // Speichere aktuelle Seite
                                    string pagePath = Path.Combine(outputPath, $"{cleanFilename}_{pageNum}.png");
                                    bitmap.Save(pagePath, System.Drawing.Imaging.ImageFormat.Png);
                                    pageNum++;

                                    // Neue Seite vorbereiten
                                    graphics.Clear(Color.White);
                                    y = margin;
                                    lineCount = 0;
                                }
                            }
                        }

                        // Letzte Seite speichern
                        if (lineCount > 0 || pageNum == 1)
                        {
                            string pagePath = Path.Combine(outputPath, $"{cleanFilename}_{pageNum}.png");
                            bitmap.Save(pagePath, System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                }

                KneeboardLogger.ServerDebug($"Text file converted to images: {textFilePath}");
            }
            catch (Exception ex)
            {
                KneeboardLogger.ServerError($"Error creating text image: {ex.Message}");
            }
        }

        private void CleanupOrphanedImages()
        {
            try
            {
                string imagesPath = $@"{folderpath}\data\images";
                if (!Directory.Exists(imagesPath))
                    return;

                // Sammle alle gültigen Ordnernamen aus filesList und foldersList
                var validFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Einzelne Dateien
                foreach (var file in filesList)
                {
                    validFolders.Add(GetCleanName(Path.GetFileNameWithoutExtension(file.Path)));
                }

                // Ordner und deren Dateien
                foreach (var folder in foldersList)
                {
                    validFolders.Add(GetCleanName(folder.Name));
                }

                // Lösche Ordner die nicht mehr in der Liste sind
                foreach (var dir in Directory.GetDirectories(imagesPath))
                {
                    string dirName = Path.GetFileName(dir);

                    // Prüfe ob es ein Hauptordner (aus foldersList) ist
                    var mainFolder = foldersList.FirstOrDefault(f =>
                        GetCleanName(f.Name).Equals(dirName, StringComparison.OrdinalIgnoreCase));

                    if (mainFolder != null)
                    {
                        // Für Hauptordner: prüfe Unterordner
                        var validSubFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var file in mainFolder.Files)
                        {
                            validSubFolders.Add(GetCleanName(Path.GetFileNameWithoutExtension(file.Path)));
                        }

                        // Lösche verwaiste Unterordner
                        foreach (var subDir in Directory.GetDirectories(dir))
                        {
                            string subDirName = Path.GetFileName(subDir);
                            if (!validSubFolders.Contains(subDirName))
                            {
                                try
                                {
                                    Directory.Delete(subDir, true);
                                    KneeboardLogger.ServerDebug($"Deleted orphaned subfolder: {subDir}");
                                }
                                catch (Exception ex)
                                {
                                    KneeboardLogger.ServerError($"Error deleting subfolder: {ex.Message}");
                                }
                            }
                        }
                    }
                    else if (!validFolders.Contains(dirName))
                    {
                        // Kein Hauptordner und nicht in filesList -> löschen
                        try
                        {
                            Directory.Delete(dir, true);
                            KneeboardLogger.ServerDebug($"Deleted orphaned folder: {dir}");
                        }
                        catch (Exception ex)
                        {
                            KneeboardLogger.ServerError($"Error deleting folder: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.ServerError($"Error during cleanup: {ex.Message}");
            }
        }

        public async void CreateImages()
        {
            // Verhindere parallele Ausführung - prüfe ob bereits eine Instanz läuft
            if (imagesProcessing)
            {
                KneeboardLogger.Warn("Server", "Image creation already in progress - skipping duplicate call");
                return;
            }

            imagesCreated = false;
            string imagesBasePath = $@"{folderpath}\data\images";

            try
            {
                // Lösche verwaiste Bilder-Ordner
                CleanupOrphanedImages();

                // Zähle Gesamtanzahl der zu verarbeitenden Dateien
                int totalFiles = 0;
                int processedFiles = 0;

                // Zähle einzelne Dateien
                foreach (var file in filesList)
                {
                    string cleanName = GetCleanName(Path.GetFileNameWithoutExtension(file.Path));
                    string targetPath = $@"{imagesBasePath}\{cleanName}";
                    if ((IsPdfFile(file.Path) || IsTextFile(file.Path)) && !Directory.Exists(targetPath))
                        totalFiles++;
                }

                // Zähle Dateien in Ordnern
                foreach (var folder in foldersList)
                {
                    if (folder.Name == "data") continue;
                    string cleanFolderName = GetCleanName(folder.Name);
                    string folderImagesPath = $@"{imagesBasePath}\{cleanFolderName}";
                    foreach (var file in folder.Files)
                    {
                        string cleanFileName = GetCleanName(Path.GetFileNameWithoutExtension(file.Path));
                        string targetPath = $@"{folderImagesPath}\{cleanFileName}";
                        if ((IsPdfFile(file.Path) || IsTextFile(file.Path)) && !Directory.Exists(targetPath))
                            totalFiles++;
                    }
                }

                // Zeige Fortschritt in UpdateMessage wenn Dateien zu verarbeiten sind
                if (totalFiles > 0)
                {
                    UpdateMessage.Visible = true;
                    UpdateMessage.BackColor = SystemColors.MenuHighlight;
                }

                // Verarbeite einzelne Dateien
                foreach (var file in filesList)
                {
                    string cleanName = GetCleanName(Path.GetFileNameWithoutExtension(file.Path));
                    string targetPath = $@"{imagesBasePath}\{cleanName}";

                    if (IsPdfFile(file.Path))
                    {
                        if (!Directory.Exists(targetPath))
                        {
                            imagesProcessing = true;
                            processedFiles++;
                            UpdateMessage.Text = $" Creating images ({processedFiles}/{totalFiles}): {Path.GetFileName(file.Path)}";
                            await Task.Run(() => createImage(file.Path, targetPath));
                            imagesCreated = true;
                        }
                    }
                    else if (IsImageFile(file.Path))
                    {
                        Directory.CreateDirectory(targetPath);
                        string destFile = $@"{targetPath}\{cleanName}_1.png";
                        System.IO.File.Copy(file.Path, destFile, true);
                    }
                    else if (IsTextFile(file.Path))
                    {
                        if (!Directory.Exists(targetPath))
                        {
                            imagesProcessing = true;
                            processedFiles++;
                            UpdateMessage.Text = $" Creating images ({processedFiles}/{totalFiles}): {Path.GetFileName(file.Path)}";
                            await Task.Run(() => CreateTextImage(file.Path, targetPath));
                            imagesCreated = true;
                        }
                    }
                }

                // Verarbeite Ordner
                foreach (var folder in foldersList)
                {
                    if (folder.Name == "data") continue;

                    string cleanFolderName = GetCleanName(folder.Name);
                    string folderImagesPath = $@"{imagesBasePath}\{cleanFolderName}";

                    if (!Directory.Exists(folderImagesPath))
                    {
                        Directory.CreateDirectory(folderImagesPath);
                        await Task.Delay(1000);
                    }

                    foreach (var file in folder.Files)
                    {
                        string cleanFileName = GetCleanName(Path.GetFileNameWithoutExtension(file.Path));
                        string targetPath = $@"{folderImagesPath}\{cleanFileName}";

                        if (IsPdfFile(file.Path))
                        {
                            if (!Directory.Exists(targetPath))
                            {
                                imagesProcessing = true;
                                processedFiles++;
                                UpdateMessage.Text = $" Creating images ({processedFiles}/{totalFiles}): {Path.GetFileName(file.Path)}";
                                await Task.Run(() => createImage(file.Path, targetPath));
                                imagesCreated = true;
                                KneeboardLogger.ServerDebug($"Images successfully created: {file.Path}");
                            }
                        }
                        else if (IsImageFile(file.Path))
                        {
                            Directory.CreateDirectory(targetPath);
                            string destFile = $@"{targetPath}\{cleanFileName}_1.png";
                            System.IO.File.Copy(file.Path, destFile, true);
                        }
                        else if (IsTextFile(file.Path))
                        {
                            if (!Directory.Exists(targetPath))
                            {
                                imagesProcessing = true;
                                processedFiles++;
                                UpdateMessage.Text = $" Creating images ({processedFiles}/{totalFiles}): {Path.GetFileName(file.Path)}";
                                await Task.Run(() => CreateTextImage(file.Path, targetPath));
                                imagesCreated = true;
                                KneeboardLogger.ServerDebug($"Text images created: {file.Path}");
                            }
                        }
                    }
                }

                // Verstecke UpdateMessage nach Abschluss (außer wenn Update verfügbar)
                if (!updateAvailable)
                {
                    UpdateMessage.Visible = false;
                }
                if (imagesCreated)
                {
                    string baseStatus = simConnectConnected ? "Status: Images updated. Server is running... | MSFS Connected" : "Status: Images updated. Server is running...";
                    statusBox.Text = baseStatus;
                }
                else
                {
                    UpdateStatusBar();
                }
            }
            catch (Exception e)
            {
                KneeboardLogger.ServerError($"Error in CreateImages: {e.Message}");
            }
            finally
            {
                imagesProcessing = false;
            }
        }


        void createImage(string file, string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                System.Threading.Thread.Sleep(1000);

                string cleanFilename = GetCleanName(Path.GetFileNameWithoutExtension(file));

                using (var pdfDoc = PdfiumViewer.PdfDocument.Load(file))
                {
                    for (int i = 0; i < pdfDoc.PageCount; i++)
                    {
                        using (var img = RenderPage(pdfDoc, i))
                        {
                            string outputPath = Path.Combine(path, $"{cleanFilename}_{i + 1}.png");
                            img.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                }

                KneeboardLogger.ServerDebug($"PDF images created successfully: {file}");
            }
            catch (Exception ex)
            {
                KneeboardLogger.ServerError($"Error creating PDF images: {ex.Message}");
                MessageBox.Show($"Error creating PDF images: {ex.Message}", "PDF Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public System.Drawing.Image RenderPage(PdfiumViewer.PdfDocument doc, int page)
        {
            // Render at 300 DPI for high quality
            const int dpi = 300;
            var pageSize = doc.PageSizes[page];
            int width = (int)(pageSize.Width / 72.0 * dpi);
            int height = (int)(pageSize.Height / 72.0 * dpi);

            // Render with transparent background (PdfRenderFlags.Transparent)
            using (var image = doc.Render(page, width, height, dpi, dpi, PdfiumViewer.PdfRenderFlags.Transparent))
            {
                // Convert to 32bpp ARGB to ensure transparency is preserved
                var result = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(result))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    g.DrawImage(image, 0, 0, width, height);
                }
                return result;
            }
        }



        public static bool simbriefDownloaded = false;

        public static void Restart()
        {
            Application.Restart();
            Environment.Exit(0);
        }


        private void KneeboardServer_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (serverRun == true)
            {
                serverRun = false;
                //myServer.Stop();
                statusBox.Text = "Status: Server is not running! Plese select a working folder.";
                statusBox.BackColor = Color.IndianRed;
            }

            // SimConnect cleanup
            if (simConnectManager != null)
            {
                try
                {
                    simConnectManager.Stop();
                    simConnectManager.Dispose();
                }
                catch { /* Ignore cleanup errors */ }
                simConnectManager = null;
            }

            Application.Exit();
        }

        // Public API for SimConnect access
        public SimConnectManager.AircraftPosition? GetSimConnectPosition()
        {
            return simConnectManager?.GetLatestPosition();
        }

        public bool IsSimConnectConnected()
        {
            return simConnectManager?.IsConnected ?? false;
        }

        public bool IsSimConnectFlightLoaded()
        {
            return simConnectManager?.IsFlightLoaded() ?? false;
        }

        public void SimConnectTeleport(double lat, double lng, double? altitude = null, double? heading = null, double? speed = null)
        {
            simConnectManager?.Teleport(lat, lng, altitude, heading, speed);
        }

        public void SimConnectSetRadioFrequency(string radio, uint frequencyHz)
        {
            simConnectManager?.SetRadioFrequency(radio, frequencyHz);
        }

        private void AddFileButton_Click(object sender, EventArgs e)
        {
            bool dataChanged = false;
            try
            {
                if (treeView1.SelectedNode != null)
                {
                    //MessageBox.Show("selected");
                    System.Windows.Forms.OpenFileDialog openDialog = new System.Windows.Forms.OpenFileDialog
                    {
                        Title = "Select A File",
                        Filter = "Documents (*.pdf;*.jpg;*.png;*.txt)|*.pdf;*.jpg;*.png;*.txt|PDF Files (*.pdf)|*.pdf|Images (*.jpg;*.png)|*.jpg;*.png|Text Files (*.txt)|*.txt"
                    };
                    if (openDialog.ShowDialog() == DialogResult.OK)
                    {
                        string fileName = openDialog.FileName;

                        bool containsItem = filesList.Any(item => item.Path == Path.GetFullPath(openDialog.FileName));

                        string fileType = Path.GetExtension(openDialog.FileName);
                        if (!containsItem)
                        {

                            foreach (KneeboardFolder folder in foldersList.Where(x => x.Name == treeView1.SelectedNode.Name))
                            {
                                //MessageBox.Show("found");s

                                KneeboardFile file = new KneeboardFile(name: Path.GetFileNameWithoutExtension(openDialog.FileName), pages: 0, path: fileName);
                                folder.Files.Add(file);
                                dataChanged = true;
                            }
                        }
                        else
                        {
                            MessageBox.Show("A file with the same name is already in the directory!");
                        }
                    }

                }
                else
                {
                    System.Windows.Forms.OpenFileDialog openDialog = new System.Windows.Forms.OpenFileDialog
                    {
                        Title = "Select A File",
                        Filter = "Documents (*.pdf;*.jpg;*.png;*.txt)|*.pdf;*.jpg;*.png;*.txt|PDF Files (*.pdf)|*.pdf|Images (*.jpg;*.png)|*.jpg;*.png|Text Files (*.txt)|*.txt"
                    };
                    if (openDialog.ShowDialog() == DialogResult.OK)
                    {
                        string fileName = openDialog.FileName;

                        bool containsItem = filesList.Any(item => item.Path == Path.GetFullPath(openDialog.FileName));

                        string fileType = Path.GetExtension(openDialog.FileName);
                        if (!containsItem)
                        {
                            KneeboardFile file = new KneeboardFile(name: Path.GetFileNameWithoutExtension(openDialog.FileName), pages: 0, path: fileName);
                            filesList.Add(file);
                            dataChanged = true;
                        }
                        else
                        {
                            MessageBox.Show("A file with the same name is already in the directory!");
                        }
                    }
                }
                UpdateFileList();
                if (dataChanged)
                {
                    SaveDocumentState();
                }
            }
            catch (Exception e2)
            {
                KneeboardLogger.ServerError(e2.Message);
            }

        }

        private void DeleteFileButton_Click(object sender, EventArgs e)
        {
            bool dataChanged = false;
            if (treeView1.SelectedNode != null)
            {
                if (treeView1.SelectedNode.Parent == null)
                {
                    if (filesList != null)
                    {
                        int removed = filesList.RemoveAll(x => x.Name == treeView1.SelectedNode.Name);
                        if (removed > 0)
                            dataChanged = true;
                    }
                } //parent
                else
                {

                    var item = foldersList?.SingleOrDefault(x => x.Name == treeView1.SelectedNode.Parent.Name);
                    if (item != null)
                    {
                        int removed = item.Files.RemoveAll(x => x.Name == treeView1.SelectedNode.Name);
                        if (removed > 0)
                            dataChanged = true;

                    }
                } //child
            }
            if (dataChanged)
            {
                UpdateFileList();
                SaveDocumentState();
            }
        }

        public string GetFileList()
        {
            string documentsString = "";

            // Debug-Ausgaben
            if ( filesList == null)
                throw new NullReferenceException("filesList ist null");
            if (foldersList == null)
                throw new NullReferenceException("foldersList ist null");

            foreach (KneeboardFile file in filesList)
            {
                if (file == null)
                    throw new NullReferenceException("Ein Element in filesList ist null");
                if (file.Name == null)
                    throw new NullReferenceException("Ein KneeboardFile.Name ist null");

                documentsString += $"\"{file.Name}\"";
            }

            foreach (KneeboardFolder folder in foldersList)
            {
                if (folder == null)
                    throw new NullReferenceException("Ein Element in foldersList ist null");
                if (folder.Files == null)
                    throw new NullReferenceException($"folder.Files ist null im Ordner {folder.Name}");
                if (folder.Name == null)
                    throw new NullReferenceException("folder.Name ist null");

                foreach (KneeboardFile file2 in folder.Files)
                {
                    if (file2 == null)
                        throw new NullReferenceException($"Ein Dateiobjekt in Ordner {folder.Name} ist null");
                    if (file2.Name == null)
                        throw new NullReferenceException($"Eine Datei in Ordner {folder.Name} hat keinen Namen");

                    documentsString += $"\"{folder.Name}\\{file2.Name}\"";
                }
            }

            return documentsString;
        }

        private void Information_Click(object sender, EventArgs e)
        {
            InformationForm InformationForm = new InformationForm();
            InformationForm.ShowDialog();
        }

        private void Kneeboard_Server_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            UpdateFileList();
            KneeboardLogger.ServerDebug("Update file list");
        }

        private void EditButton_Click(object sender, EventArgs e)
        {
            if (folderpath != "")
            {
                if (serverRun == true)
                {
                    System.Diagnostics.Process.Start("microsoft-edge:http://localhost:815/Kneeboard.html");
                }
            }
            else
            {
                MessageBox.Show("Please select a document directory!", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            if (imagesProcessing == false)
            {
                System.Windows.Forms.SaveFileDialog savefile = new System.Windows.Forms.SaveFileDialog
                {
                    // set a default file name
                    FileName = DateTime.Now.ToString("yyyy-dd-M--HH-mm-ss")
                };
                // set filters - this can be done in properties as well
                savefile.Filter = "Kneeboard navlog files (*.knf)|*.knf|All files (*.*)|*.*";
                savefile.DefaultExt = "knf";
                savefile.AddExtension = true;
                if (savefile.ShowDialog() == DialogResult.OK)
                {
                    using (StreamWriter sw2 = new StreamWriter(savefile.FileName))
                    {
                        sw2.WriteLine(myServer.values);
                        sw2.Close();
                    }
                }
            }
        }

        private void LoadButton_Click(object sender, EventArgs e)
        {
            if (imagesProcessing == false)
            {
                if (Properties.Settings.Default.simbriefId != "")
                {
                    DialogResult dialogResult = MessageBox.Show("Do you like to import latest Simbrief OFP?", "Simbrief import", MessageBoxButtons.YesNo);
                    if (dialogResult == DialogResult.Yes)
                    {
                        try
                        {
                            if (Directory.Exists(folderpath + @"\data\images\Simbrief"))
                            {
                                try
                                {
                                    Directory.Delete(folderpath + @"\data\images\Simbrief", true);
                                }
                                catch
                                {
                                }
                            }
                            var m_strFilePath = GetSimbriefApiUrl();
                            string xmlStr;
                            using (var wc = new WebClient())
                            {
                                xmlStr = wc.DownloadString(m_strFilePath);
                            }
                            var xmlDoc = new XmlDocument();
                            xmlDoc.LoadXml(xmlStr);

                            // OFP-Daten deserialisieren und speichern
                            using (StringReader stringReader = new StringReader(xmlStr))
                            {
                                XmlSerializer ofpSerializer = new XmlSerializer(typeof(Simbrief.OFP));
                                Simbrief.OFP ofpData = (Simbrief.OFP)ofpSerializer.Deserialize(stringReader);
                                simbriefOFPData = Newtonsoft.Json.JsonConvert.SerializeObject(ofpData);
                                KneeboardLogger.SimBriefDebug("OFP deserialized successfully, JSON length: " + (simbriefOFPData != null ? simbriefOFPData.Length.ToString() : "null"));
                            }

                            var plnNode = xmlDoc.DocumentElement.SelectSingleNode("/OFP/fms_downloads/mfs/link");
                            var ofpNode = xmlDoc.DocumentElement.SelectSingleNode("/OFP/files/pdf/link");

                            if (plnNode == null || ofpNode == null)
                            {
                                MessageBox.Show("Could not retrieve flight plan from SimBrief. Please check your Pilot ID and ensure you have a flight plan generated on SimBrief.", "SimBrief Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            }

                            string simbriefPLN = plnNode.InnerText;
                            string simbriefOFP = ofpNode.InnerText;

                            if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                            {
                                using (System.Net.WebClient client = new System.Net.WebClient())
                                {
                                    client.DownloadFile(new Uri("https://www.simbrief.com/ofp/flightplans/" + simbriefPLN), folderpath + @"\data\simbrief.pln");
                                    Properties.Settings.Default.communityFolderPath = "";
                                    Properties.Settings.Default.Save();
                                }
                            }
                            using (XmlReader reader = XmlReader.Create(new FileStream(folderpath + @"\data\simbrief.pln", FileMode.Open), new XmlReaderSettings() { CloseInput = true }))
                            {
                                XmlSerializer serializer = new XmlSerializer(typeof(SimBaseDocument));
                                SimBaseDocument waypoints = (SimBaseDocument)serializer.Deserialize(reader);
                                // Kombiniertes Objekt mit PLN und OFP-Daten senden
                                KneeboardLogger.SimBriefDebug("Creating combined data - simbriefOFPData is " + (simbriefOFPData != null ? "NOT null, length: " + simbriefOFPData.Length : "NULL"));
                                var combinedData = new
                                {
                                    pln = waypoints,
                                    ofp = simbriefOFPData != null ? Newtonsoft.Json.JsonConvert.DeserializeObject(simbriefOFPData) : null
                                };
                                flightplan = Newtonsoft.Json.JsonConvert.SerializeObject(combinedData);
                                KneeboardLogger.SimBriefDebug("Combined flightplan created, length: " + (flightplan != null ? flightplan.Length.ToString() : "null"));
                                reader.Close();
                            }

                            if (!Directory.Exists(simbriefDataDir))
                            {
                                Directory.CreateDirectory(simbriefDataDir);
                            }

                            if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                            {
                                using (System.Net.WebClient client = new System.Net.WebClient())
                                {
                                    client.DownloadFile(new Uri("https://www.simbrief.com/ofp/flightplans/" + simbriefOFP), Path.Combine(simbriefDataDir, "OFP.pdf"));
                                    simbriefDownloaded = true;
                                }
                            }

                            // OFP PDF zur Dokumentenliste hinzufügen
                            if (AddSimbriefOFPToDocumentList())
                            {
                                UpdateFileList();      // UI aktualisieren
                                SaveDocumentState();   // Zustand persistieren
                                KneeboardLogger.SimBrief("OFP PDF added to document list");
                            }

                            lastFlightplanSource = FlightplanSource.SimBrief;
                            MessageBox.Show("SimBrief flight plan imported successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        catch (System.Net.WebException webEx)
                        {
                            KneeboardLogger.SimBriefError($"Download error: {webEx.Message}");
                            MessageBox.Show($"Could not connect to SimBrief. Please check your internet connection.\n\nError: {webEx.Message}", "Network Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        catch (XmlException xmlEx)
                        {
                            KneeboardLogger.SimBriefError($"XML parsing error: {xmlEx.Message}");
                            MessageBox.Show($"Invalid response from SimBrief. Please ensure you have a valid flight plan generated.\n\nError: {xmlEx.Message}", "SimBrief Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        catch (Exception ex)
                        {
                            KneeboardLogger.SimBriefError($"Import error: {ex.Message}");
                            MessageBox.Show($"Failed to import SimBrief flight plan.\n\nError: {ex.Message}", "Import Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    else if (dialogResult == DialogResult.No)
                    {
                        System.Windows.Forms.OpenFileDialog openFileDialog1 = new System.Windows.Forms.OpenFileDialog
                        {
                            Filter = "Kneeboard files (*.knf;*.pln;*.flightplan)|*.knf;*.pln;*.flightplan|All files (*.*)|*.*"
                        };
                        if (openFileDialog1.ShowDialog() == DialogResult.OK)
                        {
                            KneeboardLogger.Debug("Documents", Path.GetExtension(openFileDialog1.FileName));
                            if (Path.GetExtension(openFileDialog1.FileName) == ".knf" || Path.GetExtension(openFileDialog1.FileName) == ".KNF")
                            {
                                // Create a StreamReader from a FileStream
                                using (StreamReader reader = new StreamReader(new FileStream(openFileDialog1.FileName, FileMode.Open)))
                                {
                                    string line;
                                    // Read line by line
                                    while ((line = reader.ReadLine()) != null)
                                    {
                                        myServer.values = line;
                                        KneeboardLogger.Debug("Documents", line);
                                        Properties.Settings.Default.values = line;
                                        Properties.Settings.Default.Save();
                                    }
                                    reader.Close();
                                }
                            }
                            else if (Path.GetExtension(openFileDialog1.FileName) == ".pln" || Path.GetExtension(openFileDialog1.FileName) == ".PLN")
                            {
                                try
                                {
                                    communityFolderPath = openFileDialog1.FileName;
                                    Properties.Settings.Default.communityFolderPath = communityFolderPath;
                                    Properties.Settings.Default.Save();
                                    using (XmlReader reader = XmlReader.Create(new FileStream(communityFolderPath, FileMode.Open), new XmlReaderSettings() { CloseInput = true }))
                                    {
                                        XmlSerializer serializer = new XmlSerializer(typeof(SimBaseDocument));
                                        SimBaseDocument waypoints = (SimBaseDocument)serializer.Deserialize(reader);
                                        var combinedData = new { pln = waypoints, ofp = (object)null };
                                        flightplan = Newtonsoft.Json.JsonConvert.SerializeObject(combinedData);
                                        lastFlightplanSource = FlightplanSource.LocalPLN;
                                        reader.Close();
                                    }
                                }
                                catch (Exception e2)
                                {
                                    KneeboardLogger.ServerError(e2.Message);
                                }
                            }
                            else if (Path.GetExtension(openFileDialog1.FileName) == ".flightplan" || Path.GetExtension(openFileDialog1.FileName) == ".FLIGHTPLAN")
                            {
                                try
                                {
                                    communityFolderPath = openFileDialog1.FileName;
                                    Properties.Settings.Default.communityFolderPath = communityFolderPath;
                                    Properties.Settings.Default.Save();
                                    SimBaseDocument waypoints = ParseSkyDemonFlightplan(openFileDialog1.FileName);
                                    var combinedData = new { pln = waypoints, ofp = (object)null };
                                    flightplan = Newtonsoft.Json.JsonConvert.SerializeObject(combinedData);
                                    lastFlightplanSource = FlightplanSource.LocalPLN;
                                }
                                catch (Exception e2)
                                {
                                    KneeboardLogger.ServerError(e2.Message);
                                    MessageBox.Show("Error loading SkyDemon flightplan: " + e2.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                            }
                        }
                    }
                }
                else
                {
                    System.Windows.Forms.OpenFileDialog openFileDialog1 = new System.Windows.Forms.OpenFileDialog
                    {
                        Filter = "Kneeboard files (*.knf;*.pln;*.flightplan)|*.knf;*.pln;*.flightplan|All files (*.*)|*.*"
                    };
                    if (openFileDialog1.ShowDialog() == DialogResult.OK)
                    {
                        KneeboardLogger.Debug("Documents", Path.GetExtension(openFileDialog1.FileName));
                        if (Path.GetExtension(openFileDialog1.FileName) == ".knf" || Path.GetExtension(openFileDialog1.FileName) == ".KNF")
                        {
                            // Create a StreamReader from a FileStream
                            using (StreamReader reader = new StreamReader(new FileStream(openFileDialog1.FileName, FileMode.Open)))
                            {
                                string line;
                                // Read line by line
                                while ((line = reader.ReadLine()) != null)
                                {
                                    myServer.values = line;
                                    KneeboardLogger.Debug("Documents", line);
                                    Properties.Settings.Default.values = line;
                                    Properties.Settings.Default.Save();
                                }
                                reader.Close();
                            }
                        }
                        else if (Path.GetExtension(openFileDialog1.FileName) == ".pln" || Path.GetExtension(openFileDialog1.FileName) == ".PLN")
                        {
                            try
                            {
                                communityFolderPath = openFileDialog1.FileName;
                                Properties.Settings.Default.communityFolderPath = communityFolderPath;
                                Properties.Settings.Default.Save();
                                using (XmlReader reader = XmlReader.Create(new FileStream(communityFolderPath, FileMode.Open), new XmlReaderSettings() { CloseInput = true }))
                                {
                                    XmlSerializer serializer = new XmlSerializer(typeof(SimBaseDocument));
                                    SimBaseDocument waypoints = (SimBaseDocument)serializer.Deserialize(reader);
                                    var combinedData = new { pln = waypoints, ofp = (object)null };
                                    flightplan = Newtonsoft.Json.JsonConvert.SerializeObject(combinedData);
                                    lastFlightplanSource = FlightplanSource.LocalPLN;
                                    reader.Close();
                                }
                            }
                            catch (Exception e2)
                            {
                                KneeboardLogger.ServerError(e2.Message);
                            }
                        }
                        else if (Path.GetExtension(openFileDialog1.FileName) == ".flightplan" || Path.GetExtension(openFileDialog1.FileName) == ".FLIGHTPLAN")
                        {
                            try
                            {
                                communityFolderPath = openFileDialog1.FileName;
                                Properties.Settings.Default.communityFolderPath = communityFolderPath;
                                Properties.Settings.Default.Save();
                                SimBaseDocument waypoints = ParseSkyDemonFlightplan(openFileDialog1.FileName);
                                var combinedData = new { pln = waypoints, ofp = (object)null };
                                flightplan = Newtonsoft.Json.JsonConvert.SerializeObject(combinedData);
                                lastFlightplanSource = FlightplanSource.LocalPLN;
                            }
                            catch (Exception e2)
                            {
                                KneeboardLogger.ServerError(e2.Message);
                                MessageBox.Show("Error loading SkyDemon flightplan: " + e2.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                    }
                }
            }
        }



        private void AddFolderButton_MouseHover(object sender, EventArgs e)
        {
            MyToolTip.Show("Add a folder", addFolderButton);
            addFolderButton.BackColor = Color.White;
        }

        private void DeleteFolderButton_MouseHover(object sender, EventArgs e)
        {
            MyToolTip.Show("Delete a folder", deleteFolderButton);
            deleteFolderButton.BackColor = Color.White;
        }

        private void AddFileButton_MouseHover(object sender, EventArgs e)
        {
            MyToolTip.Show("Upload a document", addFileButton);
            addFileButton.BackColor = Color.White;
        }

        private void DeleteFileButton_MouseHover(object sender, EventArgs e)
        {
            MyToolTip.Show("Delete a document", deleteFileButton);
            deleteFileButton.BackColor = Color.White;
        }

        private void LoadButton_MouseHover(object sender, EventArgs e)
        {
            MyToolTip.Show("Open a navlog/flightplan file", loadButton);
            loadButton.BackColor = Color.White;
        }

        private void SaveButton_MouseHover(object sender, EventArgs e)
        {
            MyToolTip.Show("Save a navlog file", saveButton);
            saveButton.BackColor = Color.White;
        }

        private void EditButton_MouseHover(object sender, EventArgs e)
        {
            MyToolTip.Show("Show browser kneeboard", editButton);
            editButton.BackColor = Color.White;
        }

        private void Information_MouseHover(object sender, EventArgs e)
        {
            MyToolTip.Show("Settings", information);
        }

        private void MinimizeButton_MouseHover(object sender, EventArgs e)
        {
            MyToolTip.Show("Minimize", minimizeButton);
        }

        private void MaximizeButton_MouseHover(object sender, EventArgs e)
        {
            MyToolTip.Show("Maximize", maximizeButton);
        }

        private void CloseButton_MouseHover(object sender, EventArgs e)
        {
            MyToolTip.Show("Close", closeButton);
        }

        private void label1_MouseHover(object sender, EventArgs e)
        {
            MyToolTip.Show("Recreate images", label1);
        }

        private SimBaseDocument ParseSkyDemonFlightplan(string filePath)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(filePath);

            SimBaseDocument result = new SimBaseDocument
            {
                Type = "SkyDemon",
                version = "1.0",
                Descr = "SkyDemon Flightplan Import"
            };

            var flightPlan = new SimBaseDocumentFlightPlanFlightPlan
            {
                FPType = "VFR",
                RouteType = "Direct"
            };

            // Get the root element - SkyDemon uses different root element names
            XmlElement root = doc.DocumentElement;

            // Try to find the Route element
            XmlNode routeNode = root.SelectSingleNode("//PrimaryRoute") ?? root.SelectSingleNode("//Route");

            if (routeNode == null)
            {
                throw new Exception("No Route element found in SkyDemon flightplan");
            }

            // Get departure and destination from Route attributes
            string startCoord = routeNode.Attributes?["Start"]?.Value ?? "";
            string departure = "";
            string destination = "";

            // Parse waypoints - SkyDemon uses RhumbLineRoute elements
            XmlNodeList waypointNodes = routeNode.SelectNodes("RhumbLineRoute");
            if (waypointNodes == null || waypointNodes.Count == 0)
            {
                waypointNodes = routeNode.SelectNodes("RhumLine") ?? routeNode.SelectNodes("Waypoint");
            }

            var waypointList = new List<SimBaseDocumentFlightPlanFlightPlanATCWaypoint>();

            // Add start point as first waypoint
            if (!string.IsNullOrEmpty(startCoord))
            {
                var startCoords = ParseSkyDemonCoordinatePair(startCoord);
                if (startCoords.lat != 0 || startCoords.lon != 0)
                {
                    departure = "START";
                    var startWaypoint = new SimBaseDocumentFlightPlanFlightPlanATCWaypoint
                    {
                        id = "START",
                        ATCWaypointType = "Airport",
                        WorldPosition = FormatWorldPosition(startCoords.lat, startCoords.lon, 0),
                        ICAO = new SimBaseDocumentFlightPlanFlightPlanATCWaypointICAO
                        {
                            ICAOIdent = "START"
                        }
                    };
                    waypointList.Add(startWaypoint);
                    flightPlan.DepartureLLA = FormatWorldPosition(startCoords.lat, startCoords.lon, 0);
                }
            }

            int wpIndex = 1;
            if (waypointNodes != null)
            {
                foreach (XmlNode wpNode in waypointNodes)
                {
                    // Get the "To" attribute which contains coordinates like "N542254.35 E0131427.70"
                    string toCoord = wpNode.Attributes?["To"]?.Value ?? "";

                    if (string.IsNullOrEmpty(toCoord))
                        continue;

                    // Parse coordinate pair
                    var coords = ParseSkyDemonCoordinatePair(toCoord);
                    double lat = coords.lat;
                    double lon = coords.lon;

                    if (lat == 0 && lon == 0)
                        continue;

                    // Generate waypoint name
                    string wpName = "WP" + wpIndex;
                    string toType = wpNode.Attributes?["ToType"]?.Value ?? "Unknown";
                    if (toType == "Aerodrome")
                    {
                        wpName = "DEST";
                    }
                    else if (toType == "Town")
                    {
                        wpName = "TOWN" + wpIndex;
                    }

                    // Create waypoint in MSFS format
                    var waypoint = new SimBaseDocumentFlightPlanFlightPlanATCWaypoint
                    {
                        id = wpName,
                        ATCWaypointType = toType == "Aerodrome" ? "Airport" : "User",
                        WorldPosition = FormatWorldPosition(lat, lon, 0),
                        ICAO = new SimBaseDocumentFlightPlanFlightPlanATCWaypointICAO
                        {
                            ICAOIdent = wpName
                        }
                    };

                    waypointList.Add(waypoint);

                    // Update destination with each waypoint (last one will be final)
                    destination = wpName;
                    flightPlan.DestinationLLA = FormatWorldPosition(lat, lon, 0);

                    wpIndex++;
                }
            }

            flightPlan.DepartureID = departure;
            flightPlan.DepartureName = departure;
            flightPlan.DestinationID = destination;
            flightPlan.DestinationName = destination;
            flightPlan.Title = $"{departure} to {destination}";
            flightPlan.ATCWaypoint = waypointList.ToArray();

            // Try to get cruising altitude
            string altStr = routeNode.Attributes?["Level"]?.Value ??
                           root.SelectSingleNode("//CruiseAltitude")?.InnerText ?? "3000";
            if (decimal.TryParse(altStr, out decimal cruisingAlt))
            {
                flightPlan.CruisingAlt = cruisingAlt;
            }

            result.FlightPlanFlightPlan = flightPlan;
            return result;
        }

        // Parse a coordinate pair like "N542302.10 E0131932.65"
        private (double lat, double lon) ParseSkyDemonCoordinatePair(string coordPair)
        {
            if (string.IsNullOrEmpty(coordPair))
                return (0, 0);

            // Split by space
            var parts = coordPair.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return (0, 0);

            double lat = ParseSkyDemonCoordinate(parts[0]);
            double lon = ParseSkyDemonCoordinate(parts[1]);

            return (lat, lon);
        }

        // Parse a single coordinate like "N542302.10" (format: Nddmmss.ss or Edddmmss.ss)
        private double ParseSkyDemonCoordinate(string coord)
        {
            if (string.IsNullOrEmpty(coord))
                return 0;

            coord = coord.Trim().ToUpper();
            double sign = 1;
            bool isLongitude = false;

            // Determine sign and type based on direction
            if (coord.StartsWith("S") || coord.StartsWith("W"))
            {
                sign = -1;
            }
            if (coord.StartsWith("E") || coord.StartsWith("W"))
            {
                isLongitude = true;
            }

            // Remove direction letter
            coord = coord.Substring(1);

            // Format is ddmmss.ss for latitude or dddmmss.ss for longitude
            // N542302.10 = N 54° 23' 02.10" (latitude: 2 digit degrees)
            // E0131932.65 = E 013° 19' 32.65" (longitude: 3 digit degrees)

            try
            {
                // Latitude has 2 digit degrees, Longitude has 3 digit degrees
                int degDigits = isLongitude ? 3 : 2;

                string degStr = coord.Substring(0, degDigits);
                string minStr = coord.Substring(degDigits, 2);
                string secStr = coord.Substring(degDigits + 2);

                double degrees = double.Parse(degStr, System.Globalization.CultureInfo.InvariantCulture);
                double minutes = double.Parse(minStr, System.Globalization.CultureInfo.InvariantCulture);
                double seconds = double.Parse(secStr, System.Globalization.CultureInfo.InvariantCulture);

                return sign * (degrees + minutes / 60.0 + seconds / 3600.0);
            }
            catch
            {
                return 0;
            }
        }

        private string FormatWorldPosition(double lat, double lon, double alt)
        {
            // Format as MSFS WorldPosition: "N47° 30' 15.00",E9° 45' 30.00",+000500.00"
            string latDir = lat >= 0 ? "N" : "S";
            string lonDir = lon >= 0 ? "E" : "W";

            lat = Math.Abs(lat);
            lon = Math.Abs(lon);

            int latDeg = (int)lat;
            int latMin = (int)((lat - latDeg) * 60);
            double latSec = ((lat - latDeg) * 60 - latMin) * 60;

            int lonDeg = (int)lon;
            int lonMin = (int)((lon - lonDeg) * 60);
            double lonSec = ((lon - lonDeg) * 60 - lonMin) * 60;

            // Use InvariantCulture to ensure dots as decimal separators
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}{1}° {2}' {3:F2}\",{4}{5}° {6}' {7:F2}\",+{8:000000.00}",
                latDir, latDeg, latMin, latSec, lonDir, lonDeg, lonMin, lonSec, alt);
        }

        /// <summary>
        /// Gets the time_generated timestamp from SimBrief API without downloading the full flightplan
        /// </summary>
        private static string GetSimbriefTimeGenerated()
        {
            var url = GetSimbriefApiUrl();
            using (var wc = new WebClient())
            {
                string xmlStr = wc.DownloadString(url);
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xmlStr);
                var timeNode = xmlDoc.SelectSingleNode("/OFP/params/time_generated");
                return (timeNode?.InnerText ?? "").Trim();
            }
        }

        /// <summary>
        /// Checks if a newer flightplan is available on SimBrief compared to the cached one
        /// </summary>
        public static bool CheckSimbriefUpdateAvailable()
        {
            if (string.IsNullOrEmpty(Properties.Settings.Default.simbriefId)) return false;
            // Only check for updates if we have both a flightplan AND a cached timestamp
            // This prevents false positives when browser has no flightplan but server has persisted data
            if (flightplan == null || cachedSimbriefTimeGenerated == null) return false;
            try
            {
                string currentTime = GetSimbriefTimeGenerated();
                bool updateAvailable = currentTime != cachedSimbriefTimeGenerated;
                KneeboardLogger.SimBriefDebug($"Update check: cached={cachedSimbriefTimeGenerated}, current={currentTime}, updateAvailable={updateAvailable}");
                return updateAvailable;
            }
            catch (Exception ex)
            {
                KneeboardLogger.SimBriefError($"Error checking for updates: {ex.Message}");
                return false;
            }
        }

        public static string syncFlightplan()
        {
            // Decision based on last import source
            if (lastFlightplanSource == FlightplanSource.LocalPLN &&
                Properties.Settings.Default.communityFolderPath != "" &&
                System.IO.File.Exists(Properties.Settings.Default.communityFolderPath))
            {
                // Clear cache for local PLN reload
                flightplan = null;

                // Load local PLN file with consistent format
                using (XmlReader reader = XmlReader.Create(new FileStream(Properties.Settings.Default.communityFolderPath, FileMode.Open), new XmlReaderSettings() { CloseInput = true }))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(SimBaseDocument));
                    SimBaseDocument waypoints = (SimBaseDocument)serializer.Deserialize(reader);
                    var combinedData = new { pln = waypoints, ofp = (object)null };
                    flightplan = Newtonsoft.Json.JsonConvert.SerializeObject(combinedData);
                    reader.Close();
                }
                // Enrich with SID/STAR waypoints if available
                flightplan = EnrichFlightplanWithProceduresAsync(flightplan).GetAwaiter().GetResult();
                KneeboardLogger.Debug("SimBrief", flightplan);
            }
            else if (lastFlightplanSource == FlightplanSource.SimBrief ||
                     (lastFlightplanSource == FlightplanSource.None && Properties.Settings.Default.simbriefId != ""))
            {
                try
                {
                    var m_strFilePath = GetSimbriefApiUrl();
                    string xmlStr;
                    using (var wc = new WebClient())
                    {
                        xmlStr = wc.DownloadString(m_strFilePath);
                    }
                    var xmlDoc = new XmlDocument();
                    xmlDoc.LoadXml(xmlStr);

                    // Get time_generated for cache comparison
                    var timeNode = xmlDoc.SelectSingleNode("/OFP/params/time_generated");
                    string currentTimeGenerated = (timeNode?.InnerText ?? "").Trim();

                    // If we have a cached flightplan and timestamp matches, return cached version
                    if (flightplan != null && cachedSimbriefTimeGenerated != null &&
                        currentTimeGenerated == cachedSimbriefTimeGenerated)
                    {
                        KneeboardLogger.SimBriefDebug($"Using cached flightplan (timestamp {cachedSimbriefTimeGenerated} unchanged)");
                        // SID/STAR Waypoints auch bei cached flightplan anreichern
                        flightplan = EnrichFlightplanWithProceduresAsync(flightplan).GetAwaiter().GetResult();

                        // OFP PDF herunterladen falls nicht vorhanden, und zur Document-Liste hinzufügen
                        if (instance != null)
                        {
                            try
                            {
                                // OFP PDF herunterladen falls nicht vorhanden
                                if (!System.IO.File.Exists(Path.Combine(simbriefDataDir, "OFP.pdf")))
                                {
                                    var cachedOfpNode = xmlDoc.DocumentElement.SelectSingleNode("/OFP/files/pdf/link");
                                    if (cachedOfpNode != null && System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                                    {
                                        if (!Directory.Exists(simbriefDataDir))
                                            Directory.CreateDirectory(simbriefDataDir);

                                        using (System.Net.WebClient client = new System.Net.WebClient())
                                        {
                                            client.DownloadFile(new Uri("https://www.simbrief.com/ofp/flightplans/" + cachedOfpNode.InnerText), Path.Combine(simbriefDataDir, "OFP.pdf"));
                                        }
                                        KneeboardLogger.SimBrief("syncFlightplan: OFP PDF downloaded (cached path)");
                                    }
                                }

                                // Zur Document-Liste hinzufügen
                                if (System.IO.File.Exists(Path.Combine(simbriefDataDir, "OFP.pdf")))
                                {
                                    if (instance.InvokeRequired)
                                    {
                                        instance.BeginInvoke(new Action(() =>
                                        {
                                            if (instance.AddSimbriefOFPToDocumentList())
                                            {
                                                instance.UpdateFileList();
                                                instance.SaveDocumentState();
                                                KneeboardLogger.SimBrief("syncFlightplan: OFP PDF added to document list (cached path)");
                                            }
                                        }));
                                    }
                                    else
                                    {
                                        if (instance.AddSimbriefOFPToDocumentList())
                                        {
                                            instance.UpdateFileList();
                                            instance.SaveDocumentState();
                                            KneeboardLogger.SimBrief("syncFlightplan: OFP PDF added to document list (cached path)");
                                        }
                                    }
                                }
                            }
                            catch (Exception ofpEx)
                            {
                                KneeboardLogger.SimBriefError($"syncFlightplan: OFP PDF error (cached path): {ofpEx.Message}");
                            }
                        }

                        return flightplan;
                    }

                    KneeboardLogger.SimBrief($"Loading new flightplan (cached: {cachedSimbriefTimeGenerated}, current: {currentTimeGenerated})");

                    // OFP-Daten deserialisieren und speichern
                    using (StringReader stringReader = new StringReader(xmlStr))
                    {
                        XmlSerializer ofpSerializer = new XmlSerializer(typeof(Simbrief.OFP));
                        Simbrief.OFP ofpData = (Simbrief.OFP)ofpSerializer.Deserialize(stringReader);
                        simbriefOFPData = Newtonsoft.Json.JsonConvert.SerializeObject(ofpData);
                        KneeboardLogger.SimBriefDebug("syncFlightplan: OFP deserialized, JSON length: " + (simbriefOFPData != null ? simbriefOFPData.Length.ToString() : "null"));
                    }

                    var plnNode = xmlDoc.DocumentElement.SelectSingleNode("/OFP/fms_downloads/mfs/link");
                    var ofpNode = xmlDoc.DocumentElement.SelectSingleNode("/OFP/files/pdf/link");

                    if (plnNode == null || ofpNode == null)
                    {
                        KneeboardLogger.Warn("SimBrief", "Could not retrieve flight plan data from XML response");
                        return flightplan;
                    }

                    string value = plnNode.InnerText;
                    string simbriefOFPLink = ofpNode.InnerText;

                    if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                    {
                        using (System.Net.WebClient client = new System.Net.WebClient())
                        {
                            client.DownloadFile(new Uri("https://www.simbrief.com/ofp/flightplans/" + value), folderpath + @"\data\simbrief.pln");
                            communityFolderPath = "";
                        }
                    }
                    using (XmlReader reader = XmlReader.Create(new FileStream(folderpath + @"\data\simbrief.pln", FileMode.Open), new XmlReaderSettings() { CloseInput = true }))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(SimBaseDocument));
                        SimBaseDocument waypoints = (SimBaseDocument)serializer.Deserialize(reader);

                        // Kombiniertes Objekt mit PLN und OFP-Daten senden
                        KneeboardLogger.SimBriefDebug("syncFlightplan: Creating combined data - simbriefOFPData is " + (simbriefOFPData != null ? "NOT null, length: " + simbriefOFPData.Length : "NULL"));

                        object combinedData = new
                        {
                            pln = waypoints,
                            ofp = simbriefOFPData != null ? Newtonsoft.Json.JsonConvert.DeserializeObject(simbriefOFPData) : null
                        };

                        flightplan = Newtonsoft.Json.JsonConvert.SerializeObject(combinedData);
                        lastFlightplanSource = FlightplanSource.SimBrief;

                        // Enrich with SID/STAR waypoints if available
                        flightplan = EnrichFlightplanWithProceduresAsync(flightplan).GetAwaiter().GetResult();

                        // Update cached timestamp after successful load
                        cachedSimbriefTimeGenerated = currentTimeGenerated;
                        KneeboardLogger.SimBriefDebug($"Cached timestamp updated to: {cachedSimbriefTimeGenerated}");

                        // Persist flightplan data to settings
                        SaveFlightplanDataToSettings();

                        KneeboardLogger.SimBriefDebug("syncFlightplan: Combined flightplan created, length: " + (flightplan != null ? flightplan.Length.ToString() : "null"));
                        reader.Close();
                    }

                    // OFP PDF herunterladen und zur Dokumentenliste hinzufügen
                    if (!string.IsNullOrEmpty(simbriefOFPLink) && System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                    {
                        try
                        {
                            if (!Directory.Exists(simbriefDataDir))
                            {
                                Directory.CreateDirectory(simbriefDataDir);
                            }

                            using (System.Net.WebClient client = new System.Net.WebClient())
                            {
                                client.DownloadFile(new Uri("https://www.simbrief.com/ofp/flightplans/" + simbriefOFPLink), Path.Combine(simbriefDataDir, "OFP.pdf"));
                            }

                            if (instance != null)
                            {
                                if (instance.InvokeRequired)
                                {
                                    instance.BeginInvoke(new Action(() =>
                                    {
                                        if (instance.AddSimbriefOFPToDocumentList())
                                        {
                                            instance.UpdateFileList();
                                            instance.SaveDocumentState();
                                        }
                                    }));
                                }
                                else
                                {
                                    if (instance.AddSimbriefOFPToDocumentList())
                                    {
                                        instance.UpdateFileList();
                                        instance.SaveDocumentState();
                                    }
                                }
                                KneeboardLogger.SimBrief("OFP PDF added to document list");
                            }
                        }
                        catch (Exception ofpEx)
                        {
                            KneeboardLogger.SimBriefError($"OFP PDF download error: {ofpEx.Message}");
                        }
                    }
                }
                catch (System.Net.WebException webEx)
                {
                    KneeboardLogger.SimBriefError($"Sync error (network): {webEx.Message}");
                }
                catch (XmlException xmlEx)
                {
                    KneeboardLogger.SimBriefError($"Sync error (XML parsing): {xmlEx.Message}");
                }
                catch (Exception ex)
                {
                    KneeboardLogger.SimBriefError($"Sync error: {ex.Message}");
                }
            }
            return flightplan;
        }

        /// <summary>
        /// Check if flightplan needs enrichment (no procedures, or procedures exist but all are null/empty)
        /// </summary>
        public static bool FlightplanNeedsEnrichment(string flightplanJson)
        {
            if (string.IsNullOrEmpty(flightplanJson))
                return false;

            if (!flightplanJson.Contains("\"procedures\""))
                return true;

            try
            {
                var jobj = Newtonsoft.Json.Linq.JObject.Parse(flightplanJson);
                var proc = jobj["procedures"];
                if (proc == null || proc.Type == Newtonsoft.Json.Linq.JTokenType.Null)
                {
                    KneeboardLogger.NavigraphDebug("FlightplanNeedsEnrichment: procedures is null");
                    return true;
                }

                var sidWaypoints = proc["sid"]?["waypoints"] as Newtonsoft.Json.Linq.JArray;
                var starWaypoints = proc["star"]?["waypoints"] as Newtonsoft.Json.Linq.JArray;
                var approachWaypoints = proc["approach"]?["waypoints"] as Newtonsoft.Json.Linq.JArray;

                bool hasSid = sidWaypoints != null && sidWaypoints.Count > 0;
                bool hasStar = starWaypoints != null && starWaypoints.Count > 0;
                bool hasApproach = approachWaypoints != null && approachWaypoints.Count > 0;

                KneeboardLogger.NavigraphDebug($"FlightplanNeedsEnrichment - SID: {sidWaypoints?.Count ?? 0}, STAR: {starWaypoints?.Count ?? 0}, Approach: {approachWaypoints?.Count ?? 0}");

                return !hasSid && !hasStar && !hasApproach;
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"FlightplanNeedsEnrichment exception: {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// Enrich flightplan JSON with detailed SID/STAR waypoints
        /// Uses Navigraph (if available) or SimConnect (if MSFS 2024 running)
        /// </summary>
        public static async Task<string> EnrichFlightplanWithProceduresAsync(string flightplanJson)
        {
            KneeboardLogger.NavigraphDebug($"SID/STAR enrichment called with {flightplanJson?.Length ?? 0} bytes");

            if (string.IsNullOrEmpty(flightplanJson))
                return flightplanJson;

            try
            {
                // Parse the flightplan JSON
                dynamic flightplanData = JsonConvert.DeserializeObject(flightplanJson);
                if (flightplanData == null)
                {
                    KneeboardLogger.NavigraphDebug("SID/STAR enrichment: flightplanData is null");
                    return flightplanJson;
                }
                if (flightplanData.pln == null)
                {
                    KneeboardLogger.NavigraphDebug("SID/STAR enrichment: flightplanData.pln is null");
                    return flightplanJson;
                }

                var pln = flightplanData.pln;
                var flightPlan = pln.FlightPlanFlightPlan;
                if (flightPlan == null)
                {
                    KneeboardLogger.NavigraphDebug("SID/STAR enrichment: FlightPlanFlightPlan is null");
                    return flightplanJson;
                }

                string departureIcao = (string)flightPlan.DepartureID;
                string arrivalIcao = (string)flightPlan.DestinationID;
                string sidName = null;
                string starName = null;
                string approachName = null;
                string sidTransition = null;
                string starTransition = null;
                string approachTransition = null;
                string departureRunway = null;
                string arrivalRunway = null;

                // Extract SID/STAR names from ATCWaypoints
                var waypoints = flightPlan.ATCWaypoint;
                if (waypoints != null)
                {
                    foreach (var wp in waypoints)
                    {
                        string departureFP = (string)wp.DepartureFP;
                        string arrivalFP = (string)wp.ArrivalFP;

                        if (!string.IsNullOrEmpty(departureFP))
                            sidName = departureFP;
                        if (!string.IsNullOrEmpty(arrivalFP))
                            starName = arrivalFP;
                    }
                }

                // Extract transitions and runways from OFP data
                var ofp = flightplanData.ofp;
                if (ofp != null)
                {
                    var general = ofp.General ?? ofp.general;
                    var origin = ofp.Origin ?? ofp.origin;
                    var destination = ofp.Destination ?? ofp.destination;

                    if (general != null)
                    {
                        sidTransition = (string)(general.Sid_trans ?? general.sid_trans);
                        starTransition = (string)(general.Star_trans ?? general.star_trans);
                        approachName = (string)(general.Approach_name ?? general.approach_name ??
                                                general.App_name ?? general.app_name ??
                                                general.Appr_type ?? general.appr_type);
                        approachTransition = (string)(general.App_trans ?? general.app_trans ??
                                                      general.Approach_trans ?? general.approach_trans);
                    }
                    if (origin != null)
                    {
                        departureRunway = (string)(origin.Plan_rwy ?? origin.plan_rwy);
                    }
                    if (destination != null)
                    {
                        arrivalRunway = (string)(destination.Plan_rwy ?? destination.plan_rwy);
                    }
                }

                KneeboardLogger.Navigraph($"SID/STAR enrichment - Departure: {departureIcao}, Arrival: {arrivalIcao}");
                KneeboardLogger.NavigraphDebug($"SID/STAR enrichment - SID: {sidName ?? "none"} (trans: {sidTransition ?? "none"}, rwy: {departureRunway ?? "none"})");
                KneeboardLogger.NavigraphDebug($"SID/STAR enrichment - STAR: {starName ?? "none"} (trans: {starTransition ?? "none"}, rwy: {arrivalRunway ?? "none"})");
                KneeboardLogger.NavigraphDebug($"SID/STAR enrichment - Approach: {approachName ?? "none"} (trans: {approachTransition ?? "none"})");

                // Try to get detailed waypoints with transitions
                var sidWaypoints = await GetProcedureWaypointsAsync(departureIcao, sidName, "SID", sidTransition, departureRunway);
                var starWaypoints = await GetProcedureWaypointsAsync(arrivalIcao, starName, "STAR", starTransition, arrivalRunway);
                var approachWaypoints = await GetProcedureWaypointsAsync(arrivalIcao, approachName, "APPROACH", approachTransition, arrivalRunway);

                if (sidWaypoints.Count > 0 || starWaypoints.Count > 0 || approachWaypoints.Count > 0)
                {
                    KneeboardLogger.Navigraph($"SID/STAR enrichment - Got {sidWaypoints.Count} SID, {starWaypoints.Count} STAR, {approachWaypoints.Count} Approach waypoints");

                    // Create enriched flightplan with transition info
                    var enrichedData = new
                    {
                        pln = flightplanData.pln,
                        ofp = flightplanData.ofp,
                        procedures = new
                        {
                            sid = sidWaypoints.Count > 0 ? new
                            {
                                name = sidName,
                                airport = departureIcao,
                                transition = sidTransition,
                                runway = departureRunway,
                                waypoints = sidWaypoints
                            } : null,
                            star = starWaypoints.Count > 0 ? new
                            {
                                name = starName,
                                airport = arrivalIcao,
                                transition = starTransition,
                                runway = arrivalRunway,
                                waypoints = starWaypoints
                            } : null,
                            approach = approachWaypoints.Count > 0 ? new
                            {
                                name = approachName,
                                airport = arrivalIcao,
                                transition = approachTransition,
                                runway = arrivalRunway,
                                waypoints = approachWaypoints
                            } : null
                        }
                    };

                    var result = JsonConvert.SerializeObject(enrichedData);
                    KneeboardLogger.NavigraphDebug($"SID/STAR enrichment - Enriched JSON length: {result.Length}, contains procedures: {result.Contains("procedures")}");
                    return result;
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"SID/STAR enrichment error: {ex.Message}");
                KneeboardLogger.NavigraphError($"SID/STAR enrichment stacktrace: {ex.StackTrace}");
            }

            KneeboardLogger.NavigraphDebug("SID/STAR enrichment: Returning original flightplan (no procedures enriched)");
            return flightplanJson;
        }

        /// <summary>
        /// Get procedure waypoints from available sources
        /// Priority: 1. NavdataDatabase (SQLite), 2. SimConnect live (fallback), 3. BGL files
        /// </summary>
        private static async Task<List<object>> GetProcedureWaypointsAsync(string icao, string procedureName, string type, string transition = null, string runway = null)
        {
            var waypoints = new List<object>();

            if (string.IsNullOrEmpty(icao) || string.IsNullOrEmpty(procedureName))
                return waypoints;

            try
            {
                var navigraphData = SimpleHTTPServer.GetNavigraphData();
                if (navigraphData != null && navigraphData.IsDataAvailable)
                {
                    KneeboardLogger.Navigraph($"Querying for {icao}/{procedureName} type={type} trans={transition ?? "none"} rwy={runway ?? "none"}");

                    var detail = navigraphData.GetProcedureDetail(icao, procedureName, transition, type, runway);
                    if (detail != null && detail.Waypoints != null && detail.Waypoints.Count > 0)
                    {
                        var wpNames = string.Join(", ", detail.Waypoints.Select(w => w.Identifier));
                        KneeboardLogger.NavigraphDebug($"Found {detail.Waypoints.Count} waypoints from Navigraph: {wpNames}");

                        foreach (var wp in detail.Waypoints)
                        {
                            if (Math.Abs(wp.Latitude) < 0.001 && Math.Abs(wp.Longitude) < 0.001)
                                continue;

                            waypoints.Add(new
                            {
                                name = wp.Identifier,
                                lat = wp.Latitude,
                                lng = wp.Longitude,
                                altitude = wp.Altitude1 ?? 0,
                                altitudeConstraint = wp.AltitudeConstraint,
                                speedLimit = wp.SpeedLimit,
                                overfly = wp.Overfly,
                                sequenceNumber = wp.SequenceNumber,
                                routeType = wp.RouteType,
                                transitionIdentifier = wp.TransitionIdentifier
                            });
                        }
                        KneeboardLogger.NavigraphDebug($"Added {waypoints.Count} waypoints (after filtering zero-coords)");
                    }
                    else
                    {
                        KneeboardLogger.Navigraph($"No waypoints found in Navigraph for {procedureName}");
                    }
                }
                else
                {
                    KneeboardLogger.NavigraphDebug("Navigraph data not available");
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Error getting waypoints: {ex.Message}");
            }

            return waypoints;
        }







        private void label1_Click(object sender, EventArgs e)
        {
            string[] filesPath = System.IO.Directory.GetFiles(folderpath + @"\data\");
            foreach (string filePath in filesPath)
            {
                string fileName = System.IO.Path.GetFileName(filePath).ToLower();
                if (fileName.StartsWith("kneeboard_manual"))
                {
                    System.Diagnostics.Process.Start(folderpath + @"\data\manuals\Kneeboard_Manual_EN.pdf");
                    KneeboardLogger.UI("Opening file: " + fileName);
                }
            }
        }

        private void Kneeboard_Server_SizeChanged(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                Hide();
                notifyIcon.Visible = true;
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if(!foldersList.Any(x => x.Name == "newFolder"))
            {
                foldersList.Add(new KneeboardFolder(name: "newFolder", files: new List<KneeboardFile>()));
                UpdateFileList();
                SaveDocumentState();
            }
            else
            {
                MessageBox.Show("A new folder already exists, please rename the newFolder", "Warning");
            }
        }

        private void deleteFolderButton_Click(object sender, EventArgs e)
        {
            if (treeView1.SelectedNode == null)
            {
                return;
            }

            if (treeView1.SelectedNode.Parent == null)
            {
                if (foldersList.Any(item => item.Name == treeView1.SelectedNode.Name))
                {
                    DialogResult dialogResult = MessageBox.Show("Are you sure you like to delete this folder?", "Delete folder", MessageBoxButtons.YesNo);
                    if (dialogResult == DialogResult.Yes)
                    {
                        foldersList.RemoveAll(x => x.Name == treeView1.SelectedNode.Name);
                        UpdateFileList();
                        SaveDocumentState();
                        return;
                    }
                    else if (dialogResult == DialogResult.No)
                    {
                        return;
                    }
                }
            }
        }

        private void notifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            Show();
            this.WindowState = FormWindowState.Normal;
            notifyIcon.Visible = false;
        }

        private void addFolderButton_Enter(object sender, EventArgs e)
        {
            addFolderButton.BackColor = Color.White;
        }

        private void addFolderButton_Leave(object sender, EventArgs e)
        {
            addFolderButton.BackColor = Color.White;
        }

        private void deleteFolderButton_Enter(object sender, EventArgs e)
        {
            deleteFolderButton.BackColor = Color.White;
        }

        private void deleteFolderButton_Leave(object sender, EventArgs e)
        {
            deleteFolderButton.BackColor = Color.White;
        }

        private void addFileButton_Enter(object sender, EventArgs e)
        {
            addFileButton.BackColor = Color.White;
        }

        private void addFileButton_Leave(object sender, EventArgs e)
        {
            addFileButton.BackColor = Color.White;
        }

        private void deleteFileButton_Enter(object sender, EventArgs e)
        {
            deleteFileButton.BackColor = Color.White;
        }

        private void deleteFileButton_Leave(object sender, EventArgs e)
        {
            deleteFileButton.BackColor = Color.White;
        }

        private void loadButton_Enter(object sender, EventArgs e)
        {
            loadButton.BackColor = Color.White;
        }

        private void loadButton_Leave(object sender, EventArgs e)
        {
            loadButton.BackColor = Color.White;
        }

        private async void label1_Click_1(object sender, EventArgs e)
        {
            if (imagesProcessing == false)
            {
                statusBox.Text = "Status: Delete images...";
                System.IO.DirectoryInfo di = new DirectoryInfo(folderpath + "/data/images");

                foreach (FileInfo file in di.GetFiles())
                {
                    file.Delete();
                }
                foreach (DirectoryInfo dir in di.GetDirectories())
                {
                    dir.Delete(true);
                }
                await Task.Delay(2000);
                UpdateFileList();
            }
        }

        private void statusBox_Click(object sender, EventArgs e)
        {
            if (pendingUpdateUrl != null && updateAvailable)
            {
                try { System.Diagnostics.Process.Start(pendingUpdateUrl); } catch { }
            }
        }

        private void treeView1_DoubleClick(object sender, EventArgs e)
        {
            if (treeView1.SelectedNode == null)
                return;

            // Bei Dateien: Original-Quelldatei öffnen
            if (treeView1.SelectedNode.Tag?.ToString() == "file")
            {
                string fileName = treeView1.SelectedNode.Name;
                string folderName = treeView1.SelectedNode.Parent?.Name;

                // Finde die Datei in den Listen
                KneeboardFile fileEntry = null;

                if (folderName != null)
                {
                    // Datei in einem Ordner
                    var folder = foldersList.FirstOrDefault(f => f.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase));
                    if (folder != null && folder.Files != null)
                    {
                        fileEntry = folder.Files.FirstOrDefault(f => f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                    }
                }
                else
                {
                    // Einzelne Datei
                    fileEntry = filesList.FirstOrDefault(f => f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                }

                if (fileEntry != null && System.IO.File.Exists(fileEntry.Path))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(fileEntry.Path);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Fehler beim Öffnen der Datei: {ex.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Datei nicht gefunden oder existiert nicht.", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            // Bei Ordnern: umbenennen
            if (treeView1.SelectedNode.Tag?.ToString() == "folder" && treeView1.SelectedNode.Parent == null)
            {
                EnterFilename testDialog = new EnterFilename();
                string tempFilename = treeView1.SelectedNode.Name;
                testDialog.textBox1.Text = tempFilename;
                testDialog.textBox1.SelectAll();

                // Show testDialog as a modal dialog and determine if DialogResult = OK.
                if (testDialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (testDialog.textBox1.Text != "" && testDialog.textBox1.Text != tempFilename)
                    {
                        if (foldersList.Any(item => item.Name == treeView1.SelectedNode.Name))
                        {
                            foldersList.Where(x => x.Name == treeView1.SelectedNode.Name).ToList().ForEach(b => b.Name = testDialog.textBox1.Text);
                            UpdateFileList();
                            SaveDocumentState();
                        }
                    }
                }
            }
        }

        private void label2_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("http://localhost:815/manuals/Kneeboard_Manual_EN.html");
        }
    }
}
                