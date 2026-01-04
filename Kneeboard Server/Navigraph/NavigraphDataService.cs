using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Kneeboard_Server.Properties;

namespace Kneeboard_Server.Navigraph
{
    /// <summary>
    /// Navigraph Data Service for managing navdata packages and database access
    /// </summary>
    public class NavigraphDataService
    {
        // API Endpoints
        private const string NAVDATA_API = "https://api.navigraph.com/v1/navdata";
        private const string PACKAGES_ENDPOINT = NAVDATA_API + "/packages";

        // Paths
        private static readonly string NAVIGRAPH_FOLDER = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Navigraph");
        private static readonly string BUNDLED_DB = "ng_jeppesen_fwdfd_2403.s3db";
        private static readonly string DOWNLOADED_DB = "ng_jeppesen_current.s3db";

        // Services
        private readonly NavigraphAuthService _authService;
        private readonly HttpClient _httpClient;
        private NavigraphDbService _dbService;

        // Events
        public event EventHandler<string> OnLog;
        public event EventHandler<int> OnDownloadProgress;
        public event EventHandler<NavigraphStatus> OnStatusChanged;

        // Properties
        public string DatabasePath { get; private set; }
        public string CurrentAiracCycle { get; private set; }
        public bool IsDataAvailable => _dbService != null && _dbService.IsConnected;
        public bool IsUsingBundledDatabase { get; private set; }
        public NavigraphDbService DbService => _dbService;

        public NavigraphDataService(NavigraphAuthService authService)
        {
            _authService = authService;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "KneeboardServer/2.0");

            // Ensure Navigraph folder exists
            if (!Directory.Exists(NAVIGRAPH_FOLDER))
            {
                Directory.CreateDirectory(NAVIGRAPH_FOLDER);
            }
        }

        /// <summary>
        /// Initialize the data service - loads bundled database as default
        /// </summary>
        public async Task InitializeAsync()
        {
            Log("Navigraph: Initialisiere Navdata Service...");

            // Step 1: Always load bundled database first (available for all users)
            LoadBundledDatabase();

            // Step 2: If user is authenticated with subscription, try to get latest data
            if (_authService.IsAuthenticated && _authService.HasFmsDataSubscription)
            {
                Log("Navigraph: Benutzer hat FMS Data Subscription - prüfe auf Updates...");
                await CheckAndDownloadUpdatesAsync();
            }
            else if (_authService.IsAuthenticated)
            {
                Log("Navigraph: Benutzer angemeldet aber keine FMS Data Subscription - verwende Bundled Database");
            }
            else
            {
                Log("Navigraph: Nicht angemeldet - verwende Bundled Database");
            }

            NotifyStatusChanged();
        }

        /// <summary>
        /// Load the bundled database (fallback for all users)
        /// </summary>
        public void LoadBundledDatabase()
        {
            try
            {
                var bundledPath = Path.Combine(NAVIGRAPH_FOLDER, BUNDLED_DB);

                if (!File.Exists(bundledPath))
                {
                    Log($"Navigraph: FEHLER - Bundled Database nicht gefunden: {bundledPath}");
                    return;
                }

                DatabasePath = bundledPath;
                IsUsingBundledDatabase = true;

                // Extract AIRAC cycle from filename (ng_jeppesen_fwdfd_2403.s3db -> 2403)
                var match = System.Text.RegularExpressions.Regex.Match(BUNDLED_DB, @"_(\d{4})\.s3db$");
                CurrentAiracCycle = match.Success ? match.Groups[1].Value : "Unknown";

                // Initialize or reinitialize database service
                _dbService?.Dispose();
                _dbService = new NavigraphDbService(bundledPath);

                Log($"Navigraph: Bundled Database geladen (AIRAC {CurrentAiracCycle})");
            }
            catch (Exception ex)
            {
                Log($"Navigraph: Fehler beim Laden der Bundled Database: {ex.Message}");
            }
        }

        /// <summary>
        /// Check for updates and download if user has subscription
        /// </summary>
        public async Task CheckAndDownloadUpdatesAsync()
        {
            try
            {
                if (!_authService.HasFmsDataSubscription)
                {
                    Log("Navigraph: Keine FMS Data Subscription - Update übersprungen");
                    return;
                }

                // Ensure valid token
                if (!await _authService.EnsureValidTokenAsync())
                {
                    Log("Navigraph: Token ungültig - Update übersprungen");
                    return;
                }

                // Get latest package info
                var package = await GetLatestPackageInfoAsync();
                if (package == null)
                {
                    Log("Navigraph: Konnte Package-Info nicht abrufen");
                    return;
                }

                Log($"Navigraph: Verfügbarer AIRAC Cycle: {package.Cycle}");

                // Check if we already have this cycle
                var downloadedPath = Path.Combine(NAVIGRAPH_FOLDER, DOWNLOADED_DB);
                var savedCycle = Settings.Default.values; // Use existing setting for cycle info

                if (File.Exists(downloadedPath) && savedCycle == package.Cycle)
                {
                    Log($"Navigraph: Aktuelle Database bereits vorhanden (AIRAC {package.Cycle})");
                    LoadDownloadedDatabase(downloadedPath, package.Cycle);
                    return;
                }

                // Download new database
                Log($"Navigraph: Lade AIRAC {package.Cycle} herunter...");
                await DownloadPackageAsync(package, downloadedPath);

                // Save cycle info
                Settings.Default.values = package.Cycle;
                Settings.Default.Save();

                // Load the new database
                LoadDownloadedDatabase(downloadedPath, package.Cycle);
            }
            catch (Exception ex)
            {
                Log($"Navigraph: Update-Fehler: {ex.Message}");
                // Keep using bundled database on error
            }
        }

