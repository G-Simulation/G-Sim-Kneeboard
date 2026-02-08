using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Kneeboard_Server.Logging;
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

        // Navigraph-Ordner: neben der exe (installiert) ODER im Quellverzeichnis (dev)
        private static readonly string NAVIGRAPH_FOLDER = FindNavigraphFolder();
        private static readonly string BUNDLED_DB = "ng_jeppesen_fwdfd_2403.s3db";
        private static readonly string DOWNLOADED_DB = "ng_jeppesen_current.s3db";

        private static string FindNavigraphFolder()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            KneeboardLogger.Navigraph($"BaseDirectory: {baseDir}");

            // 1. Installiert: Navigraph-Ordner neben der exe
            var installedPath = Path.Combine(baseDir, "Navigraph");
            KneeboardLogger.Navigraph($"Check installed path: {installedPath} exists={Directory.Exists(installedPath)}");
            if (Directory.Exists(installedPath))
            {
                var files = Directory.GetFiles(installedPath, "*.s3db");
                KneeboardLogger.Navigraph($"Found {files.Length} s3db files: {string.Join(", ", files.Select(Path.GetFileName))}");
                return installedPath;
            }

            // 2. Entwicklung: bin\x64\Debug -> Projekt-Root
            var devPath = Path.Combine(
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..")),
                "Navigraph");
            KneeboardLogger.Navigraph($"Check dev path: {devPath} exists={Directory.Exists(devPath)}");
            if (Directory.Exists(devPath))
            {
                var files = Directory.GetFiles(devPath, "*.s3db");
                KneeboardLogger.Navigraph($"Found {files.Length} s3db files: {string.Join(", ", files.Select(Path.GetFileName))}");
                return devPath;
            }

            // 3. Fallback: neben der exe anlegen
            KneeboardLogger.NavigraphError($"Kein Navigraph-Ordner gefunden! Erstelle: {installedPath}");
            Directory.CreateDirectory(installedPath);
            return installedPath;
        }

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
        /// First checks if a valid downloaded database exists and uses that instead.
        /// </summary>
        public void LoadBundledDatabase()
        {
            try
            {
                // First check if a downloaded database exists and is valid (in writable LOCALAPPDATA)
                var downloadedPath = Path.Combine(NAVIGRAPH_FOLDER, DOWNLOADED_DB);
                var savedCycle = Settings.Default.values;

                if (File.Exists(downloadedPath) && !string.IsNullOrEmpty(savedCycle))
                {
                    try
                    {
                        // Try to load downloaded database
                        _dbService?.Dispose();
                        _dbService = new NavigraphDbService(downloadedPath);

                        DatabasePath = downloadedPath;
                        CurrentAiracCycle = savedCycle;
                        IsUsingBundledDatabase = false;

                        Log($"Navigraph: Downloaded Database geladen (AIRAC {savedCycle})");
                        return;
                    }
                    catch (Exception ex)
                    {
                        Log($"Navigraph: Downloaded Database fehlerhaft, verwende Bundled: {ex.Message}");
                        // Fall through to load bundled
                    }
                }

                // Load bundled database as fallback
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

                // Check if we already have this cycle (in writable LOCALAPPDATA)
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
                // First call WITHOUT format parameter to see all available packages
                var request = new HttpRequestMessage(HttpMethod.Get, PACKAGES_ENDPOINT);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", _authService.AccessToken);

                Log($"Navigraph: Rufe Package API auf: {PACKAGES_ENDPOINT}");

                var response = await _httpClient.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();

                Log($"Navigraph: Package API Status: {response.StatusCode}");
                Log($"Navigraph: Package API Response: {responseBody}");

                if (!response.IsSuccessStatusCode)
                {
                    Log($"Navigraph: Package API Fehler: {response.StatusCode}");
                    return null;
                }

                // API returns a JSON array of packages
                var packages = JArray.Parse(responseBody);

                Log($"Navigraph: {packages.Count} Paket(e) in Response");

                // Log all available formats
                foreach (var p in packages)
                {
                    var fmt = p["format"]?.ToString() ?? "unknown";
                    var cyc = p["cycle"]?.ToString() ?? "unknown";
                    var stat = p["package_status"]?.ToString() ?? "unknown";
                    Log($"Navigraph: Verfügbares Paket - Format: {fmt}, Cycle: {cyc}, Status: {stat}");
                }

                if (packages.Count == 0)
                {
                    Log("Navigraph: Keine Pakete gefunden - Client hat möglicherweise keinen Navdata-Zugang");
                    return null;
                }

                // Get the first (current) package
                var pkg = packages[0];
                var files = pkg["files"] as JArray;

                // Find the database file
                string downloadUrl = null;
                string hash = null;
                long fileSize = 0;

                if (files != null && files.Count > 0)
                {
                    // Find .s3db file
                    foreach (var file in files)
                    {
                        var key = file["key"]?.ToString() ?? "";
                        if (key.EndsWith(".s3db") || key.EndsWith(".sqlite") || key.EndsWith(".db"))
                        {
                            downloadUrl = file["signed_url"]?.ToString();
                            hash = file["hash"]?.ToString();
                            break;
                        }
                    }

                    // Fallback to first file
                    if (string.IsNullOrEmpty(downloadUrl))
                    {
                        downloadUrl = files[0]["signed_url"]?.ToString();
                        hash = files[0]["hash"]?.ToString();
                    }
                }

                return new NavdataPackage
                {
                    PackageId = pkg["package_id"]?.ToString(),
                    Cycle = pkg["cycle"]?.ToString(),
                    Revision = pkg["revision"]?.ToString() ?? "",
                    Format = pkg["format"]?.ToString(),
                    DownloadUrl = downloadUrl,
                    Hash = hash,
                    FileSize = fileSize,
                    PackageStatus = pkg["package_status"]?.ToString()
                };
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
                if (string.IsNullOrEmpty(package.DownloadUrl))
                {
                    Log("Navigraph: Keine Download-URL vorhanden");
                    return;
                }

                Log($"Navigraph: Starte Download von {package.DownloadUrl.Substring(0, Math.Min(80, package.DownloadUrl.Length))}...");

                // signed_url can be used directly without Authorization header
                var response = await _httpClient.GetAsync(package.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    Log($"Navigraph: Download Fehler: {response.StatusCode}");
                    return;
                }

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var tempPath = targetPath + ".tmp";

                Log($"Navigraph: Lade {totalBytes / 1024 / 1024} MB herunter...");

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long downloadedBytes = 0;
                    int bytesRead;
                    int lastProgress = 0;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0)
                        {
                            var progress = (int)((downloadedBytes * 100) / totalBytes);
                            if (progress != lastProgress)
                            {
                                lastProgress = progress;
                                OnDownloadProgress?.Invoke(this, progress);
                                if (progress % 10 == 0)
                                {
                                    Log($"Navigraph: Download {progress}%");
                                }
                            }
                        }
                    }
                }

                // Verify hash if provided
                if (!string.IsNullOrEmpty(package.Hash))
                {
                    Log("Navigraph: Prüfe Hash...");
                    using (var sha256 = System.Security.Cryptography.SHA256.Create())
                    using (var stream = File.OpenRead(tempPath))
                    {
                        var computedHash = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "").ToLower();
                        if (computedHash != package.Hash.ToLower())
                        {
                            Log($"Navigraph: Hash-Prüfung fehlgeschlagen! Erwartet: {package.Hash}, Berechnet: {computedHash}");
                            File.Delete(tempPath);
                            return;
                        }
                        Log("Navigraph: Hash-Prüfung erfolgreich");
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
                DatabasePath = DatabasePath,
                IsDataAvailable = IsDataAvailable
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
        public ProcedureDetail GetProcedureDetail(string icao, string procedureName, string transition, string type, string runway = null)
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

            // Use GetProcedureLegs for ALL procedure types - it has proper route_type filtering
            // For Approaches with a transition, it includes RouteType 'A' legs for that transition
            List<ProcedureLeg> legs = _dbService.GetProcedureLegs(icao, procedureName, procType, transition, runway);
            Log($"[Navigraph] GetProcedureDetail: {procType} {procedureName} trans='{transition}' rwy='{runway}' -> {legs.Count} legs");

            // For SIDs/STARs, filter to single path to avoid drawing multiple runway transitions
            // Pass both transition AND runway so filtering can select the correct runway transition
            // NOTE: Approaches are NOT filtered here - their SQL query already returns the correct legs
            if (legs.Count > 0 && procType != ProcedureType.Approach)
            {
                Log($"[Navigraph] GetProcedureDetail: Filtering legs (Count: {legs.Count}, Trans: '{transition ?? "null"}', Runway: '{runway ?? "null"}')");

                if (string.IsNullOrEmpty(transition) && string.IsNullOrEmpty(runway))
                {
                    legs = FilterToSinglePath(legs, procType);
                }
                else
                {
                    legs = FilterBySpecificTransition(legs, transition, procType, runway);
                }
            }

            legs = SortProcedureLegs(legs, procType);

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
                    SequenceNumber = leg.SequenceNumber,
                    RouteType = leg.RouteType,
                    TransitionIdentifier = leg.TransitionIdentifier
                })
            };

            return detail;
        }

        /// <summary>
        /// Filter legs to ensure a single valid path is returned.
        /// Filters multiple runway transitions to just the first one.
        /// Filters multiple enroute transitions (for STARs) to just the first one.
        /// Replaces duplicate/overlapping paths with a single representative path.
        /// </summary>
        private List<ProcedureLeg> FilterToSinglePath(List<ProcedureLeg> legs, ProcedureType type)
        {
            if (legs.Count == 0) return legs;

            string firstRunwayTransId = null;
            string firstEnrouteTransId = null;
            bool foundRunwayTrans = false;
            bool foundEnrouteTrans = false;

            // First pass: identify the IDs we want to keep
            foreach (var leg in legs)
            {
                var rt = leg.RouteType;

                // Identify Runway Transition legs
                // SID: 4, STAR: 3, 6
                bool isRunwayTrans = (type == ProcedureType.SID && rt == "4") ||
                                     (type == ProcedureType.STAR && (rt == "3" || rt == "6"));

                if (isRunwayTrans && !foundRunwayTrans)
                {
                    firstRunwayTransId = leg.TransitionIdentifier;
                    foundRunwayTrans = true;
                }

                // Identify Enroute Transition legs (STAR only)
                // SID Enroute transitions are already filtered out by SQL when transition is null
                // STAR: 1, 4
                bool isEnrouteTrans = (type == ProcedureType.STAR && (rt == "1" || rt == "4"));

                if (isEnrouteTrans && !foundEnrouteTrans)
                {
                    firstEnrouteTransId = leg.TransitionIdentifier;
                    foundEnrouteTrans = true;
                }
            }

            // Second pass: Filter
            var filtered = legs.Where(leg =>
            {
                var rt = leg.RouteType;

                // Common Route: Always keep
                // SID: 5, STAR: 2, 5
                bool isCommon = (type == ProcedureType.SID && rt == "5") ||
                                (type == ProcedureType.STAR && (rt == "2" || rt == "5"));
                if (isCommon) return true;

                // Runway Transition: Keep if matches first ID
                bool isRunwayTrans = (type == ProcedureType.SID && rt == "4") ||
                                     (type == ProcedureType.STAR && (rt == "3" || rt == "6"));
                if (isRunwayTrans)
                {
                    return leg.TransitionIdentifier == firstRunwayTransId;
                }

                // Enroute Transition: Keep if matches first ID (STAR only)
                // For SID, if Type 6 exists here (shouldn't), we filter it out? 
                // No, let's allow it if it matches first ID, just to be safe.
                bool isEnrouteTrans = (type == ProcedureType.STAR && (rt == "1" || rt == "4")) ||
                                      (type == ProcedureType.SID && rt == "6");
                if (isEnrouteTrans)
                {
                    // For SID type 6, we didn't capture ID above because we assumed SQL filtered it.
                    // But if SQL didn't, we should handle it.
                    if (type == ProcedureType.SID && rt == "6")
                    {
                        if (firstEnrouteTransId == null) firstEnrouteTransId = leg.TransitionIdentifier;
                        return leg.TransitionIdentifier == firstEnrouteTransId;
                    }
                    return leg.TransitionIdentifier == firstEnrouteTransId;
                }

                // Default keep (for other types if any)
                return true;
            }).ToList();

            Log($"[NavigraphData] Filtered {legs.Count} legs to {filtered.Count} (Single Path - RwyTrans: {firstRunwayTransId ?? "none"}, EnrTrans: {firstEnrouteTransId ?? "none"})");
            return filtered;
        }

        private static int GetRouteTypeOrder(string routeType, ProcedureType type)
        {
            if (type == ProcedureType.SID)
            {
                if (routeType == "4") return 0;
                if (routeType == "5") return 1;
                if (routeType == "6") return 2;
                return 3;
            }

            if (type == ProcedureType.STAR)
            {
                if (routeType == "1" || routeType == "4") return 0;
                if (routeType == "2" || routeType == "5") return 1;
                if (routeType == "3" || routeType == "6") return 2;
                return 3;
            }

            // Approach: A=Transition, I=Initial, D=Intermediate, F=Final, Z=Missed
            if (type == ProcedureType.Approach)
            {
                if (routeType == "A") return 0;
                if (routeType == "I") return 1;
                if (routeType == "D") return 2;
                if (routeType == "F") return 3;
                if (routeType == "Z") return 4;
                return 5;
            }

            return 0;
        }

        private static List<ProcedureLeg> SortProcedureLegs(List<ProcedureLeg> legs, ProcedureType procType)
        {
            if (legs == null || legs.Count <= 1) return legs;

            return legs
                .Select((leg, idx) => new { leg, idx })
                .OrderBy(x => GetRouteTypeOrder(x.leg.RouteType, procType))
                .ThenBy(x => x.leg.SequenceNumber)
                .ThenBy(x => x.idx)
                .Select(x => x.leg)
                .ToList();
        }

        /// <summary>
        /// Get procedure detail (with ProcedureType enum)
        /// </summary>
        public ProcedureDetail GetProcedureDetail(string icao, string procedureName, string transition, ProcedureType procType, string runway = null)
        {
            return GetProcedureDetail(icao, procedureName, transition, procType.ToString(), runway);
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
        /// Filter procedure legs to keep only legs belonging to the specific transition
        /// This prevents drawing all transitions and shows only the selected one
        /// </summary>
        private List<ProcedureLeg> FilterBySpecificTransition(List<ProcedureLeg> legs, string transition, ProcedureType procType, string runway = null)
        {
            // Return early only if no legs, OR if BOTH transition AND runway are empty
            if (legs.Count == 0) return legs;
            if (string.IsNullOrEmpty(transition) && string.IsNullOrEmpty(runway)) return legs;

            KneeboardLogger.NavigraphDebug($"[Navigraph] FilterBySpecificTransition: filtering {legs.Count} legs for transition '{transition ?? "null"}', runway '{runway ?? "null"}' ({procType})");

            // Group legs by route_type and transition_identifier
            var legGroups = legs.GroupBy(leg => new { 
                RouteType = leg.RouteType ?? "", 
                TransitionId = leg.TransitionIdentifier ?? "" 
            }).ToList();

            KneeboardLogger.NavigraphDebug($"[Navigraph] Found {legGroups.Count()} leg groups:");
            foreach (var group in legGroups)
            {
                KneeboardLogger.NavigraphDebug($"  - RouteType: {group.Key.RouteType}, TransitionId: '{group.Key.TransitionId}', Count: {group.Count()}");
            }

            var filteredLegs = new List<ProcedureLeg>();

            if (procType == ProcedureType.SID)
            {
                foreach (var group in legGroups)
                {
                    var routeType = group.Key.RouteType;
                    var transitionId = group.Key.TransitionId;

                    if (routeType == "5")
                    {
                        filteredLegs.AddRange(group);
                        KneeboardLogger.NavigraphDebug($"  -> Including Common Route legs (RouteType 5)");
                    }
                    else if (routeType == "6" && transitionId == transition)
                    {
                        filteredLegs.AddRange(group);
                        KneeboardLogger.NavigraphDebug($"  -> Including Enroute Transition legs (RouteType 6, TransitionId '{transitionId}')");
                    }
                    else if (routeType == "4")
                    {
                        var hasRunwayTransition = filteredLegs.Any(l => l.RouteType == "4");

                        // If no runway specified, only include the FIRST runway transition
                        if (string.IsNullOrEmpty(runway))
                        {
                            if (!hasRunwayTransition)
                            {
                                filteredLegs.AddRange(group);
                                KneeboardLogger.NavigraphDebug($"  -> Including first Runway Transition legs (RouteType 4, TransitionId '{transitionId}') - no runway specified");
                            }
                            else
                            {
                                KneeboardLogger.NavigraphDebug($"  -> Skipping additional Runway Transition (RouteType 4, TransitionId '{transitionId}') - already have one");
                            }
                        }
                        else
                        {
                            // Normalize runway for comparison
                            string normalizedRunway = runway.StartsWith("RW") ? runway : $"RW{runway}";
                            string normalizedTransId = transitionId ?? "";

                            // Check if this transition matches the selected runway
                            bool matchesRunway = normalizedTransId == normalizedRunway ||
                                                 normalizedTransId == runway ||
                                                 normalizedTransId.Replace("RW", "") == runway.Replace("RW", "");

                            if (matchesRunway)
                            {
                                filteredLegs.AddRange(group);
                                KneeboardLogger.NavigraphDebug($"  -> Including matching Runway Transition legs (RouteType 4, TransitionId '{transitionId}', matches runway '{runway}')");
                            }
                            else
                            {
                                KneeboardLogger.NavigraphDebug($"  -> Skipping non-matching Runway Transition (RouteType 4, TransitionId '{transitionId}', expected runway '{runway}')");
                            }
                        }
                    }
                }
            }
            else if (procType == ProcedureType.STAR)
            {
                foreach (var group in legGroups)
                {
                    var routeType = group.Key.RouteType;
                    var transitionId = group.Key.TransitionId;

                    if (routeType == "2" || routeType == "5")
                    {
                        filteredLegs.AddRange(group);
                        KneeboardLogger.NavigraphDebug($"  -> Including Common Route legs (RouteType {routeType})");
                    }
                    else if ((routeType == "1" || routeType == "4") && transitionId == transition)
                    {
                        filteredLegs.AddRange(group);
                        KneeboardLogger.NavigraphDebug($"  -> Including Enroute Transition legs (RouteType {routeType}, TransitionId '{transitionId}')");
                    }
                    else if (routeType == "3" || routeType == "6")
                    {
                        var hasRunwayTransition = filteredLegs.Any(l => l.RouteType == "3" || l.RouteType == "6");

                        // If no runway specified, only include the FIRST runway transition
                        if (string.IsNullOrEmpty(runway))
                        {
                            if (!hasRunwayTransition)
                            {
                                filteredLegs.AddRange(group);
                                KneeboardLogger.NavigraphDebug($"  -> Including first Runway Transition legs (RouteType {routeType}, TransitionId '{transitionId}') - no runway specified");
                            }
                            else
                            {
                                KneeboardLogger.NavigraphDebug($"  -> Skipping additional Runway Transition (RouteType {routeType}, TransitionId '{transitionId}') - already have one");
                            }
                        }
                        else
                        {
                            // Normalize runway for comparison
                            string normalizedRunway = runway.StartsWith("RW") ? runway : $"RW{runway}";
                            string normalizedTransId = transitionId ?? "";

                            // Check if this transition matches the selected runway
                            bool matchesRunway = normalizedTransId == normalizedRunway ||
                                                 normalizedTransId == runway ||
                                                 normalizedTransId.Replace("RW", "") == runway.Replace("RW", "");

                            if (matchesRunway)
                            {
                                filteredLegs.AddRange(group);
                                KneeboardLogger.NavigraphDebug($"  -> Including matching Runway Transition legs (RouteType {routeType}, TransitionId '{transitionId}', matches runway '{runway}')");
                            }
                            else
                            {
                                KneeboardLogger.NavigraphDebug($"  -> Skipping non-matching Runway Transition (RouteType {routeType}, TransitionId '{transitionId}', expected runway '{runway}')");
                            }
                        }
                    }
                }
            }

            KneeboardLogger.NavigraphDebug($"[Navigraph] FilterBySpecificTransition: filtered from {legs.Count} to {filteredLegs.Count} legs");

            return SortProcedureLegs(filteredLegs, procType);
        }

        /// <summary>
        /// Log message
        /// </summary>
        private void Log(string message)
        {
            KneeboardLogger.Navigraph(message);
            OnLog?.Invoke(this, message);
        }
    }
}