        /// <summary>
        /// Get latest navdata package info from Navigraph API
        /// </summary>
        public async Task<NavdataPackage> GetLatestPackageInfoAsync()
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, PACKAGES_ENDPOINT);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", _authService.AccessToken);

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    Log($"Navigraph: Package API Fehler: {response.StatusCode}");
                    return null;
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(responseBody);

                // Find DFD package (we need the SQLite format)
                var packages = json["packages"] as JArray;
                if (packages != null)
                {
                    foreach (var pkg in packages)
                    {
                        var format = pkg["format"]?.ToString();
                        if (format == "dfd" || format == "sqlite" || format == "fwdfd")
                        {
                            return new NavdataPackage
                            {
                                PackageId = pkg["id"]?.ToString(),
                                Cycle = pkg["cycle"]?.ToString(),
                                Revision = pkg["revision"]?.ToString() ?? "",
                                Format = format,
                                DownloadUrl = pkg["url"]?.ToString(),
                                FileSize = pkg["size"]?.ToObject<long>() ?? 0
                            };
                        }
                    }
                }

                // Alternative: Try single package response format
                var cycle = json["cycle"]?.ToString();
                if (!string.IsNullOrEmpty(cycle))
                {
                    return new NavdataPackage
                    {
                        Cycle = cycle,
                        DownloadUrl = json["url"]?.ToString()
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                Log($"Navigraph: Package Info Fehler: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Download navdata package from Navigraph
        /// </summary>
        private async Task DownloadPackageAsync(NavdataPackage package, string targetPath)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, package.DownloadUrl);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", _authService.AccessToken);

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    Log($"Navigraph: Download Fehler: {response.StatusCode}");
                    return;
                }

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var tempPath = targetPath + ".tmp";

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long downloadedBytes = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0)
                        {
                            var progress = (int)((downloadedBytes * 100) / totalBytes);
                            OnDownloadProgress?.Invoke(this, progress);
                        }
                    }
                }

                // Check if it's a ZIP file and extract
                if (IsZipFile(tempPath))
                {
                    Log("Navigraph: Extrahiere ZIP Archiv...");
                    ExtractDatabase(tempPath, targetPath);
                    File.Delete(tempPath);
                }
                else
                {
                    // It's already a SQLite file
                    if (File.Exists(targetPath))
                        File.Delete(targetPath);
                    File.Move(tempPath, targetPath);
                }

                Log($"Navigraph: Download abgeschlossen - {new FileInfo(targetPath).Length / 1024 / 1024} MB");
            }
            catch (Exception ex)
            {
                Log($"Navigraph: Download Fehler: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Check if file is a ZIP archive
        /// </summary>
        private bool IsZipFile(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var header = new byte[4];
                    stream.Read(header, 0, 4);
                    // ZIP magic number: 0x504B0304
                    return header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Extract SQLite database from ZIP archive
        /// </summary>
        private void ExtractDatabase(string zipPath, string targetPath)
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.EndsWith(".s3db", StringComparison.OrdinalIgnoreCase) ||
                        entry.Name.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ||
                        entry.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(targetPath))
                            File.Delete(targetPath);

                        entry.ExtractToFile(targetPath);
                        Log($"Navigraph: Extrahiert: {entry.Name}");
                        return;
                    }
                }
            }

            throw new Exception("Keine Datenbank in ZIP gefunden");
        }

        /// <summary>
        /// Load the downloaded (current) database
        /// </summary>
        private void LoadDownloadedDatabase(string path, string cycle)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Log($"Navigraph: Downloaded Database nicht gefunden: {path}");
                    return;
                }

                DatabasePath = path;
                CurrentAiracCycle = cycle;
                IsUsingBundledDatabase = false;

                // Reinitialize database service
                _dbService?.Dispose();
                _dbService = new NavigraphDbService(path);

                Log($"Navigraph: Aktuelle Database geladen (AIRAC {cycle})");
                NotifyStatusChanged();
            }
            catch (Exception ex)
            {
                Log($"Navigraph: Fehler beim Laden der Downloaded Database: {ex.Message}");
                // Fallback to bundled
                LoadBundledDatabase();
            }
        }

        /// <summary>
        /// Force reload of database (e.g., after user logs in)
        /// </summary>
        public async Task ReloadAsync()
        {
            await InitializeAsync();
        }

        /// <summary>
        /// Get current status
        /// </summary>
        public NavigraphStatus GetStatus()
        {
            return new NavigraphStatus
            {
                IsAuthenticated = _authService.IsAuthenticated,
                Username = _authService.Username,
                HasFmsDataSubscription = _authService.HasFmsDataSubscription,
                CurrentAiracCycle = CurrentAiracCycle,
                IsUsingBundledDatabase = IsUsingBundledDatabase,
                DatabasePath = DatabasePath
            };
        }

        #region Database Query Wrappers

        /// <summary>
        /// Get airport information
        /// </summary>
        public AirportInfo GetAirport(string icao)
        {
            return _dbService?.GetAirport(icao);
        }

        /// <summary>
        /// Get runways for an airport
        /// </summary>
        public List<RunwayInfo> GetRunways(string icao)
        {
            return _dbService?.GetRunways(icao) ?? new List<RunwayInfo>();
        }

        /// <summary>
        /// Get a specific runway
        /// </summary>
        public RunwayInfo GetRunway(string icao, string runwayId)
        {
            return _dbService?.GetRunway(icao, runwayId);
        }

        /// <summary>
        /// Get ILS data for an airport
        /// </summary>
        public List<ILSData> GetILSData(string icao)
        {
            return _dbService?.GetILS(icao) ?? new List<ILSData>();
        }

        /// <summary>
        /// Get SID procedures for an airport
        /// </summary>
        public List<ProcedureSummary> GetSIDs(string icao)
        {
            return _dbService?.GetSIDs(icao) ?? new List<ProcedureSummary>();
        }

        /// <summary>
        /// Get STAR procedures for an airport
        /// </summary>
        public List<ProcedureSummary> GetSTARs(string icao)
        {
            return _dbService?.GetSTARs(icao) ?? new List<ProcedureSummary>();
        }

        /// <summary>
        /// Get approach procedures for an airport
        /// </summary>
        public List<ApproachSummary> GetApproaches(string icao)
        {
            return _dbService?.GetApproaches(icao) ?? new List<ApproachSummary>();
        }

        /// <summary>
        /// Get procedure detail (legs)
        /// </summary>
        public ProcedureDetail GetProcedureDetail(string icao, string procedureName, string transition, string type)
        {
            if (_dbService == null) return null;

            ProcedureType procType;
            switch (type?.ToUpperInvariant())
            {
                case "SID":
                    procType = ProcedureType.SID;
                    break;
                case "STAR":
                    procType = ProcedureType.STAR;
                    break;
                case "APPROACH":
                case "IAP":
                    procType = ProcedureType.Approach;
                    break;
                default:
                    procType = ProcedureType.SID;
                    break;
            }

            var legs = _dbService.GetProcedureLegs(icao, procedureName, procType);

            var detail = new ProcedureDetail
            {
                Identifier = procedureName,
                Airport = icao,
                Transition = transition,
                Type = procType,
                AiracCycle = CurrentAiracCycle,
                Waypoints = legs.ConvertAll(leg => new ProcedureWaypoint
                {
                    Identifier = leg.WaypointIdentifier,
                    Latitude = leg.Latitude,
                    Longitude = leg.Longitude,
                    PathTerminator = leg.PathTerminator,
                    AltitudeConstraint = leg.AltitudeConstraint,
                    Altitude1 = leg.Altitude1,
                    Altitude2 = leg.Altitude2,
                    SpeedLimit = leg.SpeedLimit,
                    Course = leg.Course,
                    Distance = leg.Distance,
                    Overfly = leg.Overfly,
                    SequenceNumber = leg.SequenceNumber
                })
            };

            return detail;
        }

        /// <summary>
        /// Get procedure detail (with ProcedureType enum)
        /// </summary>
        public ProcedureDetail GetProcedureDetail(string icao, string procedureName, string transition, ProcedureType procType)
        {
            return GetProcedureDetail(icao, procedureName, transition, procType.ToString());
        }

        /// <summary>
        /// Get waypoint information
        /// </summary>
        public WaypointInfo GetWaypoint(string ident, string region = null)
        {
            return _dbService?.GetWaypoint(ident, region);
        }

        /// <summary>
        /// Get navaid information
        /// </summary>
        public NavaidInfo GetNavaid(string ident)
        {
            return _dbService?.GetNavaid(ident);
        }

        /// <summary>
        /// Get navaids near a position
        /// </summary>
        public List<NavaidInfo> GetNavaidsNear(double lat, double lon, double radiusNm)
        {
            return _dbService?.GetNavaidsNear(lat, lon, radiusNm) ?? new List<NavaidInfo>();
        }

        /// <summary>
        /// Get airway information
        /// </summary>
        public AirwayInfo GetAirway(string ident)
        {
            return _dbService?.GetAirway(ident);
        }

        #endregion

        /// <summary>
        /// Notify status change
        /// </summary>
        private void NotifyStatusChanged()
        {
            OnStatusChanged?.Invoke(this, GetStatus());
        }

        /// <summary>
        /// Log message
        /// </summary>
        private void Log(string message)
        {
            Console.WriteLine(message);
            OnLog?.Invoke(this, message);
        }
    }
}
