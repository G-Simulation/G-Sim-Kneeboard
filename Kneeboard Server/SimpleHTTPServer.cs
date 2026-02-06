// MIT License - Copyright (c) 2016 Can Güney Aksakalli
// https://aksakalli.github.io/2014/02/24/simple-http-server-with-csparp.html

using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kneeboard_Server.Logging;
using Kneeboard_Server.Navigraph;
using static Kneeboard_Server.Kneeboard_Server;
using EmbedIO;
using EmbedIO.Actions;
using EmbedIO.Files;

namespace Kneeboard_Server
{
    public class SimpleHTTPServer
    {
        private Kneeboard_Server _kneeboardServer;

        // Navigraph Integration
        private static NavigraphAuthService _navigraphAuth;
        private static NavigraphDataService _navigraphData;
        private static readonly object _navigraphLock = new object();
        // Load API key: first from secrets.config (via AppSettings), fallback to user settings, then hardcoded default
        private string OPENAIP_API_KEY
        {
            get
            {
                // Try secrets.config first
                string key = ConfigurationManager.AppSettings["OpenAipApiKey"];
                if (!string.IsNullOrEmpty(key))
                    return key;

                // Fallback to user settings
                key = Properties.Settings.Default.openaip;
                if (!string.IsNullOrEmpty(key))
                    return key;

                // Last resort: hardcoded default (from original Settings.Designer.cs)
                return "d41058a9711be731a31a63fab762175c";
            }
        }

        // Shared HttpClient Instance (wiederverwendbar, thread-safe, besser als WebClient)
        private static readonly HttpClient _httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        // Source-Verzeichnis: Statische Daten (data\) liegen neben der exe
        private static readonly string SOURCE_DIR = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

        // Cache-Verzeichnis: %LOCALAPPDATA% statt ProgramData (Schreibrechte ohne Admin)
        private static readonly string CACHE_BASE_DIR = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Gsimulations", "Kneeboard Server", "cache");

        // OpenAIP Cache Configuration
        private static readonly string CACHE_DIR = Path.Combine(CACHE_BASE_DIR, "openaip");
        private static readonly TimeSpan CACHE_TTL = TimeSpan.FromDays(7);
        private static readonly object _cacheLock = new object();

        // FIR Boundaries - Permanente lokale Speicherung mit 7-Tage Auto-Update
        private static readonly string BOUNDARIES_CACHE_DIR = Path.Combine(CACHE_BASE_DIR, "boundaries");
        private static readonly string BOUNDARIES_DATA_DIR = Path.Combine(SOURCE_DIR, "data", "boundaries");
        private static readonly TimeSpan BOUNDARIES_CACHE_TTL = TimeSpan.FromDays(7); // 7 Tage statt 24h
        private static string _cachedVatsimBoundaries = null;
        private static DateTime _vatsimBoundariesCacheTime = DateTime.MinValue;
        private static string _cachedIvaoBoundaries = null;
        private static DateTime _ivaoBoundariesCacheTime = DateTime.MinValue;
        private static string _cachedVatspyFirNames = null;
        private static DateTime _vatspyFirNamesCacheTime = DateTime.MinValue;
        private static string _cachedVatsimTraconBoundaries = null;
        private static DateTime _vatsimTraconBoundariesCacheTime = DateTime.MinValue;
        private static readonly object _boundariesCacheLock = new object();

        // VATSIM/IVAO Piloten-Daten Cache (Hybrid-Ansatz)
        private static string _cachedVatsimPilots = null;
        private static DateTime _vatsimPilotsCacheTime = DateTime.MinValue;
        private static readonly TimeSpan PILOTS_CACHE_TTL = TimeSpan.FromSeconds(30); // 30 Sekunden Cache (reduziert Server-Last)
        private static string _cachedIvaoPilots = null;
        private static DateTime _ivaoPilotsCacheTime = DateTime.MinValue;
        private static readonly object _pilotsCacheLock = new object();

        // Vorverarbeitete Boundaries mit Bounding-Boxes (HYBRID)
        private static string _preprocessedVatsimBoundaries = null;
        private static DateTime _preprocessedVatsimBoundariesTime = DateTime.MinValue;
        private static string _preprocessedIvaoBoundaries = null;
        private static DateTime _preprocessedIvaoBoundariesTime = DateTime.MinValue;

        // Pilot Favorites Storage
        private static readonly string FAVORITES_FILE = Path.Combine(CACHE_BASE_DIR, "pilot_favorites.json");
        private static readonly object _favoritesLock = new object();

        // Baselayer Tile Cache Configuration
        private static readonly string BASELAYER_CACHE_DIR = Path.Combine(CACHE_BASE_DIR, "tiles");
        private static readonly TimeSpan BASELAYER_CACHE_TTL = TimeSpan.FromDays(30); // 30 Tage Cache für Baselayer
        private static readonly object _baselayerCacheLock = new object();

        // Aircraft-Kategorien (Server-seitige Klassifizierung)
        private static readonly HashSet<string> SUPER_HEAVY_TYPES = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "A380", "A388", "A38F", "A3ST", "BLCF", "AN22", "AN24", "AN225", "AN25", "C5", "C5M", "SLCM"
        };
        private static readonly HashSet<string> HEAVY_TYPES = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "A332", "A333", "A339", "A338", "A342", "A343", "A345", "A346", "A359", "A35K", "A306", "A30B", "A3ST",
            "B744", "B748", "B74D", "B74F", "B74R", "B74S", "B752", "B753", "B762", "B763", "B764", "B772", "B773",
            "B77L", "B77W", "B778", "B779", "B788", "B789", "B78X", "IL96", "IL86", "IL62", "MD11", "DC10",
            "C17", "C135", "C141", "KC10", "KC30", "KC35", "KC46", "A310", "A400", "C130", "L100"
        };
        private static readonly HashSet<string> TURBOPROP_TYPES = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AT42", "AT43", "AT45", "AT46", "AT72", "AT73", "AT75", "AT76", "DH8A", "DH8B", "DH8C", "DH8D",
            "JS31", "JS32", "JS41", "ATP", "B190", "BE99", "BE9L", "BE20", "BE30", "BE35", "BE36", "B350",
            "SF34", "SW3", "SW4", "C208", "PC12", "TBM7", "TBM8", "TBM9", "P180", "U21", "E120", "F27", "F50"
        };
        private static readonly HashSet<string> HELI_TYPES = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "A109", "A119", "A129", "A139", "A149", "A169", "A189", "AS32", "AS50", "AS55", "AS65",
            "B06", "B06T", "B105", "B205", "B206", "B212", "B214", "B222", "B230", "B407", "B412", "B429", "B430",
            "EC20", "EC25", "EC30", "EC35", "EC45", "EC55", "EC75", "EC120", "EC130", "EC135", "EC145", "EC155", "EC175", "EC225",
            "H125", "H130", "H135", "H145", "H155", "H160", "H175", "H215", "H225", "H500", "H520", "H600",
            "MD52", "MD60", "MD90", "S61", "S64", "S70", "S76", "S92", "R22", "R44", "R66", "UH1", "UH60", "AH64", "CH47", "CH53", "V22"
        };
        private static readonly HashSet<string> MILITARY_PREFIXES = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RCH", "DUKE", "REACH", "EVAC", "MOOSE", "TANGO", "CNV", "CONVOY", "RRR", "REDHAWK", "BOLT", "VIPER",
            "TOPCAT", "HAVOC", "SENTRY", "COBRA", "RAPTOR", "TALON", "VIKING", "DEMON", "LANCER", "BONES", "SKULL"
        };
        private static readonly HashSet<string> MILITARY_AIRCRAFT_TYPES = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "F16", "F15", "F15C", "F15E", "F18", "FA18", "F22", "F35", "F35A", "F35B", "F35C", "F117", "B1", "B1B",
            "B2", "B52", "B52H", "A10", "A10C", "C17", "C130", "C130J", "C5", "C5M", "KC10", "KC135", "KC46",
            "E3", "E3A", "E3B", "E3C", "E3G", "E6", "E6B", "E8", "P3", "P8", "P8A", "U2", "SR71", "RQ4", "MQ9",
            "EF2K", "EUFI", "F4", "F4E", "F104", "F111", "TFND", "MRTT", "A400", "A400M"
        };

        public SimpleHTTPServer(string path, int port, Kneeboard_Server kneeboardServer)
        {
            this._kneeboardServer = kneeboardServer;
            this.Initialize(path, port);

            // Start loading global airport database in background
            StartGlobalAirportIndexLoad();

            // Start loading/updating boundaries in background (VATSIM, IVAO, TRACON)
            StartBoundariesUpdateCheck();

            // Initialize Navigraph services
            InitializeNavigraphServices();
        }

        /// <summary>
        /// Initialize Navigraph authentication and data services
        /// </summary>
        private static void InitializeNavigraphServices()
        {
            lock (_navigraphLock)
            {
                if (_navigraphAuth == null)
                {
                    _navigraphAuth = new NavigraphAuthService();
                    _navigraphData = new NavigraphDataService(_navigraphAuth);

                    // Load bundled database immediately (synchronous)
                    KneeboardLogger.Navigraph("Loading bundled database...");
                    _navigraphData.LoadBundledDatabase();

                    // Enrich any persisted flightplan now that Navigraph is available
                    KneeboardLogger.NavigraphDebug($"Checking if flightplan needs enrichment (length: {Kneeboard_Server.flightplan?.Length ?? 0})");
                    bool needsEnrichment = Kneeboard_Server.FlightplanNeedsEnrichment(Kneeboard_Server.flightplan);
                    KneeboardLogger.NavigraphDebug($"FlightplanNeedsEnrichment result: {needsEnrichment}");
                    
                    if (needsEnrichment)
                    {
                        KneeboardLogger.Navigraph("Enriching persisted flightplan with procedures...");
                        try
                        {
                            var enrichedFlightplan = Kneeboard_Server.EnrichFlightplanWithProceduresAsync(Kneeboard_Server.flightplan).GetAwaiter().GetResult();
                            KneeboardLogger.NavigraphDebug($"Enrichment returned, length: {enrichedFlightplan?.Length ?? 0}");
                            
                            bool stillNeedsEnrichment = Kneeboard_Server.FlightplanNeedsEnrichment(enrichedFlightplan);
                            KneeboardLogger.NavigraphDebug($"Enriched flightplan still needs enrichment: {stillNeedsEnrichment}");
                            
                            if (!stillNeedsEnrichment)
                            {
                                Kneeboard_Server.flightplan = enrichedFlightplan;
                                Kneeboard_Server.SaveFlightplanDataToSettings();
                                KneeboardLogger.Navigraph("Persisted flightplan enriched and saved");
                            }
                            else
                            {
                                KneeboardLogger.Warn("Navigraph", "Enrichment completed but no procedure waypoints found");
                            }
                        }
                        catch (Exception enrichEx)
                        {
                            KneeboardLogger.NavigraphError($"Flightplan enrichment failed: {enrichEx.Message}");
                            KneeboardLogger.NavigraphError(enrichEx);
                        }
                    }
                    else if (!string.IsNullOrEmpty(Kneeboard_Server.flightplan))
                    {
                        KneeboardLogger.NavigraphDebug("Persisted flightplan already has procedure waypoints");
                    }

                    // If already authenticated, check for updates asynchronously
                    if (_navigraphAuth.IsAuthenticated)
                    {
                        KneeboardLogger.Navigraph("User already authenticated, checking for navdata updates...");
                        Task.Run(async () =>
                        {
                            try
                            {
                                await _navigraphData.CheckAndDownloadUpdatesAsync();
                            }
                            catch (Exception ex)
                            {
                                KneeboardLogger.NavigraphError($"Update check failed: {ex.Message}");
                            }
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Get Navigraph authentication service (for UI integration)
        /// </summary>
        public static NavigraphAuthService GetNavigraphAuth()
        {
            lock (_navigraphLock)
            {
                if (_navigraphAuth == null)
                {
                    InitializeNavigraphServices();
                }
                return _navigraphAuth;
            }
        }

        /// <summary>
        /// Get Navigraph data service (for UI integration)
        /// </summary>
        public static NavigraphDataService GetNavigraphData()
        {
            lock (_navigraphLock)
            {
                if (_navigraphData == null)
                {
                    InitializeNavigraphServices();
                }
                return _navigraphData;
            }
        }

        /// <summary>
        /// Helper-Methode für synchronen HTTP GET mit HttpClient (ersetzt WebClient.DownloadString)
        /// </summary>
        private static string HttpGetString(string url, string userAgent = "KneeboardServer/1.0")
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", userAgent);

            var response = _httpClient.SendAsync(request).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        private readonly string[] _indexFiles = {
            "kneeboard.html",
            "documents.html",
            "map.html",
            "navlog.html",
            "notepad.html",
            "formulas.html",
        };
        private static readonly IDictionary<string, string> _mimeTypeMappings = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase) {
        #region extension to MIME type list
        {".asf", "video/x-ms-asf"},
        {".asx", "video/x-ms-asf"},
        {".avi", "video/x-msvideo"},
        {".bin", "application/octet-stream"},
        {".cco", "application/x-cocoa"},
        {".crt", "application/x-x509-ca-cert"},
        {".css", "text/css"},
        {".deb", "application/octet-stream"},
        {".der", "application/x-x509-ca-cert"},
        {".dll", "application/octet-stream"},
        {".dmg", "application/octet-stream"},
        {".ear", "application/java-archive"},
        {".eot", "application/octet-stream"},
        {".exe", "application/octet-stream"},
        {".flv", "video/x-flv"},
        {".gif", "image/gif"},
        {".hqx", "application/mac-binhex40"},
        {".htc", "text/x-component"},
        {".htm", "text/html"},
        {".html", "text/html"},
        {".ico", "image/x-icon"},
        {".img", "application/octet-stream"},
        {".iso", "application/octet-stream"},
        {".jar", "application/java-archive"},
        {".jardiff", "application/x-java-archive-diff"},
        {".jng", "image/x-jng"},
        {".jnlp", "application/x-java-jnlp-file"},
        {".jpeg", "image/jpeg"},
        {".jpg", "image/jpeg"},
        {".js", "application/x-javascript"},
        {".mjs", "text/javascript"},
        {".mml", "text/mathml"},
        {".mng", "video/x-mng"},
        {".mov", "video/quicktime"},
        {".mp3", "audio/mpeg"},
        {".mpeg", "video/mpeg"},
        {".mpg", "video/mpeg"},
        {".msi", "application/octet-stream"},
        {".msm", "application/octet-stream"},
        {".msp", "application/octet-stream"},
        {".pdb", "application/x-pilot"},
        {".pdf", "application/pdf"},
        {".pem", "application/x-x509-ca-cert"},
        {".pl", "application/x-perl"},
        {".pm", "application/x-perl"},
        {".png", "image/png"},
        {".prc", "application/x-pilot"},
        {".ra", "audio/x-realaudio"},
        {".rar", "application/x-rar-compressed"},
        {".rpm", "application/x-redhat-package-manager"},
        {".rss", "text/xml"},
        {".run", "application/x-makeself"},
        {".sea", "application/x-sea"},
        {".shtml", "text/html"},
        {".sit", "application/x-stuffit"},
        {".swf", "application/x-shockwave-flash"},
        {".tcl", "application/x-tcl"},
        {".tk", "application/x-tcl"},
        {".txt", "text/plain"},
        {".war", "application/java-archive"},
        {".wbmp", "image/vnd.wap.wbmp"},
        {".wmv", "video/x-ms-wmv"},
        {".xml", "text/xml"},
        {".xpi", "application/x-xpinstall"},
        {".zip", "application/zip"},
        #endregion
    };
        private string _rootDirectory;
        private WebServer _server;
        private int _port;
        public string values = "";
        public long valuesTimestamp = 0;

        /// <summary>
        /// Finds a file using case-insensitive matching on Windows.
        /// Returns the actual file path if found, null otherwise.
        /// </summary>
        private static string FindFileCaseInsensitive(string filePath)
        {
            // First try exact match (fast path)
            if (File.Exists(filePath))
            {
                return filePath;
            }

            // Case-insensitive fallback
            try
            {
                string directory = Path.GetDirectoryName(filePath);
                string fileName = Path.GetFileName(filePath);

                // Check for empty filename
                if (string.IsNullOrEmpty(fileName))
                {
                    KneeboardLogger.ServerDebug("Empty filename, returning null");
                    return null;
                }

                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    KneeboardLogger.ServerDebug($"Directory not found: {directory}");
                    return null;
                }

                // Find file with case-insensitive comparison
                string[] files = Directory.GetFiles(directory);
                foreach (string file in files)
                {
                    if (Path.GetFileName(file).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.ServerError($"FindFile exception: {ex.Message}");
            }

            return null;
        }



        public int Port
        {
            get { return _port; }
            private set { }
        }

        /// <summary>
        /// Construct server with given port.
        /// </summary>
        /// <param name="path">Directory path to serve.</param>
        /// <param name="port">Port of the server.</param>
        public SimpleHTTPServer(string path, int port)
        {
            this.Initialize(path, port);
        }



        /// <summary>
        /// Stop server and dispose all functions.
        /// </summary>
        public void Stop()
        {
            try
            {
                _server?.Dispose();
                DisposeSrtmCache();
            }
            catch (Exception ex)
            {
                KneeboardLogger.ServerError($"Error stopping server: {ex.Message}");
            }
        }

        private WebServer CreateWebServer()
        {
            KneeboardLogger.Server($"Creating WebServer on port {_port} with root: {_rootDirectory}");

            // Listen on all interfaces (requires admin or netsh urlacl)
            var server = new WebServer(o => o
                .WithUrlPrefix($"http://*:{_port}/")
                .WithMode(HttpListenerMode.EmbedIO))
                .WithCors()
                .WithModule(new ActionModule("/", HttpVerbs.Any, ProcessRequestAsync));

            KneeboardLogger.Server($"Server listening on http://*:{_port}/");

            // Note: Static files are handled in ProcessRequestAsync
            // No need for separate StaticFolder module

            return server;
        }

        private async Task StartServerAsync()
        {
            try
            {
                _server = CreateWebServer();
                await _server.RunAsync();
            }
            catch (Exception ex)
            {
                KneeboardLogger.ServerError($"Failed to start HTTP server: {ex.Message}");
                KneeboardLogger.ServerError("Try running as Administrator or use a different port.");
            }
        }

        private async Task ResponseStringAsync(IHttpContext ctx, string text)
        {
            try
            {
                await ctx.SendStringAsync(text ?? string.Empty, "text/plain", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                KneeboardLogger.ServerError($"Error sending response: {ex.Message}");
            }
        }

        private async Task ResponseJsonAsync(IHttpContext ctx, string json)
        {
            try
            {
                await ctx.SendStringAsync(json ?? "{}", "application/json", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                KneeboardLogger.ServerError($"Error sending JSON response: {ex.Message}");
            }
        }

        #region Procedure API Handlers

        /// <summary>
        /// Handle request for procedure details: api/procedure/{airport}/{type}/{name}
        /// Returns detailed waypoints with coordinates, altitudes, and speeds
        /// </summary>
        private async Task HandleProcedureRequest(IHttpContext ctx, string path)
        {
            try
            {
                var parts = path.Split('/');
                if (parts.Length < 3)
                {
                    await ResponseJsonAsync(ctx, "{\"error\":\"Invalid path. Use: api/procedure/{airport}/{SID|STAR}/{name}\"}");
                    return;
                }

                string airport = parts[0].ToUpperInvariant();
                string type = parts[1].ToUpperInvariant();
                string procedureName = parts[2];
                string transition = parts.Length > 3 ? parts[3] : null;

                if (type != "SID" && type != "STAR")
                {
                    await ResponseJsonAsync(ctx, "{\"error\":\"Type must be SID or STAR\"}");
                    return;
                }

                // Try to get procedure from available sources
                var procedureDetail = GetProcedureDetailFromSources(airport, procedureName, type, transition);

                if (procedureDetail != null)
                {
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(procedureDetail);
                    await ResponseJsonAsync(ctx, json);
                }
                else
                {
                    await ResponseJsonAsync(ctx, $"{{\"error\":\"Procedure {procedureName} not found for {airport}\"}}");
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Procedure error: {ex.Message}");
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message.Replace("\"", "\\\"")}\"}}" );
            }
        }

        /// <summary>
        /// Handle request for all procedures at an airport: api/procedures/{airport}
        /// Returns list of SIDs and STARs with basic info
        /// </summary>
        private async Task HandleProceduresListRequest(IHttpContext ctx, string airportIcao)
        {
            try
            {
                string airport = airportIcao.ToUpperInvariant();

                var result = new
                {
                    airport = airport,
                    sids = new List<Navigraph.ProcedureSummary>(),
                    stars = new List<Navigraph.ProcedureSummary>(),
                    source = "none"
                };





                // Try Navigraph if available
                if (result.source == "none")
                {
                    var navigraphData = GetNavigraphData();
                    if (navigraphData != null && navigraphData.IsDataAvailable)
                    {
                        try
                        {
                            var sids = navigraphData.GetSIDs(airport);
                            var stars = navigraphData.GetSTARs(airport);

                            if ((sids != null && sids.Count > 0) || (stars != null && stars.Count > 0))
                            {
                                result = new
                                {
                                    airport = airport,
                                    sids = sids ?? new List<Navigraph.ProcedureSummary>(),
                                    stars = stars ?? new List<Navigraph.ProcedureSummary>(),
                                    source = "Navigraph"
                                };
                            }
                        }
                        catch (Exception ex)
                        {
                            KneeboardLogger.NavigraphError($"Procedure list error: {ex.Message}");
                        }
                    }
                }

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(result);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Procedures list error: {ex.Message}");
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message.Replace("\"", "\\\"")}\"}}" );
            }
        }

        /// <summary>
        /// Get procedure details from available sources (MSFS 2024, MSFS 2020, Navigraph)
        /// </summary>
        private Navigraph.ProcedureDetail GetProcedureDetailFromSources(
            string airport, string procedureName, string type, string transition)
        {
            bool isSid = type == "SID";

            // Try Navigraph
            var navigraphData = GetNavigraphData();
            if (navigraphData != null && navigraphData.IsDataAvailable)
            {
                try
                {
                    var procType = isSid ? Navigraph.ProcedureType.SID : Navigraph.ProcedureType.STAR;
                    var detail = navigraphData.GetProcedureDetail(airport, procedureName, transition, procType);
                    if (detail != null && detail.Waypoints.Count > 0)
                    {
                        return detail;
                    }
                }
                catch (Exception ex)
                {
                    KneeboardLogger.NavigraphError($"Procedure detail error: {ex.Message}");
                }
            }

            return null;
        }

        #endregion

        private async Task HandleNoaaProxy(IHttpContext ctx, string icaoRaw, bool isTaf)
        {
            string icao = (icaoRaw ?? string.Empty).Trim().ToUpperInvariant();
            if (icao.Length != 4 || !icao.All(char.IsLetter))
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                // EmbedIO manages stream
                return;
            }

            string endpoint = isTaf
                ? $"https://aviationweather.gov/api/data/taf?ids={icao}&format=json"
                : $"https://aviationweather.gov/api/data/metar?ids={icao}&format=json";

            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add(HttpRequestHeader.UserAgent, "KneeboardServer/1.0");
                    client.Encoding = Encoding.UTF8;
                    string payload = client.DownloadString(endpoint);
                    byte[] buffer = Encoding.UTF8.GetBytes(payload);
                    ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                    ctx.Response.ContentType = "application/json";
                    // ctx.Response.ContentLength = buffer.Length;
                    await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
            }
            catch (WebException ex)
            {
                var httpResponse = ex.Response as HttpWebResponse;
                ctx.Response.StatusCode = httpResponse != null
                    ? (int)httpResponse.StatusCode
                    : (int)HttpStatusCode.BadGateway;

                string errorPayload = string.Empty;
                if (ex.Response != null)
                {
                    try
                    {
                        using (var reader = new StreamReader(ex.Response.GetResponseStream()))
                        {
                            errorPayload = reader.ReadToEnd();
                        }
                    }
                    catch
                    {
                        errorPayload = string.Empty;
                    }
                }

                if (string.IsNullOrWhiteSpace(errorPayload))
                {
                    errorPayload = "{\"error\":\"Unable to reach NOAA service\"}";
                }

                byte[] buffer = Encoding.UTF8.GetBytes(errorPayload);
                ctx.Response.ContentType = "application/json";
                // ctx.Response.ContentLength = buffer.Length;
                await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            finally
            {
                // EmbedIO manages stream
            }
        }
        // Elevation cache - stores elevation values by rounded coordinates
        private static readonly Dictionary<string, double> _elevationCache = new Dictionary<string, double>();
        private static readonly object _elevationCacheLock = new object();
        private static readonly string _elevationCacheDir = Path.Combine(CACHE_BASE_DIR, "elevation");
        private static DateTime _lastElevationApiCall = DateTime.MinValue;
        private static readonly TimeSpan _elevationApiMinInterval = TimeSpan.FromMilliseconds(500);

        private string GetElevationCacheKey(double lat, double lon)
        {
            // Round to 4 decimal places (~11m precision)
            var roundedLat = Math.Round(lat, 4);
            var roundedLon = Math.Round(lon, 4);
            return $"{roundedLat.ToString(System.Globalization.CultureInfo.InvariantCulture)}_{roundedLon.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }

        private double? GetCachedElevation(double lat, double lon)
        {
            var key = GetElevationCacheKey(lat, lon);

            // Check memory cache first
            lock (_elevationCacheLock)
            {
                if (_elevationCache.TryGetValue(key, out double elev))
                {
                    return elev;
                }
            }

            // Check file cache
            try
            {
                var cachePath = Path.Combine(_elevationCacheDir, $"{key}.txt");
                if (File.Exists(cachePath))
                {
                    var content = File.ReadAllText(cachePath);
                    if (double.TryParse(content, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double elevation))
                    {
                        // Add to memory cache
                        lock (_elevationCacheLock)
                        {
                            _elevationCache[key] = elevation;
                        }
                        return elevation;
                    }
                }
            }
            catch { /* ignore cache read errors */ }

            return null;
        }

        private void CacheElevation(double lat, double lon, double elevation)
        {
            var key = GetElevationCacheKey(lat, lon);

            // Memory cache
            lock (_elevationCacheLock)
            {
                _elevationCache[key] = elevation;
            }

            // File cache (async fire-and-forget)
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(_elevationCacheDir))
                    {
                        Directory.CreateDirectory(_elevationCacheDir);
                    }
                    var cachePath = Path.Combine(_elevationCacheDir, $"{key}.txt");
                    File.WriteAllText(cachePath, elevation.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                catch { /* ignore cache write errors */ }
            });
        }

        // SRTM elevation data support
        private static readonly string _srtmDataDir = Path.Combine(CACHE_BASE_DIR, "srtm");

        // MemoryMappedFile-Cache: Tile einmal öffnen, für alle weiteren Reads im RAM halten
        private static readonly Dictionary<string, (MemoryMappedFile mmf, MemoryMappedViewAccessor accessor, int samples)> _srtmTileCache
            = new Dictionary<string, (MemoryMappedFile, MemoryMappedViewAccessor, int)>();
        private static readonly object _srtmTileCacheLock = new object();
        // Merke Tiles, die nicht existieren (404 bei Download = Ozean)
        private static readonly HashSet<string> _srtmMissingTiles = new HashSet<string>();

        public static int GetSrtmFileCount()
        {
            try
            {
                if (!Directory.Exists(_srtmDataDir))
                    return 0;
                return Directory.GetFiles(_srtmDataDir, "*.hgt").Length;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Gibt den MemoryMappedViewAccessor und samples-Zahl für ein SRTM-Tile zurück.
        /// Cached: erstes Öffnen ~1ms, alle weiteren Zugriffe ~0.001ms.
        /// </summary>
        private static (MemoryMappedViewAccessor accessor, int samples)? GetSrtmTileAccessor(string fileName, string filePath)
        {
            lock (_srtmTileCacheLock)
            {
                if (_srtmTileCache.TryGetValue(fileName, out var cached))
                    return (cached.accessor, cached.samples);

                if (_srtmMissingTiles.Contains(fileName))
                    return null;
            }

            // Außerhalb des Locks: Datei prüfen und öffnen
            if (!File.Exists(filePath))
            {
                lock (_srtmTileCacheLock) { _srtmMissingTiles.Add(fileName); }
                return null;
            }

            var fileInfo = new FileInfo(filePath);
            int samples;
            if (fileInfo.Length == 1201 * 1201 * 2)
                samples = 1201; // SRTM3
            else if (fileInfo.Length == 3601 * 3601 * 2)
                samples = 3601; // SRTM1
            else
                return null;

            try
            {
                var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
                var accessor = mmf.CreateViewAccessor(0, fileInfo.Length, MemoryMappedFileAccess.Read);

                lock (_srtmTileCacheLock)
                {
                    // Double-check nach Lock
                    if (!_srtmTileCache.ContainsKey(fileName))
                    {
                        _srtmTileCache[fileName] = (mmf, accessor, samples);
                    }
                    else
                    {
                        accessor.Dispose();
                        mmf.Dispose();
                        var existing = _srtmTileCache[fileName];
                        return (existing.accessor, existing.samples);
                    }
                }
                return (accessor, samples);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Liest einen Big-Endian Int16-Wert vom MemoryMappedViewAccessor.
        /// </summary>
        private static short ReadBigEndianInt16(MemoryMappedViewAccessor accessor, long offset)
        {
            byte b0 = accessor.ReadByte(offset);
            byte b1 = accessor.ReadByte(offset + 1);
            return (short)((b0 << 8) | b1);
        }

        public static double? GetSrtmElevation(double lat, double lon)
        {
            try
            {
                int latInt = (int)Math.Floor(lat);
                int lonInt = (int)Math.Floor(lon);

                string latPrefix = lat >= 0 ? "N" : "S";
                string lonPrefix = lon >= 0 ? "E" : "W";
                string fileName = $"{latPrefix}{Math.Abs(latInt):D2}{lonPrefix}{Math.Abs(lonInt):D3}.hgt";
                string filePath = Path.Combine(_srtmDataDir, fileName);

                var tile = GetSrtmTileAccessor(fileName, filePath);
                if (tile == null) return null;

                var accessor = tile.Value.accessor;
                int samples = tile.Value.samples;

                // Bilineare Interpolation für glatteres Höhenprofil
                double latFrac = lat - latInt;
                double lonFrac = lon - lonInt;

                double rowExact = (1 - latFrac) * (samples - 1);
                double colExact = lonFrac * (samples - 1);

                int row0 = Math.Min((int)rowExact, samples - 2);
                int col0 = Math.Min((int)colExact, samples - 2);
                int row1 = row0 + 1;
                int col1 = col0 + 1;

                double rowFrac = rowExact - row0;
                double colFrac = colExact - col0;

                // 4 Nachbarpunkte lesen
                short e00 = ReadBigEndianInt16(accessor, ((long)row0 * samples + col0) * 2);
                short e01 = ReadBigEndianInt16(accessor, ((long)row0 * samples + col1) * 2);
                short e10 = ReadBigEndianInt16(accessor, ((long)row1 * samples + col0) * 2);
                short e11 = ReadBigEndianInt16(accessor, ((long)row1 * samples + col1) * 2);

                // Void-Werte prüfen
                if (e00 == -32768 || e01 == -32768 || e10 == -32768 || e11 == -32768)
                {
                    // Fallback: nearest-neighbor wenn ein Nachbar void ist
                    int row = Math.Max(0, Math.Min(samples - 1, (int)Math.Round(rowExact)));
                    int col = Math.Max(0, Math.Min(samples - 1, (int)Math.Round(colExact)));
                    short elev = ReadBigEndianInt16(accessor, ((long)row * samples + col) * 2);
                    return elev == -32768 ? (double?)null : elev;
                }

                // Bilineare Interpolation
                double elevation = e00 * (1 - rowFrac) * (1 - colFrac)
                                 + e01 * (1 - rowFrac) * colFrac
                                 + e10 * rowFrac * (1 - colFrac)
                                 + e11 * rowFrac * colFrac;

                return Math.Round(elevation, 1);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Räumt die MemoryMappedFile-Caches auf (bei Server-Stop aufrufen).
        /// </summary>
        public static void DisposeSrtmCache()
        {
            lock (_srtmTileCacheLock)
            {
                foreach (var tile in _srtmTileCache.Values)
                {
                    try { tile.accessor.Dispose(); } catch { }
                    try { tile.mmf.Dispose(); } catch { }
                }
                _srtmTileCache.Clear();
                _srtmMissingTiles.Clear();
            }
        }

        /// <summary>
        /// SRTM region definitions for download
        /// </summary>
        public static readonly Dictionary<string, (int minLat, int maxLat, int minLon, int maxLon, string description)> SrtmRegions =
            new Dictionary<string, (int, int, int, int, string)>
        {
            { "Europe", (35, 71, -11, 40, "Europe (~2-3 GB)") },
            { "NorthAmerica", (24, 60, -130, -52, "North America (~5 GB)") },
            { "SouthAmerica", (-56, 12, -82, -34, "South America (~4 GB)") },
            { "Africa", (-35, 37, -18, 52, "Africa (~5 GB)") },
            { "Asia", (0, 60, 60, 150, "Asia (~7 GB)") },
            { "Oceania", (-47, 0, 110, 180, "Oceania (~3 GB)") },
            { "Worldwide", (-60, 60, -180, 179, "Worldwide (~25 GB)") }
        };

        private static CancellationTokenSource _srtmDownloadCts;

        // Dedizierter HttpClient für SRTM-Downloads: Connection Pooling + HTTP Keep-Alive
        private static readonly HttpClient _srtmHttpClient = new HttpClient(new HttpClientHandler
        {
            MaxConnectionsPerServer = 30,
            AutomaticDecompression = System.Net.DecompressionMethods.None // Wir decomprimieren selbst (GZip-Dateien, nicht HTTP-Kompression)
        })
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        public static void CancelSrtmDownload()
        {
            _srtmDownloadCts?.Cancel();
        }

        public static async Task DownloadSrtmRegionAsync(string regionName, IProgress<string> progress)
        {
            if (!SrtmRegions.TryGetValue(regionName, out var region))
            {
                throw new ArgumentException($"Unknown region: {regionName}");
            }

            _srtmDownloadCts = new CancellationTokenSource();
            await DownloadSrtmAreaAsync(region.minLat, region.maxLat, region.minLon, region.maxLon, progress, _srtmDownloadCts.Token);
        }

        public static async Task DownloadSrtmAreaAsync(int minLat, int maxLat, int minLon, int maxLon, IProgress<string> progress, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(_srtmDataDir))
                Directory.CreateDirectory(_srtmDataDir);

            // Build list of all tiles to download
            var tiles = new List<(int lat, int lon, string fileName, string filePath)>();
            for (int lat = minLat; lat <= maxLat; lat++)
            {
                for (int lon = minLon; lon <= maxLon; lon++)
                {
                    string latPrefix = lat >= 0 ? "N" : "S";
                    string lonPrefix = lon >= 0 ? "E" : "W";
                    string fileName = $"{latPrefix}{Math.Abs(lat):D2}{lonPrefix}{Math.Abs(lon):D3}.hgt";
                    string filePath = Path.Combine(_srtmDataDir, fileName);
                    tiles.Add((lat, lon, fileName, filePath));
                }
            }

            int totalTiles = tiles.Count;
            int processedTiles = 0;
            int downloadedTiles = 0;
            int skippedTiles = 0;
            int errorTiles = 0;
            long totalBytesDownloaded = 0;
            var startTime = DateTime.Now;

            // Semaphore für parallele Downloads (20 gleichzeitig, Connection Pooling via HttpClient)
            var semaphore = new SemaphoreSlim(20);

            var tasks = tiles.Select(async tile =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    int current = Interlocked.Increment(ref processedTiles);

                    // Skip if already exists
                    if (File.Exists(tile.filePath))
                    {
                        Interlocked.Increment(ref skippedTiles);
                        ReportSrtmProgress(progress, current, totalTiles, downloadedTiles, skippedTiles, totalBytesDownloaded, startTime);
                        return;
                    }

                    try
                    {
                        string latPrefix = tile.lat >= 0 ? "N" : "S";
                        string url = $"https://elevation-tiles-prod.s3.amazonaws.com/skadi/{latPrefix}{Math.Abs(tile.lat):D2}/{tile.fileName}.gz";

                        using (var response = await _srtmHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                        {
                            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                            {
                                // 404 = tile doesn't exist (water/ocean)
                                Interlocked.Increment(ref skippedTiles);
                                return;
                            }
                            response.EnsureSuccessStatusCode();

                            // Streaming: direkt vom HTTP-Stream dekomprimieren → Datei schreiben (kein temp-File)
                            using (var httpStream = await response.Content.ReadAsStreamAsync())
                            using (var gzipStream = new System.IO.Compression.GZipStream(httpStream, System.IO.Compression.CompressionMode.Decompress))
                            using (var outputStream = new FileStream(tile.filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                            {
                                await gzipStream.CopyToAsync(outputStream);
                            }

                            long fileSize = new FileInfo(tile.filePath).Length;
                            Interlocked.Add(ref totalBytesDownloaded, fileSize);
                            Interlocked.Increment(ref downloadedTiles);
                        }
                    }
                    catch (HttpRequestException ex) when (ex.Message.Contains("404"))
                    {
                        Interlocked.Increment(ref skippedTiles);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        KneeboardLogger.ElevationError($"Error downloading {tile.fileName}: {ex.Message}");
                        Interlocked.Increment(ref errorTiles);
                        // Unvollständige Datei löschen
                        try { if (File.Exists(tile.filePath)) File.Delete(tile.filePath); } catch { }
                    }

                    ReportSrtmProgress(progress, current, totalTiles, downloadedTiles, skippedTiles, totalBytesDownloaded, startTime);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                progress?.Report($"Cancelled: {downloadedTiles} new, {skippedTiles} exist");
                throw;
            }

            double totalMB = totalBytesDownloaded / (1024.0 * 1024.0);
            progress?.Report($"Done: {downloadedTiles} new, {skippedTiles} exist, {errorTiles} errors ({totalMB:F1} MB)");
        }

        private static void ReportSrtmProgress(IProgress<string> progress, int current, int total,
            int downloaded, int skipped, long totalBytes, DateTime startTime)
        {
            double elapsedSec = (DateTime.Now - startTime).TotalSeconds;
            double mbDownloaded = totalBytes / (1024.0 * 1024.0);
            string speedInfo = elapsedSec > 1 ? $" - {mbDownloaded / elapsedSec:F1} MB/s" : "";
            progress?.Report($"{current}/{total} ({downloaded} new, {skipped} exist, {mbDownloaded:F0} MB{speedInfo})");
        }

        private async Task HandleElevationProxy(IHttpContext ctx)
        {
            var request = ctx.Request;

            if (request.HttpMethod != "POST")
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                // EmbedIO manages stream
                return;
            }

            string requestBody;
            try
            {
                using (var reader = ctx.OpenRequestText())
                {
                    requestBody = await reader.ReadToEndAsync();
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.ElevationError($"Error reading elevation request body: {ex.Message}");
                ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            try
            {
                // Parse the incoming request to extract coordinates
                // Expected format: {"locations": [{"latitude": XX, "longitude": XX}]}
                dynamic requestData = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(requestBody);
                var locations = requestData?.locations;

                if (locations == null || locations.Count == 0)
                {
                    KneeboardLogger.ElevationError($"No locations in request. Body length: {requestBody.Length}");
                    ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    var errorBuffer = Encoding.UTF8.GetBytes("{\"error\":\"No locations provided\"}");
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.OutputStream.WriteAsync(errorBuffer, 0, errorBuffer.Length);
                    return;
                }

                var allResults = new List<object>();
                var uncachedLocations = new List<(int index, double lat, double lon)>();
                bool useSrtm = Properties.Settings.Default.useSrtmElevation;
                int srtmHits = 0;

                // First pass: check SRTM (if enabled), then cache for all locations
                int idx = 0;
                foreach (var loc in locations)
                {
                    double lat = (double)loc.latitude;
                    double lon = (double)loc.longitude;
                    double? elevation = null;

                    // Try SRTM first (if enabled and available)
                    if (useSrtm)
                    {
                        elevation = GetSrtmElevation(lat, lon);
                        if (elevation.HasValue)
                        {
                            srtmHits++;
                            // Also cache this for faster access next time
                            CacheElevation(lat, lon, elevation.Value);
                        }
                    }

                    // Try cache if SRTM didn't have it
                    if (!elevation.HasValue)
                    {
                        elevation = GetCachedElevation(lat, lon);
                    }

                    if (elevation.HasValue)
                    {
                        allResults.Add(new { latitude = lat, longitude = lon, elevation = elevation.Value });
                    }
                    else
                    {
                        uncachedLocations.Add((idx, lat, lon));
                        allResults.Add(null); // Placeholder
                    }
                    idx++;
                }

                int cacheHits = allResults.Count(r => r != null);
                int cacheMisses = uncachedLocations.Count;

                if (cacheMisses == 0)
                {
                    // All from SRTM/cache - no API needed!
                    KneeboardLogger.ElevationDebug($"All {cacheHits} locations local (SRTM: {srtmHits}, cache: {cacheHits - srtmHits})");
                    string payload = Newtonsoft.Json.JsonConvert.SerializeObject(new { results = allResults });
                    var buffer = Encoding.UTF8.GetBytes(payload);
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.Headers.Add("X-Cache", "HIT");
                    await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    return;
                }

                KneeboardLogger.ElevationDebug($"SRTM: {srtmHits}, Cache: {cacheHits - srtmHits}, API needed: {cacheMisses}");

                // Rate limiting: wait if last API call was too recent
                var timeSinceLastCall = DateTime.Now - _lastElevationApiCall;
                if (timeSinceLastCall < _elevationApiMinInterval)
                {
                    var waitTime = _elevationApiMinInterval - timeSinceLastCall;
                    await System.Threading.Tasks.Task.Delay(waitTime);
                }

                // Open-Meteo API limit: max 100 locations per request
                const int MAX_LOCATIONS_PER_REQUEST = 100;
                int totalBatches = (int)Math.Ceiling((double)uncachedLocations.Count / MAX_LOCATIONS_PER_REQUEST);

                for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
                {
                    int startIndex = batchIndex * MAX_LOCATIONS_PER_REQUEST;
                    int batchSize = Math.Min(MAX_LOCATIONS_PER_REQUEST, uncachedLocations.Count - startIndex);

                    // Build comma-separated lat/lng lists for this batch
                    var latitudes = new List<string>();
                    var longitudes = new List<string>();
                    var batchItems = new List<(int index, double lat, double lon)>();

                    for (int i = startIndex; i < startIndex + batchSize; i++)
                    {
                        var item = uncachedLocations[i];
                        batchItems.Add(item);
                        latitudes.Add(item.lat.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        longitudes.Add(item.lon.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }

                    // Use Open-Meteo API
                    string apiUrl = $"https://api.open-meteo.com/v1/elevation?latitude={string.Join(",", latitudes)}&longitude={string.Join(",", longitudes)}";
                    _lastElevationApiCall = DateTime.Now;

                    var outboundRequest = (HttpWebRequest)WebRequest.Create(apiUrl);
                    outboundRequest.Method = "GET";
                    outboundRequest.Accept = "application/json";
                    outboundRequest.Timeout = 10000;
                    outboundRequest.ReadWriteTimeout = 10000;

                    using (var upstreamResponse = (HttpWebResponse)outboundRequest.GetResponse())
                    {
                        string responseBody;
                        using (var upstreamStream = upstreamResponse.GetResponseStream())
                        using (var reader = new StreamReader(upstreamStream))
                        {
                            responseBody = reader.ReadToEnd();
                        }

                        // Parse Open-Meteo response: {"elevation": [123.5, 456.7]}
                        dynamic meteoData = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(responseBody);
                        var elevations = meteoData.elevation;

                        // Add results from this batch
                        for (int i = 0; i < batchSize && i < elevations.Count; i++)
                        {
                            var item = batchItems[i];
                            double elevation = (double)elevations[i];

                            // Cache the result
                            CacheElevation(item.lat, item.lon, elevation);

                            // Update the result at the original index
                            allResults[item.index] = new
                            {
                                latitude = item.lat,
                                longitude = item.lon,
                                elevation = elevation
                            };
                        }
                    }

                    // Small delay between batches to avoid rate limiting
                    if (batchIndex < totalBatches - 1)
                    {
                        await System.Threading.Tasks.Task.Delay(100);
                    }
                }

                KneeboardLogger.ElevationDebug($"Returning {allResults.Count} elevation results (SRTM: {srtmHits}, cache: {cacheHits - srtmHits}, API: {cacheMisses})");

                // Return combined results
                string resultPayload = Newtonsoft.Json.JsonConvert.SerializeObject(new { results = allResults });
                var resultBuffer = Encoding.UTF8.GetBytes(resultPayload);

                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                ctx.Response.Headers.Add("X-Cache", cacheHits > 0 ? "PARTIAL" : "MISS");
                await ctx.Response.OutputStream.WriteAsync(resultBuffer, 0, resultBuffer.Length);
            }
            catch (WebException ex)
            {
                var httpResponse = ex.Response as HttpWebResponse;
                ctx.Response.StatusCode = httpResponse != null
                    ? (int)httpResponse.StatusCode
                    : (int)HttpStatusCode.BadGateway;

                KneeboardLogger.ElevationError($"Elevation Proxy Error: {ex.Message}");
                if (httpResponse != null)
                {
                    KneeboardLogger.ElevationError($"Upstream Status: {httpResponse.StatusCode}");
                }

                string payload = "{\"error\":\"Unable to reach elevation service\"}";
                var buffer = Encoding.UTF8.GetBytes(payload);
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                KneeboardLogger.ElevationError($"Elevation Proxy Error: {ex.Message}");
                ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                string payload = "{\"error\":\"Internal server error\"}";
                var buffer = Encoding.UTF8.GetBytes(payload);
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            finally
            {
                // EmbedIO manages stream
            }
        }

        private async Task HandleDfsProxy(IHttpContext ctx)
        {
            var request = ctx.Request;
            var path = request.Url.AbsolutePath;

            // Extract the tile path (z/x/y.png) from the URL
            var idx = path.IndexOf("/api/dfs/tiles/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                // EmbedIO manages stream
                return;
            }

            var tilePart = path.Substring(idx + "/api/dfs/tiles/".Length);
            var targetUrl = $"https://secais.dfs.de/static-maps/icao500/tiles/{tilePart}";

            try
            {
                var outboundRequest = (HttpWebRequest)WebRequest.Create(targetUrl);
                outboundRequest.Method = "GET";
                outboundRequest.Accept = "image/png";
                outboundRequest.Timeout = 10000;
                outboundRequest.ReadWriteTimeout = 10000;

                using (var upstreamResponse = (HttpWebResponse)outboundRequest.GetResponse())
                {
                    ctx.Response.StatusCode = (int)upstreamResponse.StatusCode;
                    ctx.Response.ContentType = upstreamResponse.ContentType ?? "image/png";

                    using (var upstreamStream = upstreamResponse.GetResponseStream())
                    {
                        await upstreamStream.CopyToAsync(ctx.Response.OutputStream);
                    }
                }
            }
            catch (WebException ex)
            {
                var httpResponse = ex.Response as HttpWebResponse;
                ctx.Response.StatusCode = httpResponse != null
                    ? (int)httpResponse.StatusCode
                    : (int)HttpStatusCode.BadGateway;

                KneeboardLogger.ProxyError($"DFS Proxy Error: {ex.Message}");
                KneeboardLogger.ProxyDebug($"Requested URL: {targetUrl}");
                if (httpResponse != null)
                {
                    KneeboardLogger.ProxyError($"Upstream Status: {httpResponse.StatusCode}");
                }

                // Return empty PNG for failed tile requests
                ctx.Response.ContentType = "image/png";
            }
            finally
            {
                // EmbedIO manages stream
            }
        }

        private async Task HandleOfmProxy(IHttpContext ctx)
        {
            var request = ctx.Request;
            var path = request.Url.AbsolutePath;

            // Extract the tile path (z/x/y.png) from the URL
            var idx = path.IndexOf("/api/ofm/tiles/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                // EmbedIO manages stream
                return;
            }

            var tilePart = path.Substring(idx + "/api/ofm/tiles/".Length);
            var targetUrl = $"https://nwy-tiles-api.prod.newaydata.com/tiles/{tilePart}?path=latest/aero/latest";

            try
            {
                var outboundRequest = (HttpWebRequest)WebRequest.Create(targetUrl);
                outboundRequest.Method = "GET";
                outboundRequest.Accept = "image/png";
                outboundRequest.Timeout = 10000;
                outboundRequest.ReadWriteTimeout = 10000;
                // Add Referer header as some tile servers require it
                outboundRequest.Referer = "https://www.openflightmaps.org/";

                using (var upstreamResponse = (HttpWebResponse)outboundRequest.GetResponse())
                {
                    ctx.Response.StatusCode = (int)upstreamResponse.StatusCode;
                    ctx.Response.ContentType = upstreamResponse.ContentType ?? "image/png";

                    using (var upstreamStream = upstreamResponse.GetResponseStream())
                    {
                        await upstreamStream.CopyToAsync(ctx.Response.OutputStream);
                    }
                }
            }
            catch (WebException ex)
            {
                var httpResponse = ex.Response as HttpWebResponse;
                ctx.Response.StatusCode = httpResponse != null
                    ? (int)httpResponse.StatusCode
                    : (int)HttpStatusCode.BadGateway;

                KneeboardLogger.ProxyError($"OFM Proxy Error: {ex.Message}");
                KneeboardLogger.ProxyDebug($"Requested URL: {targetUrl}");

                // Return empty PNG for failed tile requests
                ctx.Response.ContentType = "image/png";
            }
            finally
            {
                // EmbedIO manages stream
            }
        }

        private async Task HandleVatsimBoundariesProxy(IHttpContext ctx)
        {
            try
            {
                // Check memory cache first
                string cachedData = null;
                lock (_boundariesCacheLock)
                {
                    if (_cachedVatsimBoundaries != null && (DateTime.Now - _vatsimBoundariesCacheTime) < BOUNDARIES_CACHE_TTL)
                    {
                        cachedData = _cachedVatsimBoundaries;
                    }
                }

                if (cachedData != null)
                {
                    ctx.Response.Headers.Add("X-Cache", "HIT");
                    await ResponseJsonAsync(ctx, cachedData);
                    return;
                }

                // Check permanent data directory first (preferred)
                string dataFilePath = Path.Combine(BOUNDARIES_DATA_DIR, "vatsim_boundaries.json");
                if (File.Exists(dataFilePath))
                {
                    var fileInfo = new FileInfo(dataFilePath);
                    if ((DateTime.Now - fileInfo.LastWriteTime) < BOUNDARIES_CACHE_TTL)
                    {
                        string data = File.ReadAllText(dataFilePath);
                        lock (_boundariesCacheLock)
                        {
                            _cachedVatsimBoundaries = data;
                            _vatsimBoundariesCacheTime = fileInfo.LastWriteTime;
                        }
                        ctx.Response.Headers.Add("X-Cache", "DATA");
                        KneeboardLogger.Boundaries("VATSIM loaded from data directory");
                        await ResponseJsonAsync(ctx, data);
                        return;
                    }
                }

                // Fallback to legacy disk cache
                string diskCachePath = Path.Combine(BOUNDARIES_CACHE_DIR, "vatsim_boundaries.json");
                if (File.Exists(diskCachePath))
                {
                    var fileInfo = new FileInfo(diskCachePath);
                    if ((DateTime.Now - fileInfo.LastWriteTime) < BOUNDARIES_CACHE_TTL)
                    {
                        string diskData = File.ReadAllText(diskCachePath);
                        lock (_boundariesCacheLock)
                        {
                            _cachedVatsimBoundaries = diskData;
                            _vatsimBoundariesCacheTime = fileInfo.LastWriteTime;
                        }
                        ctx.Response.Headers.Add("X-Cache", "DISK");
                        KneeboardLogger.Boundaries("VATSIM loaded from disk cache");
                        await ResponseJsonAsync(ctx, diskData);
                        return;
                    }
                }

                // Fetch from GitHub
                using (var client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers.Add(HttpRequestHeader.UserAgent, "KneeboardServer/1.0");
                    string boundaries = client.DownloadString("https://raw.githubusercontent.com/vatsimnetwork/vatspy-data-project/master/Boundaries.geojson");

                    lock (_boundariesCacheLock)
                    {
                        _cachedVatsimBoundaries = boundaries;
                        _vatsimBoundariesCacheTime = DateTime.Now;
                    }

                    // Save to disk cache
                    try
                    {
                        Directory.CreateDirectory(BOUNDARIES_CACHE_DIR);
                        File.WriteAllText(diskCachePath, boundaries);
                        KneeboardLogger.Boundaries("VATSIM saved to disk cache");
                    }
                    catch (Exception cacheEx)
                    {
                        KneeboardLogger.BoundariesError($"VATSIM failed to save disk cache: {cacheEx.Message}");
                    }

                    ctx.Response.Headers.Add("X-Cache", "MISS");
                    await ResponseJsonAsync(ctx, boundaries);
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"VATSIM Boundaries fetch error: {ex.Message}");

                // Try to return cached data even if expired
                string staleData = null;
                lock (_boundariesCacheLock)
                {
                    if (_cachedVatsimBoundaries != null)
                    {
                        staleData = _cachedVatsimBoundaries;
                    }
                }

                if (staleData != null)
                {
                    ctx.Response.Headers.Add("X-Cache", "STALE");
                    await ResponseJsonAsync(ctx, staleData);
                    return;
                }

                ctx.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                await ResponseJsonAsync(ctx, "{\"error\":\"Unable to fetch VATSIM boundaries\"}");
            }
        }

        private async Task HandleVatsimTraconBoundariesProxy(IHttpContext ctx)
        {
            try
            {
                // Check memory cache first
                string cachedData = null;
                lock (_boundariesCacheLock)
                {
                    if (_cachedVatsimTraconBoundaries != null && (DateTime.Now - _vatsimTraconBoundariesCacheTime) < BOUNDARIES_CACHE_TTL)
                    {
                        cachedData = _cachedVatsimTraconBoundaries;
                    }
                }

                if (cachedData != null)
                {
                    ctx.Response.Headers.Add("X-Cache", "HIT");
                    await ResponseJsonAsync(ctx, cachedData);
                    return;
                }

                // Check permanent data directory first (preferred)
                string dataFilePath = Path.Combine(BOUNDARIES_DATA_DIR, "vatsim_tracon_boundaries.json");
                if (File.Exists(dataFilePath))
                {
                    var fileInfo = new FileInfo(dataFilePath);
                    if ((DateTime.Now - fileInfo.LastWriteTime) < BOUNDARIES_CACHE_TTL)
                    {
                        string data = File.ReadAllText(dataFilePath);
                        lock (_boundariesCacheLock)
                        {
                            _cachedVatsimTraconBoundaries = data;
                            _vatsimTraconBoundariesCacheTime = fileInfo.LastWriteTime;
                        }
                        ctx.Response.Headers.Add("X-Cache", "DATA");
                        KneeboardLogger.Boundaries("VATSIM TRACON loaded from data directory");
                        await ResponseJsonAsync(ctx, data);
                        return;
                    }
                }

                // Fallback to legacy disk cache
                string diskCachePath = Path.Combine(BOUNDARIES_CACHE_DIR, "vatsim_tracon_boundaries.json");
                if (File.Exists(diskCachePath))
                {
                    var fileInfo = new FileInfo(diskCachePath);
                    if ((DateTime.Now - fileInfo.LastWriteTime) < BOUNDARIES_CACHE_TTL)
                    {
                        string diskData = File.ReadAllText(diskCachePath);
                        lock (_boundariesCacheLock)
                        {
                            _cachedVatsimTraconBoundaries = diskData;
                            _vatsimTraconBoundariesCacheTime = fileInfo.LastWriteTime;
                        }
                        ctx.Response.Headers.Add("X-Cache", "DISK");
                        KneeboardLogger.Boundaries("VATSIM TRACON loaded from disk cache");
                        await ResponseJsonAsync(ctx, diskData);
                        return;
                    }
                }

                // Fetch from SimAware TRACON project on GitHub (use releases, not raw/main)
                using (var client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers.Add(HttpRequestHeader.UserAgent, "KneeboardServer/1.0");

                    // Get latest release URL from GitHub API
                    string traconUrl = GetLatestTraconReleaseUrl();
                    if (string.IsNullOrEmpty(traconUrl))
                    {
                        throw new Exception("Could not find TRACON boundaries release");
                    }

                    string boundaries = client.DownloadString(traconUrl);

                    lock (_boundariesCacheLock)
                    {
                        _cachedVatsimTraconBoundaries = boundaries;
                        _vatsimTraconBoundariesCacheTime = DateTime.Now;
                    }

                    // Save to disk cache
                    try
                    {
                        Directory.CreateDirectory(BOUNDARIES_CACHE_DIR);
                        File.WriteAllText(diskCachePath, boundaries);
                        KneeboardLogger.Boundaries("VATSIM TRACON saved to disk cache");
                    }
                    catch (Exception cacheEx)
                    {
                        KneeboardLogger.BoundariesError($"VATSIM TRACON failed to save disk cache: {cacheEx.Message}");
                    }

                    ctx.Response.Headers.Add("X-Cache", "MISS");
                    await ResponseJsonAsync(ctx, boundaries);
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"VATSIM TRACON Boundaries fetch error: {ex.Message}");

                // Try to return cached data even if expired
                string staleData = null;
                lock (_boundariesCacheLock)
                {
                    if (_cachedVatsimTraconBoundaries != null)
                    {
                        staleData = _cachedVatsimTraconBoundaries;
                    }
                }

                if (staleData != null)
                {
                    ctx.Response.Headers.Add("X-Cache", "STALE");
                    await ResponseJsonAsync(ctx, staleData);
                    return;
                }

                ctx.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                await ResponseJsonAsync(ctx, "{\"error\":\"Unable to fetch VATSIM TRACON boundaries\"}");
            }
        }

        /// <summary>
        /// Gets the latest TRACON boundaries release URL from GitHub API
        /// Falls back to known working version if API fails
        /// </summary>
        private string GetLatestTraconReleaseUrl()
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add(HttpRequestHeader.UserAgent, "KneeboardServer/1.0");
                    client.Headers.Add(HttpRequestHeader.Accept, "application/vnd.github.v3+json");
                    string json = client.DownloadString("https://api.github.com/repos/vatsimnetwork/simaware-tracon-project/releases/latest");

                    // Parse JSON to find TRACONBoundaries.geojson asset
                    dynamic release = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);
                    if (release?.assets != null)
                    {
                        foreach (var asset in release.assets)
                        {
                            string name = (string)asset.name;
                            if (name != null && name.EndsWith(".geojson", StringComparison.OrdinalIgnoreCase))
                            {
                                string url = (string)asset.browser_download_url;
                                KneeboardLogger.BoundariesDebug($"TRACON boundaries URL: {url}");
                                return url;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"Error getting TRACON release URL: {ex.Message}");
            }

            // Fallback to known working version
            KneeboardLogger.Warn("Boundaries", "TRACON boundaries: using fallback URL v1.2.1");
            return "https://github.com/vatsimnetwork/simaware-tracon-project/releases/download/v1.2.1/TRACONBoundaries.geojson";
        }

        /// <summary>
        /// Starts background check/update of all boundary files (VATSIM, IVAO, TRACON)
        /// Called on server startup to pre-load boundaries for faster initial map display
        /// </summary>
        private void StartBoundariesUpdateCheck()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // Ensure directories exist
                    Directory.CreateDirectory(BOUNDARIES_CACHE_DIR);
                    Directory.CreateDirectory(BOUNDARIES_DATA_DIR);

                    KneeboardLogger.Boundaries("Starting background update check...");

                    // Check/update VATSIM FIR boundaries
                    UpdateBoundaryFile("vatsim_boundaries.json", () =>
                    {
                        using (var client = new WebClient())
                        {
                            client.Encoding = Encoding.UTF8;
                            client.Headers.Add(HttpRequestHeader.UserAgent, "KneeboardServer/1.0");
                            return client.DownloadString("https://raw.githubusercontent.com/vatsimnetwork/vatspy-data-project/master/Boundaries.geojson");
                        }
                    });

                    // Check/update VATSIM TRACON boundaries
                    UpdateBoundaryFile("vatsim_tracon_boundaries.json", () =>
                    {
                        using (var client = new WebClient())
                        {
                            client.Encoding = Encoding.UTF8;
                            client.Headers.Add(HttpRequestHeader.UserAgent, "KneeboardServer/1.0");
                            string url = GetLatestTraconReleaseUrl();
                            return client.DownloadString(url);
                        }
                    });

                    // Check/update IVAO boundaries (from Little Navmap)
                    UpdateBoundaryFile("ivao_boundaries_geojson.json", () =>
                    {
                        string latestUrl = GetLatestIvaoFileUrl();
                        if (string.IsNullOrEmpty(latestUrl))
                        {
                            throw new Exception("Could not find IVAO boundaries file");
                        }
                        using (var client = new WebClient())
                        {
                            client.Headers.Add(HttpRequestHeader.UserAgent, "KneeboardServer/1.0");
                            byte[] zipData = client.DownloadData(latestUrl);
                            string rawJson = ExtractIvaoJsonFromZip(zipData);
                            return ConvertIvaoToGeoJson(rawJson);
                        }
                    });

                    KneeboardLogger.Boundaries("Background update check completed");
                }
                catch (Exception ex)
                {
                    KneeboardLogger.BoundariesError($"Background update error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Updates a boundary file if it doesn't exist or is older than 7 days
        /// </summary>
        private void UpdateBoundaryFile(string fileName, Func<string> fetchData)
        {
            string filePath = Path.Combine(BOUNDARIES_DATA_DIR, fileName);
            string cachePath = Path.Combine(BOUNDARIES_CACHE_DIR, fileName);
            bool needsUpdate = false;

            // Check if file exists and age
            if (!File.Exists(filePath))
            {
                // Also check legacy cache location
                if (File.Exists(cachePath))
                {
                    // Migrate from cache to data directory
                    try
                    {
                        File.Copy(cachePath, filePath, true);
                        KneeboardLogger.Boundaries($"Migrated {fileName} from cache to data directory");
                    }
                    catch { }
                }

                if (!File.Exists(filePath))
                {
                    needsUpdate = true;
                    KneeboardLogger.Warn("Boundaries", $"{fileName}: file missing, downloading...");
                }
            }

            if (!needsUpdate && File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                var age = DateTime.Now - fileInfo.LastWriteTime;
                if (age > BOUNDARIES_CACHE_TTL)
                {
                    needsUpdate = true;
                    KneeboardLogger.Boundaries($"{fileName}: file is {age.TotalDays:F1} days old, updating...");
                }
                else
                {
                    KneeboardLogger.BoundariesDebug($"{fileName}: up to date ({age.TotalDays:F1} days old)");

                    // Pre-load into memory cache
                    try
                    {
                        string data = File.ReadAllText(filePath);
                        PreloadBoundaryToCache(fileName, data, fileInfo.LastWriteTime);
                    }
                    catch { }
                }
            }

            if (needsUpdate)
            {
                try
                {
                    string data = fetchData();
                    if (!string.IsNullOrEmpty(data))
                    {
                        File.WriteAllText(filePath, data);
                        KneeboardLogger.Boundaries($"{fileName}: downloaded and saved ({data.Length / 1024}KB)");

                        // Also save to cache dir for compatibility
                        try { File.WriteAllText(cachePath, data); } catch { }

                        // Pre-load into memory cache
                        PreloadBoundaryToCache(fileName, data, DateTime.Now);
                    }
                }
                catch (Exception ex)
                {
                    KneeboardLogger.BoundariesError($"{fileName}: download failed - {ex.Message}");

                    // If download fails but old file exists, use it anyway
                    if (File.Exists(filePath))
                    {
                        KneeboardLogger.Warn("Boundaries", $"{fileName}: using existing file despite age");
                    }
                }
            }
        }

        /// <summary>
        /// Pre-loads boundary data into the in-memory cache
        /// </summary>
        private void PreloadBoundaryToCache(string fileName, string data, DateTime timestamp)
        {
            lock (_boundariesCacheLock)
            {
                if (fileName.Contains("vatsim_boundaries") && !fileName.Contains("tracon"))
                {
                    _cachedVatsimBoundaries = data;
                    _vatsimBoundariesCacheTime = timestamp;
                }
                else if (fileName.Contains("tracon"))
                {
                    _cachedVatsimTraconBoundaries = data;
                    _vatsimTraconBoundariesCacheTime = timestamp;
                }
                else if (fileName.Contains("ivao"))
                {
                    _cachedIvaoBoundaries = data;
                    _ivaoBoundariesCacheTime = timestamp;
                }
            }
        }

        private async Task HandleVatspyFirNamesProxy(IHttpContext ctx)
        {
            try
            {
                // Check memory cache first
                string cachedData = null;
                lock (_boundariesCacheLock)
                {
                    if (_cachedVatspyFirNames != null && (DateTime.Now - _vatspyFirNamesCacheTime) < BOUNDARIES_CACHE_TTL)
                    {
                        cachedData = _cachedVatspyFirNames;
                    }
                }

                if (cachedData != null)
                {
                    ctx.Response.Headers.Add("X-Cache", "HIT");
                    await ResponseJsonAsync(ctx, cachedData);
                    return;
                }

                // Check disk cache
                string diskCachePath = Path.Combine(BOUNDARIES_CACHE_DIR, "vatspy_firnames.json");
                if (File.Exists(diskCachePath))
                {
                    var fileInfo = new FileInfo(diskCachePath);
                    if ((DateTime.Now - fileInfo.LastWriteTime) < BOUNDARIES_CACHE_TTL)
                    {
                        string diskData = File.ReadAllText(diskCachePath);
                        lock (_boundariesCacheLock)
                        {
                            _cachedVatspyFirNames = diskData;
                            _vatspyFirNamesCacheTime = fileInfo.LastWriteTime;
                        }
                        ctx.Response.Headers.Add("X-Cache", "DISK");
                        KneeboardLogger.Boundaries("VATSpy FIR names loaded from disk cache");
                        await ResponseJsonAsync(ctx, diskData);
                        return;
                    }
                }

                // Fetch VATSpy.dat from GitHub and parse FIR names
                using (var client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers.Add(HttpRequestHeader.UserAgent, "KneeboardServer/1.0");
                    string vatspyData = client.DownloadString("https://raw.githubusercontent.com/vatsimnetwork/vatspy-data-project/master/VATSpy.dat");

                    // Parse both [FIRs] and [Airports] sections to extract prefix -> name mappings
                    var firNames = new Dictionary<string, string>();
                    string currentSection = "";

                    foreach (string line in vatspyData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string trimmedLine = line.Trim();

                        if (trimmedLine.StartsWith("["))
                        {
                            currentSection = trimmedLine.ToUpperInvariant();
                            continue;
                        }

                        if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith(";"))
                            continue;

                        var parts = trimmedLine.Split('|');

                        if (currentSection == "[FIRS]" && parts.Length >= 2)
                        {
                            // FIR format: ICAO|Name|Prefix|FIR Boundary ID
                            // Example: EDGG|Langen Radar|EDGG|EDGG
                            string icao = parts[0].Trim();
                            string name = parts[1].Trim();
                            if (!string.IsNullOrEmpty(icao) && !string.IsNullOrEmpty(name))
                            {
                                firNames[icao] = name;
                            }
                        }
                        else if (currentSection == "[AIRPORTS]" && parts.Length >= 5)
                        {
                            // Airport format: ICAO|Name|Lat|Lon|Prefix|FIR|...
                            // Example: KOAK|Oakland Intl CA|37.721292|-122.220717|OAK|KZOA|0
                            // We want to map Prefix (OAK) -> Name (Oakland Intl CA)
                            string name = parts[1].Trim();
                            string prefix = parts[4].Trim();
                            if (!string.IsNullOrEmpty(prefix) && !string.IsNullOrEmpty(name) && !firNames.ContainsKey(prefix))
                            {
                                firNames[prefix] = name;
                            }
                        }
                    }

                    // Convert to JSON
                    var jsonBuilder = new StringBuilder("{");
                    bool first = true;
                    foreach (var kvp in firNames)
                    {
                        if (!first) jsonBuilder.Append(",");
                        // Escape quotes in name
                        string escapedName = kvp.Value.Replace("\\", "\\\\").Replace("\"", "\\\"");
                        jsonBuilder.AppendFormat("\"{0}\":\"{1}\"", kvp.Key, escapedName);
                        first = false;
                    }
                    jsonBuilder.Append("}");

                    string jsonResult = jsonBuilder.ToString();

                    lock (_boundariesCacheLock)
                    {
                        _cachedVatspyFirNames = jsonResult;
                        _vatspyFirNamesCacheTime = DateTime.Now;
                    }

                    // Save to disk cache
                    try
                    {
                        Directory.CreateDirectory(BOUNDARIES_CACHE_DIR);
                        File.WriteAllText(diskCachePath, jsonResult);
                        KneeboardLogger.Boundaries("VATSpy FIR names saved to disk cache");
                    }
                    catch (Exception cacheEx)
                    {
                        KneeboardLogger.BoundariesError($"VATSpy FIR names failed to save disk cache: {cacheEx.Message}");
                    }

                    KneeboardLogger.Boundaries($"VATSpy FIR names loaded: {firNames.Count} entries");
                    ctx.Response.Headers.Add("X-Cache", "MISS");
                    await ResponseJsonAsync(ctx, jsonResult);
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"VATSpy FIR names fetch error: {ex.Message}");

                // Try to return cached data even if expired
                string staleData = null;
                lock (_boundariesCacheLock)
                {
                    if (_cachedVatspyFirNames != null)
                    {
                        staleData = _cachedVatspyFirNames;
                    }
                }

                if (staleData != null)
                {
                    ctx.Response.Headers.Add("X-Cache", "STALE");
                    await ResponseJsonAsync(ctx, staleData);
                    return;
                }

                ctx.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                await ResponseJsonAsync(ctx, "{}");
            }
        }

        private async Task HandleIvaoBoundariesProxy(IHttpContext ctx)
        {
            try
            {
                // Check memory cache first
                string cachedData = null;
                lock (_boundariesCacheLock)
                {
                    if (_cachedIvaoBoundaries != null && (DateTime.Now - _ivaoBoundariesCacheTime) < BOUNDARIES_CACHE_TTL)
                    {
                        cachedData = _cachedIvaoBoundaries;
                    }
                }

                if (cachedData != null)
                {
                    ctx.Response.Headers.Add("X-Cache", "HIT");
                    await ResponseJsonAsync(ctx, cachedData);
                    return;
                }

                // Check permanent data directory first (preferred)
                string dataFilePath = Path.Combine(BOUNDARIES_DATA_DIR, "ivao_boundaries_geojson.json");
                if (File.Exists(dataFilePath))
                {
                    var fileInfo = new FileInfo(dataFilePath);
                    if ((DateTime.Now - fileInfo.LastWriteTime) < BOUNDARIES_CACHE_TTL)
                    {
                        string data = File.ReadAllText(dataFilePath);
                        lock (_boundariesCacheLock)
                        {
                            _cachedIvaoBoundaries = data;
                            _ivaoBoundariesCacheTime = fileInfo.LastWriteTime;
                        }
                        ctx.Response.Headers.Add("X-Cache", "DATA");
                        KneeboardLogger.Boundaries("IVAO loaded from data directory");
                        await ResponseJsonAsync(ctx, data);
                        return;
                    }
                }

                // Fallback to legacy disk cache (using _geojson suffix to invalidate old array-format cache)
                string diskCachePath = Path.Combine(BOUNDARIES_CACHE_DIR, "ivao_boundaries_geojson.json");
                if (File.Exists(diskCachePath))
                {
                    var fileInfo = new FileInfo(diskCachePath);
                    if ((DateTime.Now - fileInfo.LastWriteTime) < BOUNDARIES_CACHE_TTL)
                    {
                        string diskData = File.ReadAllText(diskCachePath);
                        lock (_boundariesCacheLock)
                        {
                            _cachedIvaoBoundaries = diskData;
                            _ivaoBoundariesCacheTime = fileInfo.LastWriteTime;
                        }
                        ctx.Response.Headers.Add("X-Cache", "DISK");
                        KneeboardLogger.Boundaries("IVAO loaded from disk cache");
                        await ResponseJsonAsync(ctx, diskData);
                        return;
                    }
                }

                // First, get the directory listing to find the latest file
                string latestFileUrl = GetLatestIvaoFileUrl();
                if (string.IsNullOrEmpty(latestFileUrl))
                {
                    throw new Exception("Could not find IVAO boundaries file");
                }

                // Download and extract the ZIP file
                using (var client = new WebClient())
                {
                    client.Headers.Add(HttpRequestHeader.UserAgent, "KneeboardServer/1.0");
                    byte[] zipData = client.DownloadData(latestFileUrl);

                    string rawJsonContent = ExtractIvaoJsonFromZip(zipData);
                    if (string.IsNullOrEmpty(rawJsonContent))
                    {
                        throw new Exception("Could not extract JSON from IVAO ZIP file");
                    }

                    // Convert IVAO array format to GeoJSON FeatureCollection
                    string geoJsonContent = ConvertIvaoToGeoJson(rawJsonContent);

                    lock (_boundariesCacheLock)
                    {
                        _cachedIvaoBoundaries = geoJsonContent;
                        _ivaoBoundariesCacheTime = DateTime.Now;
                    }

                    // Save to disk cache (as GeoJSON)
                    try
                    {
                        Directory.CreateDirectory(BOUNDARIES_CACHE_DIR);
                        File.WriteAllText(diskCachePath, geoJsonContent);
                        KneeboardLogger.Boundaries("IVAO saved to disk cache (GeoJSON format)");
                    }
                    catch (Exception cacheEx)
                    {
                        KneeboardLogger.BoundariesError($"IVAO failed to save disk cache: {cacheEx.Message}");
                    }

                    ctx.Response.Headers.Add("X-Cache", "MISS");
                    await ResponseJsonAsync(ctx, geoJsonContent);
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"IVAO Boundaries fetch error: {ex.Message}");

                // Try to return cached data even if expired
                string staleData = null;
                lock (_boundariesCacheLock)
                {
                    if (_cachedIvaoBoundaries != null)
                    {
                        staleData = _cachedIvaoBoundaries;
                    }
                }

                if (staleData != null)
                {
                    ctx.Response.Headers.Add("X-Cache", "STALE");
                    await ResponseJsonAsync(ctx, staleData);
                    return;
                }

                ctx.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                await ResponseJsonAsync(ctx, "{\"error\":\"Unable to fetch IVAO boundaries\"}");
            }
        }

        private string GetLatestIvaoFileUrl()
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add(HttpRequestHeader.UserAgent, "KneeboardServer/1.0");
                    string html = client.DownloadString("https://www.littlenavmap.org/downloads/Airspace%20Boundaries/");

                    // Parse HTML to find IVAO ZIP files and get the latest one
                    // Files are named like "IVAO%20ATC%20Positions%2020250801.zip" (URL-encoded)
                    // or "IVAO ATC Positions 20250801.zip" (plain text)
                    var regex = new System.Text.RegularExpressions.Regex(@"href=""(IVAO[^""]+\.zip)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var matches = regex.Matches(html);

                    string latestFile = null;
                    int latestDate = 0;

                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        string fileName = match.Groups[1].Value;
                        // Decode URL-encoded filename to extract date
                        string decodedFileName = Uri.UnescapeDataString(fileName);
                        // Extract date from filename (e.g., "20250801")
                        var dateMatch = System.Text.RegularExpressions.Regex.Match(decodedFileName, @"(\d{8})");
                        if (dateMatch.Success)
                        {
                            int fileDate = int.Parse(dateMatch.Groups[1].Value);
                            if (fileDate > latestDate)
                            {
                                latestDate = fileDate;
                                latestFile = fileName; // Keep original (possibly URL-encoded) filename
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(latestFile))
                    {
                        string url;
                        // File may already be URL-encoded in HTML, or not - handle both cases
                        if (latestFile.Contains("%"))
                        {
                            // Already URL-encoded
                            url = "https://www.littlenavmap.org/downloads/Airspace%20Boundaries/" + latestFile;
                        }
                        else
                        {
                            // Needs encoding
                            url = "https://www.littlenavmap.org/downloads/Airspace%20Boundaries/" + Uri.EscapeDataString(latestFile);
                        }
                        KneeboardLogger.BoundariesDebug($"IVAO boundaries URL: {url}");
                        return url;
                    }
                    else
                    {
                        KneeboardLogger.Warn("Boundaries", "No IVAO files found in directory listing");
                    }
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"Error getting IVAO file list: {ex.Message}");
            }

            return null;
        }

        private string ExtractIvaoJsonFromZip(byte[] zipData)
        {
            try
            {
                using (var zipStream = new MemoryStream(zipData))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        {
                            using (var entryStream = entry.Open())
                            using (var reader = new StreamReader(entryStream, Encoding.UTF8))
                            {
                                string rawJson = reader.ReadToEnd();
                                // Pre-filter to only include items with valid map_region
                                return FilterIvaoJson(rawJson);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"Error extracting IVAO ZIP: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Filters IVAO JSON to only include entries with valid map_region (>2 points)
        /// and extracts only the needed fields (airport_id, position, name, map_region)
        /// with reduced coordinate precision (4 decimal places = ~11m accuracy)
        /// This reduces the data from ~29MB to ~3-5MB
        /// </summary>
        private string FilterIvaoJson(string rawJson)
        {
            try
            {
                var filteredItems = new List<string>();
                int arrayStart = rawJson.IndexOf('[');
                if (arrayStart < 0) return rawJson;

                int depth = 0;
                int itemStart = -1;
                bool inString = false;
                bool escaped = false;

                for (int i = arrayStart; i < rawJson.Length; i++)
                {
                    char c = rawJson[i];

                    if (escaped) { escaped = false; continue; }
                    if (c == '\\' && inString) { escaped = true; continue; }
                    if (c == '"') { inString = !inString; continue; }
                    if (inString) continue;

                    if (c == '{')
                    {
                        if (depth == 1) itemStart = i;
                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 1 && itemStart >= 0)
                        {
                            string item = rawJson.Substring(itemStart, i - itemStart + 1);
                            // Extract and minimize item if it has valid geometry
                            string minimized = MinimizeIvaoItem(item);
                            if (minimized != null)
                            {
                                filteredItems.Add(minimized);
                            }
                            itemStart = -1;
                        }
                    }
                    else if (c == '[' && depth == 0) depth = 1;
                    else if (c == ']' && depth == 1) break;
                }

                KneeboardLogger.BoundariesDebug($"IVAO filter: {filteredItems.Count} items with valid geometry (minimized)");
                return "[" + string.Join(",", filteredItems) + "]";
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"IVAO filter error: {ex.Message}, returning raw data");
                return rawJson;
            }
        }

        /// <summary>
        /// Extracts only needed fields from IVAO item and reduces coordinate precision
        /// Returns null if item has no valid map_region
        /// </summary>
        private string MinimizeIvaoItem(string jsonItem)
        {
            // Extract airport_id
            string airportId = ExtractJsonStringValue(jsonItem, "airport_id");
            // Extract position
            string position = ExtractJsonStringValue(jsonItem, "position");
            // Extract name
            string name = ExtractJsonStringValue(jsonItem, "name");
            // Extract middle_identifier (sector ID, e.g., "N" for EDGG_N_CTR)
            string middleId = ExtractJsonStringValue(jsonItem, "middle_identifier");
            // Extract center_id (for CTR zones like LIRR, LFFF)
            string centerId = ExtractJsonStringValue(jsonItem, "center_id");

            // Extract and minimize map_region coordinates
            int mapRegionIdx = jsonItem.IndexOf("\"map_region\"");
            if (mapRegionIdx < 0) return null;

            int arrayStart = jsonItem.IndexOf('[', mapRegionIdx);
            if (arrayStart < 0) return null;

            // Find end of map_region array
            int depth = 0;
            int arrayEnd = -1;
            for (int i = arrayStart; i < jsonItem.Length; i++)
            {
                if (jsonItem[i] == '[') depth++;
                else if (jsonItem[i] == ']')
                {
                    depth--;
                    if (depth == 0) { arrayEnd = i; break; }
                }
            }
            if (arrayEnd < 0) return null;

            // Parse coordinates and minimize precision
            var coords = new List<string>();
            string mapRegion = jsonItem.Substring(arrayStart, arrayEnd - arrayStart + 1);

            // Extract {lat:..., lng:...} objects
            int objStart = -1;
            depth = 0;
            for (int i = 0; i < mapRegion.Length; i++)
            {
                if (mapRegion[i] == '{')
                {
                    if (depth == 0) objStart = i;
                    depth++;
                }
                else if (mapRegion[i] == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        string coordObj = mapRegion.Substring(objStart, i - objStart + 1);
                        double? lat = ExtractJsonNumberValue(coordObj, "lat");
                        double? lng = ExtractJsonNumberValue(coordObj, "lng");
                        if (lat.HasValue && lng.HasValue)
                        {
                            // Round to 4 decimal places (~11m precision)
                            coords.Add($"{{\"lat\":{Math.Round(lat.Value, 4).ToString(System.Globalization.CultureInfo.InvariantCulture)},\"lng\":{Math.Round(lng.Value, 4).ToString(System.Globalization.CultureInfo.InvariantCulture)}}}");
                        }
                        objStart = -1;
                    }
                }
            }

            // Need at least 3 points for a valid polygon
            if (coords.Count < 3) return null;

            // Build minimized JSON
            var sb = new System.Text.StringBuilder();
            sb.Append("{");
            if (!string.IsNullOrEmpty(airportId))
                sb.Append($"\"airport_id\":\"{EscapeJsonString(airportId)}\",");
            if (!string.IsNullOrEmpty(centerId))
                sb.Append($"\"center_id\":\"{EscapeJsonString(centerId)}\",");
            if (!string.IsNullOrEmpty(position))
                sb.Append($"\"position\":\"{EscapeJsonString(position)}\",");
            if (!string.IsNullOrEmpty(middleId))
                sb.Append($"\"middle_identifier\":\"{EscapeJsonString(middleId)}\",");
            if (!string.IsNullOrEmpty(name))
                sb.Append($"\"name\":\"{EscapeJsonString(name)}\",");
            sb.Append("\"map_region\":[");
            sb.Append(string.Join(",", coords));
            sb.Append("]}");

            return sb.ToString();
        }

        private string ExtractJsonStringValue(string json, string key)
        {
            string searchKey = $"\"{key}\"";
            int keyIdx = json.IndexOf(searchKey);
            if (keyIdx < 0) return null;

            int colonIdx = json.IndexOf(':', keyIdx + searchKey.Length);
            if (colonIdx < 0) return null;

            // Find start quote
            int startQuote = json.IndexOf('"', colonIdx + 1);
            if (startQuote < 0) return null;

            // Check if it's null
            string afterColon = json.Substring(colonIdx + 1, Math.Min(10, json.Length - colonIdx - 1)).Trim();
            if (afterColon.StartsWith("null")) return null;

            // Find end quote (handling escapes)
            int endQuote = startQuote + 1;
            while (endQuote < json.Length)
            {
                if (json[endQuote] == '\\') { endQuote += 2; continue; }
                if (json[endQuote] == '"') break;
                endQuote++;
            }
            if (endQuote >= json.Length) return null;

            return json.Substring(startQuote + 1, endQuote - startQuote - 1);
        }

        private double? ExtractJsonNumberValue(string json, string key)
        {
            string searchKey = $"\"{key}\"";
            int keyIdx = json.IndexOf(searchKey);
            if (keyIdx < 0) return null;

            int colonIdx = json.IndexOf(':', keyIdx + searchKey.Length);
            if (colonIdx < 0) return null;

            // Find number start
            int numStart = colonIdx + 1;
            while (numStart < json.Length && (json[numStart] == ' ' || json[numStart] == '\t')) numStart++;

            // Find number end
            int numEnd = numStart;
            while (numEnd < json.Length && (char.IsDigit(json[numEnd]) || json[numEnd] == '.' || json[numEnd] == '-' || json[numEnd] == 'e' || json[numEnd] == 'E' || json[numEnd] == '+'))
                numEnd++;

            if (numEnd <= numStart) return null;

            string numStr = json.Substring(numStart, numEnd - numStart);
            if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double result))
                return result;
            return null;
        }

        private string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        /// <summary>
        /// Konvertiert IVAO Array-Format zu GeoJSON FeatureCollection
        /// </summary>
        private string ConvertIvaoToGeoJson(string ivaoArrayJson)
        {
            try
            {
                var features = new List<string>();
                int arrayStart = ivaoArrayJson.IndexOf('[');
                if (arrayStart < 0) return "{\"type\":\"FeatureCollection\",\"features\":[]}";

                int depth = 0;
                int itemStart = -1;
                bool inString = false;
                bool escaped = false;

                for (int i = arrayStart; i < ivaoArrayJson.Length; i++)
                {
                    char c = ivaoArrayJson[i];

                    if (escaped) { escaped = false; continue; }
                    if (c == '\\' && inString) { escaped = true; continue; }
                    if (c == '"') { inString = !inString; continue; }
                    if (inString) continue;

                    if (c == '{')
                    {
                        if (depth == 1) itemStart = i;
                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 1 && itemStart >= 0)
                        {
                            string item = ivaoArrayJson.Substring(itemStart, i - itemStart + 1);
                            string feature = ConvertIvaoItemToGeoJsonFeature(item);
                            if (feature != null)
                            {
                                features.Add(feature);
                            }
                            itemStart = -1;
                        }
                    }
                    else if (c == '[' && depth == 0) depth = 1;
                    else if (c == ']' && depth == 1) break;
                }

                KneeboardLogger.BoundariesDebug($"IVAO -> GeoJSON: {features.Count} features converted");

                // Debug: Log some sample feature IDs for verification
                if (features.Count > 0)
                {
                    var sampleIds = features.Take(20).Select(f => {
                        int idStart = f.IndexOf("\"id\":\"") + 6;
                        int idEnd = f.IndexOf("\"", idStart);
                        return idStart > 5 && idEnd > idStart ? f.Substring(idStart, idEnd - idStart) : "?";
                    });
                    KneeboardLogger.BoundariesDebug($"IVAO Sample IDs: {string.Join(", ", sampleIds)}");
                }

                return "{\"type\":\"FeatureCollection\",\"features\":[" + string.Join(",", features) + "]}";
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"IVAO GeoJSON conversion error: {ex.Message}");
                return "{\"type\":\"FeatureCollection\",\"features\":[]}";
            }
        }

        /// <summary>
        /// Konvertiert ein einzelnes IVAO Item zu einem GeoJSON Feature
        /// </summary>
        private string ConvertIvaoItemToGeoJsonFeature(string jsonItem)
        {
            // Extract fields directly from LittleNavMap data
            // APP zones have airport_id (e.g., "SUMU"), CTR zones have center_id (e.g., "LIRR")
            string airportId = ExtractJsonStringValue(jsonItem, "airport_id");
            string centerId = ExtractJsonStringValue(jsonItem, "center_id");
            string position = ExtractJsonStringValue(jsonItem, "position");
            string name = ExtractJsonStringValue(jsonItem, "name");
            string middleId = ExtractJsonStringValue(jsonItem, "middle_identifier");

            // ICAO code for fallback matching: airport_id for APP zones, center_id for CTR/FSS zones
            string icao = !string.IsNullOrEmpty(airportId) ? airportId : centerId;

            // For zone ID and prefix: use airport_id if available, otherwise use NAME (not center_id!)
            // This ensures zones like "ROMA RADAR_EW_CTR" keep their name-based ID, but have icao="LIRR" for matching
            if (string.IsNullOrEmpty(airportId))
            {
                if (string.IsNullOrEmpty(name))
                {
                    return null; // No identifier at all, skip
                }
                // Use name as the identifier - zone ID will be like "ROMA RADAR_EW_CTR"
                airportId = name;
            }

            // Extract map_region coordinates
            int mapRegionIdx = jsonItem.IndexOf("\"map_region\"");
            if (mapRegionIdx < 0) return null;

            int arrayStart = jsonItem.IndexOf('[', mapRegionIdx);
            if (arrayStart < 0) return null;

            int depth = 0;
            int arrayEnd = -1;
            for (int i = arrayStart; i < jsonItem.Length; i++)
            {
                if (jsonItem[i] == '[') depth++;
                else if (jsonItem[i] == ']')
                {
                    depth--;
                    if (depth == 0) { arrayEnd = i; break; }
                }
            }
            if (arrayEnd < 0) return null;

            // Parse coordinates and convert to GeoJSON format [lng, lat]
            var geoJsonCoords = new List<string>();
            string mapRegion = jsonItem.Substring(arrayStart, arrayEnd - arrayStart + 1);

            int objStart = -1;
            depth = 0;
            for (int i = 0; i < mapRegion.Length; i++)
            {
                if (mapRegion[i] == '{')
                {
                    if (depth == 0) objStart = i;
                    depth++;
                }
                else if (mapRegion[i] == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        string coordObj = mapRegion.Substring(objStart, i - objStart + 1);
                        double? lat = ExtractJsonNumberValue(coordObj, "lat");
                        double? lng = ExtractJsonNumberValue(coordObj, "lng");
                        if (lat.HasValue && lng.HasValue)
                        {
                            // GeoJSON uses [lng, lat] order!
                            geoJsonCoords.Add($"[{Math.Round(lng.Value, 4).ToString(System.Globalization.CultureInfo.InvariantCulture)},{Math.Round(lat.Value, 4).ToString(System.Globalization.CultureInfo.InvariantCulture)}]");
                        }
                        objStart = -1;
                    }
                }
            }

            // Need at least 3 points for a valid polygon
            if (geoJsonCoords.Count < 3) return null;

            // Close the polygon if not already closed
            if (geoJsonCoords[0] != geoJsonCoords[geoJsonCoords.Count - 1])
            {
                geoJsonCoords.Add(geoJsonCoords[0]);
            }

            // Build GeoJSON Feature
            string prefix = !string.IsNullOrEmpty(airportId) ? airportId.ToUpperInvariant() : "";

            // Construct feature ID like LittleNavMap: {airport_id}[_{middle_id}]_{position}
            // Examples: EDGG_CTR, EDGG_N_CTR, EDDS_APP
            string featureId;
            if (!string.IsNullOrEmpty(middleId) && !string.IsNullOrEmpty(position))
            {
                // Sectored zone: EDGG_N_CTR
                featureId = prefix + "_" + middleId.ToUpperInvariant() + "_" + position.ToUpperInvariant();
            }
            else if (!string.IsNullOrEmpty(position))
            {
                // Standard zone: EDGG_CTR
                featureId = prefix + "_" + position.ToUpperInvariant();
            }
            else
            {
                // No position: just prefix
                featureId = prefix;
            }

            // Skip zones without valid ID
            if (string.IsNullOrEmpty(prefix))
            {
                return null;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("{\"type\":\"Feature\",\"properties\":{");
            sb.Append($"\"id\":\"{EscapeJsonString(featureId)}\",");
            sb.Append($"\"prefix\":\"{EscapeJsonString(prefix)}\",");
            if (!string.IsNullOrEmpty(position))
                sb.Append($"\"position\":\"{EscapeJsonString(position.ToUpperInvariant())}\",");
            if (!string.IsNullOrEmpty(middleId))
                sb.Append($"\"sectorId\":\"{EscapeJsonString(middleId.ToUpperInvariant())}\",");
            // ICAO code for fallback matching (airport_id for APP, center_id for CTR)
            if (!string.IsNullOrEmpty(icao))
                sb.Append($"\"icao\":\"{EscapeJsonString(icao.ToUpperInvariant())}\",");
            // Store original name for matching with controller's atcSession.position
            sb.Append($"\"name\":\"{EscapeJsonString(name ?? featureId)}\",");
            // radioCallsign is the exact name from LittleNavMap data for matching
            sb.Append($"\"radioCallsign\":\"{EscapeJsonString(name ?? "")}\"");
            sb.Append("},\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[");
            sb.Append(string.Join(",", geoJsonCoords));
            sb.Append("]]}}");

            return sb.ToString();
        }

        private async Task HandleNominatimProxy(IHttpContext ctx)
        {
            var request = ctx.Request;
            var queryString = request.Url.Query;

            // Check if query is too short (Nominatim blocks very short queries)
            var queryParam = request.QueryString["q"] ?? string.Empty;
            if (queryParam.Length < 2)
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                string payload = "[]"; // Return empty results for very short queries
                var buffer = Encoding.UTF8.GetBytes(payload);
                ctx.Response.ContentType = "application/json";
                // ctx.Response.ContentLength = buffer.Length;
                await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                // EmbedIO manages stream
                return;
            }

            // Build the target URL for Nominatim
            var targetUrl = $"https://nominatim.openstreetmap.org/search{queryString}";

            try
            {
                var outboundRequest = (HttpWebRequest)WebRequest.Create(targetUrl);
                outboundRequest.Method = "GET";
                outboundRequest.Accept = "application/json";
                outboundRequest.UserAgent = "KneeboardServer/1.0 Flight Planning Application"; // Nominatim requires a descriptive User-Agent
                outboundRequest.Referer = $"http://localhost:{_port}/"; // Add Referer header
                outboundRequest.Timeout = 10000;
                outboundRequest.ReadWriteTimeout = 10000;

                using (var upstreamResponse = (HttpWebResponse)outboundRequest.GetResponse())
                {
                    ctx.Response.StatusCode = (int)upstreamResponse.StatusCode;
                    ctx.Response.ContentType = upstreamResponse.ContentType ?? "application/json";

                    using (var upstreamStream = upstreamResponse.GetResponseStream())
                    {
                        await upstreamStream.CopyToAsync(ctx.Response.OutputStream);
                    }
                }
            }
            catch (WebException ex)
            {
                var httpResponse = ex.Response as HttpWebResponse;
                ctx.Response.StatusCode = httpResponse != null
                    ? (int)httpResponse.StatusCode
                    : (int)HttpStatusCode.BadGateway;

                KneeboardLogger.ProxyError($"Nominatim Proxy Error: {ex.Message}");
                KneeboardLogger.ProxyDebug($"Requested URL: {targetUrl}");
                if (httpResponse != null)
                {
                    KneeboardLogger.ProxyError($"Upstream Status: {httpResponse.StatusCode}");
                }

                // Return JSON error instead of HTML from Nominatim
                string payload = "{\"error\":\"Nominatim service unavailable or request blocked. Try a more specific search query.\"}";
                var buffer = Encoding.UTF8.GetBytes(payload);
                ctx.Response.ContentType = "application/json";
                // ctx.Response.ContentLength = buffer.Length;
                await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            finally
            {
                // EmbedIO manages stream
            }
        }

        // Static HttpClient for wind proxy - reused for connection pooling
        // AllowAutoRedirect=false because we handle redirects manually (NOAA does cross-protocol redirects)
        private static readonly HttpClient _windHttpClient;

        static SimpleHTTPServer()
        {
            // Configure HttpClient WITHOUT automatic redirect - we handle redirects manually
            // NOAA NOMADS does cross-protocol redirects (HTTPS->HTTP) which .NET refuses to follow automatically
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,  // MANUAL redirect handling
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            _windHttpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _windHttpClient.DefaultRequestHeaders.Add("User-Agent", "KneeboardServer/2.0 Flight Planning Application");
            _windHttpClient.DefaultRequestHeaders.Add("Accept", "*/*");
        }

        private async Task HandleWindProxy(IHttpContext ctx)
        {
            var request = ctx.Request;
            var path = request.Url.AbsolutePath;
            var queryString = request.Url.Query;

            // Extract the GFS path from /api/wind/gfs{date}/gfs_1p00_{run}z.ascii?...
            var idx = path.IndexOf("/api/wind/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                // EmbedIO manages stream
                return;
            }

            var gfsPart = path.Substring(idx + "/api/wind/".Length);
            var targetUrl = $"https://nomads.ncep.noaa.gov/dods/{gfsPart}{queryString}";

            KneeboardLogger.ProxyDebug($"Wind Proxy requesting: {targetUrl}");

            try
            {
                // Manual redirect following - NOAA does HTTPS->HTTP redirects which .NET won't auto-follow
                HttpResponseMessage response = null;
                var currentUrl = targetUrl;
                int maxRedirects = 5;

                for (int i = 0; i <= maxRedirects; i++)
                {
                    var requestMsg = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                    var responseTask = _windHttpClient.SendAsync(requestMsg, HttpCompletionOption.ResponseHeadersRead);
                    responseTask.Wait();
                    response = responseTask.Result;

                    // Check for redirect (3xx status codes)
                    int statusCode = (int)response.StatusCode;
                    if (statusCode >= 300 && statusCode < 400)
                    {
                        var redirectUrl = response.Headers.Location;
                        if (redirectUrl == null)
                        {
                            KneeboardLogger.Warn("Proxy", $"Wind Proxy redirect {statusCode} without Location header");
                            break;
                        }

                        // Handle relative redirects
                        if (!redirectUrl.IsAbsoluteUri)
                        {
                            redirectUrl = new Uri(new Uri(currentUrl), redirectUrl);
                        }

                        currentUrl = redirectUrl.ToString();
                        KneeboardLogger.ProxyDebug($"Wind Proxy following {statusCode} redirect to: {currentUrl}");
                        response.Dispose();
                        continue;
                    }

                    break; // Not a redirect, use this response
                }

                KneeboardLogger.ProxyDebug($"Final Response: {response.StatusCode}, ContentLength: {response.Content.Headers.ContentLength}");

                if (response.IsSuccessStatusCode)
                {
                    var contentTask = response.Content.ReadAsByteArrayAsync();
                    contentTask.Wait();
                    var content = contentTask.Result;

                    ctx.Response.StatusCode = (int)response.StatusCode;
                    ctx.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "text/plain";
                    // ctx.Response.ContentLength = content.Length;
                    await ctx.Response.OutputStream.WriteAsync(content, 0, content.Length);
                }
                else
                {
                    KneeboardLogger.Proxy($"Non-success status: {response.StatusCode}");
                    ctx.Response.StatusCode = (int)response.StatusCode;
                    ctx.Response.ContentType = "text/plain";
                }
            }
            catch (AggregateException ae)
            {
                var innerEx = ae.InnerException ?? ae;
                KneeboardLogger.ProxyError($"Error: {innerEx.Message}");
                ctx.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                ctx.Response.ContentType = "text/plain";
            }
            catch (Exception ex)
            {
                KneeboardLogger.ProxyError($"Error: {ex.Message}");
                ctx.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                ctx.Response.ContentType = "text/plain";
            }
            finally
            {
                // EmbedIO manages stream
            }
        }

        // ============================================================================
        // SimConnect API Handlers
        // ============================================================================

        private async Task HandleSimConnectPositionRequest(IHttpContext ctx)
        {
            try
            {
                var position = _kneeboardServer.GetSimConnectPosition();
                var isFlightLoaded = _kneeboardServer.IsSimConnectFlightLoaded();

                if (position.HasValue && isFlightLoaded)
                {
                    var pos = position.Value;

                    var json = new
                    {
                        connected = true,
                        flightLoaded = true,
                        latitude = pos.Latitude,
                        longitude = pos.Longitude,
                        altitude = pos.Altitude,
                        heading = pos.Heading,
                        groundSpeed = pos.GroundSpeed,
                        indicatedAirspeed = pos.IndicatedAirspeed,
                        windDirection = pos.WindDirection,
                        windSpeed = pos.WindSpeed,
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };

                    string jsonString = Newtonsoft.Json.JsonConvert.SerializeObject(json);
                    await ResponseJsonAsync(ctx, jsonString);
                }
                else
                {
                    await ResponseJsonAsync(ctx, "{\"connected\":" + (position.HasValue ? "true" : "false") + ",\"flightLoaded\":false}");
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.SimConnectError($"Position request error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, "{\"connected\":false,\"error\":\"Internal error\"}");
            }
        }

        private async Task HandleSimConnectStatusRequest(IHttpContext ctx)
        {
            try
            {
                bool connected = _kneeboardServer.IsSimConnectConnected();

                var json = new
                {
                    connected = connected,
                    simulator = connected ? "MSFS" : (object)null,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                await ResponseJsonAsync(ctx, Newtonsoft.Json.JsonConvert.SerializeObject(json));
            }
            catch (Exception ex)
            {
                KneeboardLogger.SimConnectError($"Status request error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, "{\"connected\":false}");
            }
        }

        private async Task HandleSimConnectTeleportRequest(IHttpContext ctx)
        {
            if (ctx.Request.HttpMethod != "POST")
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                await ResponseJsonAsync(ctx, "{\"success\":false,\"error\":\"Method not allowed\"}");
                return;
            }

            try
            {
                string requestBody = await GetPostedTextAsync(ctx);
                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(requestBody);

                double lat = data.lat;
                double lng = data.lng;
                double? altitude = data.altitude != null ? (double?)data.altitude : null;
                double? heading = data.heading != null ? (double?)data.heading : null;
                double? speed = data.speed != null ? (double?)data.speed : null;

                _kneeboardServer.SimConnectTeleport(lat, lng, altitude, heading, speed);

                await ResponseJsonAsync(ctx, "{\"success\":true,\"error\":null}");
            }
            catch (Exception ex)
            {
                KneeboardLogger.SimConnectError($"Teleport error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                string errorJson = Newtonsoft.Json.JsonConvert.SerializeObject(new { success = false, error = ex.Message });
                await ResponseJsonAsync(ctx, errorJson);
            }
        }

        private async Task HandleSimConnectPauseRequest(IHttpContext ctx)
        {
            if (ctx.Request.HttpMethod != "POST")
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                await ResponseJsonAsync(ctx, "{\"success\":false,\"error\":\"Method not allowed\"}");
                return;
            }

            try
            {
                string requestBody = await GetPostedTextAsync(ctx);
                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(requestBody);

                bool paused = data.paused;

                // DUMMY IMPLEMENTATION - MSFS 2024 Pause Events funktionieren nicht!
                // Pause/Resume Events (PAUSE_ON, PAUSE_OFF, PAUSE_TOGGLE, SLEW_TOGGLE)
                // sind alle in MSFS 2024 kaputt via SimConnect.
                // Wir geben einfach success zurück ohne wirklich zu pausieren.
                KneeboardLogger.SimConnect($"Pause request received (paused={paused}) - IGNORED (MSFS 2024 bug)");

                string responseJson = Newtonsoft.Json.JsonConvert.SerializeObject(new { success = true, paused = paused });
                await ResponseJsonAsync(ctx, responseJson);
            }
            catch (Exception ex)
            {
                KneeboardLogger.SimConnectError($"Pause error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                string errorJson = Newtonsoft.Json.JsonConvert.SerializeObject(new { success = false, error = ex.Message });
                await ResponseJsonAsync(ctx, errorJson);
            }
        }

        private async Task HandleSimConnectRadioFrequencyRequest(IHttpContext ctx)
        {
            if (ctx.Request.HttpMethod != "POST")
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                await ResponseJsonAsync(ctx, "{\"success\":false,\"error\":\"Method not allowed\"}");
                return;
            }

            try
            {
                string requestBody = await GetPostedTextAsync(ctx);
                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(requestBody);

                string radio = data.radio;
                uint frequencyHz = data.frequencyHz;

                _kneeboardServer.SimConnectSetRadioFrequency(radio, frequencyHz);

                await ResponseJsonAsync(ctx, "{\"success\":true,\"error\":null}");
            }
            catch (Exception ex)
            {
                KneeboardLogger.SimConnectError($"Radio frequency error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                string errorJson = Newtonsoft.Json.JsonConvert.SerializeObject(new { success = false, error = ex.Message });
                await ResponseJsonAsync(ctx, errorJson);
            }
        }

        // ============================================================================
        // Procedure API Methods (Navigraph + SimBrief Hybrid)
        // ============================================================================

        /// <summary>
        /// Get list of SIDs for an airport
        /// URL: /api/procedures/sids/{icao}
        /// </summary>
        private async Task HandleGetSIDListRequest(IHttpContext ctx, string command)
        {
            try
            {
                string icao = command.Replace("/api/procedures/sids/", "").Trim('/').ToUpper();
                if (string.IsNullOrEmpty(icao))
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"ICAO code required\"}");
                    return;
                }

                // Try Navigraph first
                if (_navigraphAuth?.IsAuthenticated == true && _navigraphData?.IsDataAvailable == true)
                {
                    var sids = _navigraphData.GetSIDs(icao);
                    var response = new SIDListResponse
                    {
                        Source = "Navigraph",
                        AiracCycle = _navigraphData.CurrentAiracCycle,
                        Airport = icao,
                        Sids = sids
                    };
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                    await ResponseJsonAsync(ctx, json);
                    return;
                }

                // Fallback to SimBrief
                var sidData = Kneeboard_Server.GetSidWaypointsFromSimbrief();
                string fallbackJson = Newtonsoft.Json.JsonConvert.SerializeObject(sidData, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, fallbackJson);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"SID list error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Get list of STARs for an airport
        /// URL: /api/procedures/stars/{icao}
        /// </summary>
        private async Task HandleGetSTARListRequest(IHttpContext ctx, string command)
        {
            try
            {
                string icao = command.Replace("/api/procedures/stars/", "").Trim('/').ToUpper();
                if (string.IsNullOrEmpty(icao))
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"ICAO code required\"}");
                    return;
                }

                // Try Navigraph first
                if (_navigraphAuth?.IsAuthenticated == true && _navigraphData?.IsDataAvailable == true)
                {
                    var stars = _navigraphData.GetSTARs(icao);
                    var response = new STARListResponse
                    {
                        Source = "Navigraph",
                        AiracCycle = _navigraphData.CurrentAiracCycle,
                        Airport = icao,
                        Stars = stars
                    };
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                    await ResponseJsonAsync(ctx, json);
                    return;
                }

                // Fallback to SimBrief
                var starData = Kneeboard_Server.GetStarWaypointsFromSimbrief();
                string fallbackJson = Newtonsoft.Json.JsonConvert.SerializeObject(starData, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, fallbackJson);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"STAR list error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Get list of approaches for an airport
        /// URL: /api/procedures/approaches/{icao}
        /// </summary>
        private async Task HandleGetApproachListRequest(IHttpContext ctx, string command)
        {
            try
            {
                string icao = command.Replace("/api/procedures/approaches/", "").Trim('/').ToUpper();
                if (string.IsNullOrEmpty(icao))
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"ICAO code required\"}");
                    return;
                }

                // Approaches only available via Navigraph
                if (_navigraphAuth?.IsAuthenticated == true && _navigraphData?.IsDataAvailable == true)
                {
                    var approaches = _navigraphData.GetApproaches(icao);
                    var response = new ApproachListResponse
                    {
                        Source = "Navigraph",
                        AiracCycle = _navigraphData.CurrentAiracCycle,
                        Airport = icao,
                        Approaches = approaches
                    };
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                    await ResponseJsonAsync(ctx, json);
                    return;
                }

                // No fallback for approaches
                ctx.Response.StatusCode = 503;
                await ResponseJsonAsync(ctx, "{\"error\":\"Approach data requires Navigraph authentication. Please log in via the Info panel.\"}");
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Approach list error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Get detailed procedure with waypoints
        /// URL: /api/procedures/procedure/{icao}/{name}?transition=xxx&type=SID|STAR|Approach
        /// </summary>
        private async Task HandleGetProcedureRequest(IHttpContext ctx, string command)
        {
            try
            {
                // Parse: /api/procedures/procedure/EDDM/GIVMI1N
                string path = command.Replace("/api/procedures/procedure/", "").Trim('/');
                string[] parts = path.Split('/');

                if (parts.Length < 2)
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Format: /api/procedures/procedure/{icao}/{name}\"}");
                    return;
                }

                string icao = parts[0].ToUpper();
                string procedureName = parts[1].ToUpper();

                // Parse query parameters
                var query = ctx.Request.QueryString;
                string transition = query["transition"];
                string typeStr = query["type"] ?? "SID";
                ProcedureType type = ProcedureType.SID;
                if (Enum.TryParse(typeStr, true, out ProcedureType parsedType))
                {
                    type = parsedType;
                }

                // Only available via Navigraph
                if (_navigraphAuth?.IsAuthenticated == true && _navigraphData?.IsDataAvailable == true)
                {
                    var detail = _navigraphData.GetProcedureDetail(icao, procedureName, transition, type);
                    if (detail != null && detail.Waypoints.Count > 0)
                    {
                        string json = Newtonsoft.Json.JsonConvert.SerializeObject(detail, Newtonsoft.Json.Formatting.Indented);
                        await ResponseJsonAsync(ctx, json);
                        return;
                    }

                    ctx.Response.StatusCode = 404;
                    await ResponseJsonAsync(ctx, $"{{\"error\":\"Procedure {procedureName} not found at {icao}\"}}");
                    return;
                }

                // Fallback: return SimBrief procedures if available
                var simbrief = Kneeboard_Server.GetSimbriefProcedures();
                string fallbackJson = Newtonsoft.Json.JsonConvert.SerializeObject(simbrief, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, fallbackJson);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Procedure detail error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Alias for approach list
        /// </summary>
        private async Task HandleGetApproachRequest(IHttpContext ctx, string command)
        {
            // Redirect to approach list handler
            string newCommand = command.Replace("/api/procedures/approach/", "/api/procedures/approaches/");
            await HandleGetApproachListRequest(ctx, newCommand);
        }

        /// <summary>
        /// Get Navigraph status
        /// URL: /api/navigraph/status
        /// </summary>
        private async Task HandleNavigraphStatusRequest(IHttpContext ctx)
        {
            try
            {
                var status = _navigraphData?.GetStatus() ?? new NavigraphStatus { Authenticated = false };
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(status, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Status error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        private async Task HandleProcedureStatusRequest(IHttpContext ctx)
        {
            await HandleNavigraphStatusRequest(ctx);
        }

        private async Task HandleNavdataFolderInfoRequest(IHttpContext ctx)
        {
            try
            {
                var info = new
                {
                    navigraphAuthenticated = _navigraphAuth?.IsAuthenticated ?? false,
                    navigraphUsername = _navigraphAuth?.Username,
                    airacCycle = _navigraphData?.CurrentAiracCycle,
                    databasePath = _navigraphData?.DatabasePath,
                    isDataAvailable = _navigraphData?.IsDataAvailable ?? false
                };
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(info, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        private async Task HandleProcedureDebugRequest(IHttpContext ctx, string command)
        {
            // Debug: return procedure info for an airport
            string icao = command.Replace("/api/procedures/debug/", "").Trim('/').ToUpper();
            try
            {
                var debug = new
                {
                    airport = icao,
                    navigraphAvailable = _navigraphData?.IsDataAvailable ?? false,
                    sidsCount = _navigraphData?.GetSIDs(icao)?.Count ?? 0,
                    starsCount = _navigraphData?.GetSTARs(icao)?.Count ?? 0,
                    approachesCount = _navigraphData?.GetApproaches(icao)?.Count ?? 0
                };
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(debug, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        private async Task HandleAllProceduresDebugRequest(IHttpContext ctx)
        {
            await HandleNavdataFolderInfoRequest(ctx);
        }

        /// <summary>
        /// Gets combined SID and STAR waypoints from SimBrief navlog
        /// URL: /api/procedures/simbrief
        /// </summary>
        private async Task HandleSimbriefProceduresRequest(IHttpContext ctx)
        {
            try
            {
                var procedures = Kneeboard_Server.GetSimbriefProcedures();
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(procedures, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"SimBrief procedures error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Gets SID waypoints from SimBrief navlog
        /// URL: /api/procedures/simbrief/sid
        /// </summary>
        private async Task HandleSimbriefSidRequest(IHttpContext ctx)
        {
            try
            {
                var sidData = Kneeboard_Server.GetSidWaypointsFromSimbrief();
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(sidData, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"SimBrief SID error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Gets STAR waypoints from SimBrief navlog
        /// URL: /api/procedures/simbrief/star
        /// </summary>
        private async Task HandleSimbriefStarRequest(IHttpContext ctx)
        {
            try
            {
                var starData = Kneeboard_Server.GetStarWaypointsFromSimbrief();
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(starData, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"SimBrief STAR error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        // ============================================================================
        // ILS API Methods (Navigraph Integration)
        // ============================================================================

        /// <summary>
        /// Get ILS data for an airport
        /// URL: /api/ils/{icao}
        /// </summary>
        private async Task HandleIlsRequest(IHttpContext ctx, string command)
        {
            try
            {
                string icao = command.Replace("/api/ils/", "").Trim('/').ToUpper();
                if (string.IsNullOrEmpty(icao))
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"ICAO code required\"}");
                    return;
                }

                // ILS data only available via Navigraph
                if (_navigraphAuth?.IsAuthenticated == true && _navigraphData?.IsDataAvailable == true)
                {
                    var ilsData = _navigraphData.GetILSData(icao);
                    var runways = _navigraphData.GetRunways(icao);

                    var response = new
                    {
                        source = "Navigraph",
                        airacCycle = _navigraphData.CurrentAiracCycle,
                        airport = icao,
                        ils = ilsData,
                        runways = runways
                    };
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                    await ResponseJsonAsync(ctx, json);
                    return;
                }

                // No fallback for ILS data
                ctx.Response.StatusCode = 503;
                await ResponseJsonAsync(ctx, "{\"error\":\"ILS data requires Navigraph authentication. Please log in via the Info panel.\"}");
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Get runway data for flight path drawing
        /// URL: /api/navigraph/runway/{icao}/{runway}
        /// </summary>
        private async Task HandleRunwayRequest(IHttpContext ctx, string command)
        {
            try
            {
                // Parse: api/navigraph/runway/EDDM/25C
                string path = command.Replace("api/navigraph/runway/", "").Trim('/');
                string[] parts = path.Split('/');

                if (parts.Length < 2)
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Format: /api/navigraph/runway/{icao}/{runway}\"}");
                    return;
                }

                string icao = parts[0].ToUpperInvariant();
                string runwayId = parts[1].ToUpperInvariant();

                if (_navigraphData?.IsDataAvailable != true)
                {
                    ctx.Response.StatusCode = 503;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Navigraph data not available\"}");
                    return;
                }

                var runway = _navigraphData.DbService.GetRunwayWithOppositeEnd(icao, runwayId);
                if (runway == null)
                {
                    ctx.Response.StatusCode = 404;
                    await ResponseJsonAsync(ctx, $"{{\"error\":\"Runway {runwayId} not found at {icao}\"}}");
                    return;
                }

                var response = new
                {
                    airport = icao,
                    runway = runway.Identifier,
                    thresholdLat = runway.ThresholdLat,
                    thresholdLon = runway.ThresholdLon,
                    endLat = runway.EndLat,
                    endLon = runway.EndLon,
                    heading = runway.Heading,
                    length = runway.Length,
                    width = runway.Width,
                    elevation = runway.ThresholdElevation,
                    airacCycle = _navigraphData.CurrentAiracCycle
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Runway error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Get all runways for an airport
        /// URL: /api/navigraph/runways/{icao}
        /// </summary>
        private async Task HandleRunwaysRequest(IHttpContext ctx, string command)
        {
            try
            {
                string icao = command.Replace("api/navigraph/runways/", "").Trim('/').ToUpperInvariant();

                if (string.IsNullOrEmpty(icao))
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"ICAO code required\"}");
                    return;
                }

                if (_navigraphData?.IsDataAvailable != true)
                {
                    ctx.Response.StatusCode = 503;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Navigraph data not available\"}");
                    return;
                }

                var runways = _navigraphData.GetRunways(icao);
                var response = new
                {
                    airport = icao,
                    runways = runways.Select(r => new
                    {
                        identifier = r.Identifier,
                        thresholdLat = r.ThresholdLat,
                        thresholdLon = r.ThresholdLon,
                        endLat = r.EndLat,
                        endLon = r.EndLon,
                        heading = r.Heading,
                        length = r.Length,
                        width = r.Width,
                        elevation = r.ThresholdElevation
                    }),
                    airacCycle = _navigraphData.CurrentAiracCycle
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Runways error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Get airport information
        /// URL: /api/navigraph/airport/{icao}
        /// </summary>
        private async Task HandleAirportRequest(IHttpContext ctx, string command)
        {
            try
            {
                string icao = command.Replace("api/navigraph/airport/", "").Trim('/').ToUpperInvariant();

                if (string.IsNullOrEmpty(icao))
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"ICAO code required\"}");
                    return;
                }

                if (_navigraphData?.IsDataAvailable != true)
                {
                    ctx.Response.StatusCode = 503;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Navigraph data not available\"}");
                    return;
                }

                var airport = _navigraphData.GetAirport(icao);
                if (airport == null)
                {
                    ctx.Response.StatusCode = 404;
                    await ResponseJsonAsync(ctx, $"{{\"error\":\"Airport {icao} not found\"}}");
                    return;
                }

                var ils = _navigraphData.GetILSData(icao);
                var sids = _navigraphData.GetSIDs(icao);
                var stars = _navigraphData.GetSTARs(icao);
                var approaches = _navigraphData.GetApproaches(icao);

                var response = new
                {
                    icao = airport.Icao,
                    iata = airport.Iata,
                    name = airport.Name,
                    latitude = airport.Latitude,
                    longitude = airport.Longitude,
                    elevation = airport.Elevation,
                    transitionAltitude = airport.TransitionAltitude,
                    transitionLevel = airport.TransitionLevel,
                    runways = airport.Runways?.Select(r => new
                    {
                        identifier = r.Identifier,
                        thresholdLat = r.ThresholdLat,
                        thresholdLon = r.ThresholdLon,
                        endLat = r.EndLat,
                        endLon = r.EndLon,
                        heading = r.Heading,
                        length = r.Length,
                        width = r.Width
                    }),
                    frequencies = airport.Frequencies?.Select(f => new
                    {
                        type = f.Type,
                        frequency = f.Frequency,
                        name = f.Name
                    }),
                    ils = ils?.Select(i => new
                    {
                        identifier = i.Identifier,
                        runway = i.RunwayIdentifier,
                        frequency = i.Frequency,
                        bearing = i.LocalizerBearing,
                        glideSlope = i.GlideSlopeAngle,
                        category = i.Category
                    }),
                    procedureCount = new
                    {
                        sids = sids?.Count ?? 0,
                        stars = stars?.Count ?? 0,
                        approaches = approaches?.Count ?? 0
                    },
                    airacCycle = _navigraphData.CurrentAiracCycle
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Airport error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Get waypoint information
        /// URL: /api/navigraph/waypoint/{ident}
        /// </summary>
        private async Task HandleWaypointRequest(IHttpContext ctx, string command)
        {
            try
            {
                string ident = command.Replace("api/navigraph/waypoint/", "").Trim('/').ToUpperInvariant();

                if (string.IsNullOrEmpty(ident))
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Waypoint identifier required\"}");
                    return;
                }

                if (_navigraphData?.IsDataAvailable != true)
                {
                    ctx.Response.StatusCode = 503;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Navigraph data not available\"}");
                    return;
                }

                var waypoint = _navigraphData.GetWaypoint(ident);
                if (waypoint == null)
                {
                    ctx.Response.StatusCode = 404;
                    await ResponseJsonAsync(ctx, $"{{\"error\":\"Waypoint {ident} not found\"}}");
                    return;
                }

                var response = new
                {
                    identifier = waypoint.Identifier,
                    name = waypoint.Name,
                    region = waypoint.Region,
                    latitude = waypoint.Latitude,
                    longitude = waypoint.Longitude,
                    type = waypoint.Type,
                    airacCycle = _navigraphData.CurrentAiracCycle
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Waypoint error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Get navaid information
        /// URL: /api/navigraph/navaid/{ident}
        /// </summary>
        private async Task HandleNavaidRequest(IHttpContext ctx, string command)
        {
            try
            {
                string ident = command.Replace("api/navigraph/navaid/", "").Trim('/').ToUpperInvariant();

                if (string.IsNullOrEmpty(ident))
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Navaid identifier required\"}");
                    return;
                }

                if (_navigraphData?.IsDataAvailable != true)
                {
                    ctx.Response.StatusCode = 503;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Navigraph data not available\"}");
                    return;
                }

                var navaid = _navigraphData.GetNavaid(ident);
                if (navaid == null)
                {
                    ctx.Response.StatusCode = 404;
                    await ResponseJsonAsync(ctx, $"{{\"error\":\"Navaid {ident} not found\"}}");
                    return;
                }

                var response = new
                {
                    identifier = navaid.Identifier,
                    name = navaid.Name,
                    type = navaid.Type,
                    frequency = navaid.Frequency,
                    latitude = navaid.Latitude,
                    longitude = navaid.Longitude,
                    region = navaid.Region,
                    elevation = navaid.Elevation,
                    airacCycle = _navigraphData.CurrentAiracCycle
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Navaid error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Get SIDs for an airport
        /// URL: /api/navigraph/sids/{icao}
        /// </summary>
        private async Task HandleSIDsRequest(IHttpContext ctx, string command)
        {
            KneeboardLogger.Navigraph($"HandleSIDsRequest called with command: {command}");
            try
            {
                string icao = command.Replace("api/navigraph/sids/", "").Trim('/').ToUpperInvariant();
                KneeboardLogger.Navigraph($"Getting SIDs for ICAO: {icao}");

                if (string.IsNullOrEmpty(icao))
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"ICAO code required\"}");
                    return;
                }

                if (_navigraphData?.IsDataAvailable != true)
                {
                    ctx.Response.StatusCode = 503;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Navigraph data not available\"}");
                    return;
                }

                var sids = _navigraphData.GetSIDs(icao);
                KneeboardLogger.Navigraph($"Retrieved {sids.Count} SIDs for {icao}");
                var response = sids.Select(s => new
                {
                    Identifier = s.Identifier,
                    Runway = s.Runway,
                    RouteType = s.RouteType,
                    TransitionIdentifier = s.TransitionIdentifier
                }).ToList();

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                KneeboardLogger.NavigraphDebug($"Sending SIDs response ({json.Length} bytes)");
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"SIDs error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Get STARs for an airport
        /// URL: /api/navigraph/stars/{icao}
        /// </summary>
        private async Task HandleSTARsRequest(IHttpContext ctx, string command)
        {
            try
            {
                string icao = command.Replace("api/navigraph/stars/", "").Trim('/').ToUpperInvariant();

                if (string.IsNullOrEmpty(icao))
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"ICAO code required\"}");
                    return;
                }

                if (_navigraphData?.IsDataAvailable != true)
                {
                    ctx.Response.StatusCode = 503;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Navigraph data not available\"}");
                    return;
                }

                var stars = _navigraphData.GetSTARs(icao);
                var response = stars.Select(s => new
                {
                    Identifier = s.Identifier,
                    Runway = s.Runway,
                    RouteType = s.RouteType,
                    TransitionIdentifier = s.TransitionIdentifier
                }).ToList();

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"STARs error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Get Approaches for an airport
        /// URL: /api/navigraph/approaches/{icao}
        /// </summary>
        private async Task HandleApproachesRequest(IHttpContext ctx, string command)
        {
            try
            {
                string icao = command.Replace("api/navigraph/approaches/", "").Trim('/').ToUpperInvariant();

                if (string.IsNullOrEmpty(icao))
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"ICAO code required\"}");
                    return;
                }

                if (_navigraphData?.IsDataAvailable != true)
                {
                    ctx.Response.StatusCode = 503;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Navigraph data not available\"}");
                    return;
                }

                var approaches = _navigraphData.GetApproaches(icao);
                var response = approaches.Select(a => new
                {
                    Identifier = a.Identifier,
                    Runway = a.Runway,
                    Type = a.Type.ToString(),
                    Transitions = a.Transitions
                }).ToList();

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Approaches error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Get procedure waypoints (SID, STAR, or Approach)
        /// URL: /api/navigraph/procedure/{icao}/{procedureName}?type=SID|STAR|APPROACH&transition=XXX
        /// </summary>
        private async Task HandleNavigraphProcedureRequest(IHttpContext ctx, string command)
        {
            try
            {
                // Parse URL: api/navigraph/procedure/{icao}/{procedureName}
                string path = command.Replace("api/navigraph/procedure/", "").Trim('/');
                string[] parts = path.Split('/');

                if (parts.Length < 2)
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"ICAO and procedure name required\"}");
                    return;
                }

                string icao = parts[0].ToUpperInvariant();
                string procedureName = System.Net.WebUtility.UrlDecode(parts[1]);

                // Get query parameters
                string typeParam = ctx.Request.QueryString["type"] ?? "SID";
                string transition = ctx.Request.QueryString["transition"] ?? "";
                string runway = ctx.Request.QueryString["runway"] ?? "";

                if (_navigraphData?.IsDataAvailable != true)
                {
                    ctx.Response.StatusCode = 503;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Navigraph data not available\"}");
                    return;
                }

                // Get procedure detail using existing wrapper method
                var procedureDetail = _navigraphData.GetProcedureDetail(icao, procedureName, transition, typeParam, runway);

                if (procedureDetail == null || procedureDetail.Waypoints == null)
                {
                    ctx.Response.StatusCode = 404;
                    await ResponseJsonAsync(ctx, $"{{\"error\":\"Procedure {procedureName} not found for {icao}\"}}");
                    return;
                }

                var response = new
                {
                    Icao = icao,
                    ProcedureName = procedureName,
                    Type = typeParam,
                    Transition = transition,
                    AiracCycle = procedureDetail.AiracCycle,
                    Waypoints = procedureDetail.Waypoints.Select(w => new
                    {
                        Identifier = w.Identifier,
                        Latitude = w.Latitude,
                        Longitude = w.Longitude,
                        Altitude1 = w.Altitude1,
                        Altitude2 = w.Altitude2,
                        AltitudeConstraint = w.AltitudeConstraint,
                        SpeedLimit = w.SpeedLimit,
                        PathTerminator = w.PathTerminator,
                        Overfly = w.Overfly,
                        SequenceNumber = w.SequenceNumber,
                        RouteType = w.RouteType,
                        TransitionIdentifier = w.TransitionIdentifier
                    }).ToList()
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Procedure error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Debug endpoint to get ALL raw approach legs (including vector legs with 0,0 coordinates)
        /// URL: /api/navigraph/approach-debug/{icao}/{procedureId}
        /// </summary>
        private async Task HandleApproachDebugRequest(IHttpContext ctx, string command)
        {
            try
            {
                string path = command.Replace("api/navigraph/approach-debug/", "").Trim('/');
                string[] parts = path.Split('/');

                if (parts.Length < 2)
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"ICAO and procedure ID required\"}");
                    return;
                }

                string icao = parts[0].ToUpperInvariant();
                string procedureId = parts[1];

                if (_navigraphData?.IsDataAvailable != true)
                {
                    ctx.Response.StatusCode = 503;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Navigraph data not available\"}");
                    return;
                }

                var legs = _navigraphData.GetRawApproachLegs(icao, procedureId);

                var response = new
                {
                    Icao = icao,
                    ProcedureId = procedureId,
                    TotalLegs = legs.Count,
                    Legs = legs
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Approach debug error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Test endpoint using exact GetProcedureLegs SQL query
        /// URL: /api/navigraph/approach-test/{icao}/{procedureId}
        /// </summary>
        private async Task HandleApproachTestRequest(IHttpContext ctx, string command)
        {
            try
            {
                string path = command.Replace("api/navigraph/approach-test/", "").Trim('/');
                string[] parts = path.Split('/');

                if (parts.Length < 2)
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"ICAO and procedure ID required\"}");
                    return;
                }

                string icao = parts[0].ToUpperInvariant();
                string procedureId = parts[1];

                if (_navigraphData?.IsDataAvailable != true)
                {
                    ctx.Response.StatusCode = 503;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Navigraph data not available\"}");
                    return;
                }

                var legs = _navigraphData.TestApproachQuery(icao, procedureId);

                var response = new
                {
                    Icao = icao,
                    ProcedureId = procedureId,
                    Query = "SELECT ... FROM tbl_pf_iaps WHERE ... AND route_type NOT IN ('A', 'Z')",
                    TotalLegs = legs.Count,
                    Legs = legs
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Approach test error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Direct test of GetProcedureLegs method
        /// URL: /api/navigraph/approach-legs/{icao}/{procedureId}
        /// </summary>
        private async Task HandleApproachLegsRequest(IHttpContext ctx, string command)
        {
            try
            {
                string path = command.Replace("api/navigraph/approach-legs/", "").Trim('/');
                string[] parts = path.Split('/');

                if (parts.Length < 2)
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"ICAO and procedure ID required\"}");
                    return;
                }

                string icao = parts[0].ToUpperInvariant();
                string procedureId = parts[1];

                if (_navigraphData?.IsDataAvailable != true)
                {
                    ctx.Response.StatusCode = 503;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Navigraph data not available\"}");
                    return;
                }

                var legs = _navigraphData.TestGetProcedureLegs(icao, procedureId);

                var response = new
                {
                    Icao = icao,
                    ProcedureId = procedureId,
                    Method = "GetProcedureLegs(Approach, no transition, no runway)",
                    TotalLegs = legs.Count,
                    Legs = legs.Select(l => new {
                        l.SequenceNumber,
                        l.WaypointIdentifier,
                        l.Latitude,
                        l.Longitude,
                        l.RouteType,
                        l.PathTerminator
                    }).ToList()
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Approach legs error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Diagnostic test of GetProcedureLegs with detailed debug output
        /// URL: /api/navigraph/approach-diag/{icao}/{procedureId}
        /// </summary>
        private async Task HandleApproachDiagRequest(IHttpContext ctx, string command)
        {
            try
            {
                string path = command.Replace("api/navigraph/approach-diag/", "").Trim('/');
                string[] parts = path.Split('/');

                if (parts.Length < 2)
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"ICAO and procedure ID required\"}");
                    return;
                }

                string icao = parts[0].ToUpperInvariant();
                string procedureId = parts[1];

                if (_navigraphData?.IsDataAvailable != true)
                {
                    ctx.Response.StatusCode = 503;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Navigraph data not available\"}");
                    return;
                }

                var (legs, debugLog) = _navigraphData.DiagnosticGetProcedureLegs(icao, procedureId);

                var response = new
                {
                    Icao = icao,
                    ProcedureId = procedureId,
                    TotalLegs = legs.Count,
                    DebugLog = debugLog,
                    Legs = legs.Select(l => new {
                        l.SequenceNumber,
                        l.WaypointIdentifier,
                        l.Latitude,
                        l.Longitude,
                        l.RouteType,
                        l.PathTerminator
                    }).ToList()
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.NavigraphError($"Approach diag error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Get debug configuration
        /// URL: GET /api/debug/config
        /// Returns: JSON with all debug flags and their status
        /// </summary>
        private async Task HandleGetDebugConfig(IHttpContext ctx)
        {
            try
            {
                var config = new
                {
                    DISABLE_ALL_LOGS = false,
                    MAP = true,
                    WIND = false,
                    CZ = false,
                    FREQ = false,
                    RUNWAY = true,
                    SIMCONNECT = true,
                    WAYPOINTS = true,
                    TELEPORT = false,
                    API = true,
                    WEATHER = false,
                    NAVLOG = false,
                    SIMBRIEF = true,
                    AIRPORTS = false
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
                await ResponseJsonAsync(ctx, json);
                Logging.KneeboardLogger.Debug("API", "Debug config retrieved");
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
                Logging.KneeboardLogger.Error("API", $"Error getting debug config: {ex.Message}");
            }
        }

        /// <summary>
        /// Set debug configuration
        /// URL: POST /api/debug/config
        /// Body: { "RUNWAY": true, "MAP": false, ... }
        /// Returns: Updated config
        /// </summary>
        private async Task HandleSetDebugConfig(IHttpContext ctx)
        {
            try
            {
                using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                {
                    string body = reader.ReadToEnd();
                    if (string.IsNullOrEmpty(body))
                    {
                        ctx.Response.StatusCode = 400;
                        await ResponseJsonAsync(ctx, "{\"error\":\"Empty request body\"}");
                        return;
                    }

                    var updates = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(body);
                    var response = new
                    {
                        status = "Debug config updated",
                        updates = updates
                    };

                    string responseJson = Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented);
                    await ResponseJsonAsync(ctx, responseJson);
                    
                    Logging.KneeboardLogger.Info("API", $"Debug config updated: {body}");
                }
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
                Logging.KneeboardLogger.Error("API", $"Error setting debug config: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle client log forwarding
        /// URL: POST /api/log
        /// Body: { "level": "INFO", "module": "Map", "message": "Log message" }
        /// </summary>
        private async Task HandleClientLogRequest(IHttpContext ctx)
        {
            try
            {
                using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                {
                    string body = reader.ReadToEnd();
                    if (string.IsNullOrEmpty(body))
                    {
                        ctx.Response.StatusCode = 400;
                        await ResponseJsonAsync(ctx, "{\"error\":\"Empty request body\"}");
                        return;
                    }

                    var logEntry = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(body);
                    string level = logEntry?.level?.ToString() ?? "INFO";
                    string module = logEntry?.module?.ToString() ?? "Client";
                    string message = logEntry?.message?.ToString() ?? "";

                    if (!string.IsNullOrEmpty(message))
                    {
                        Logging.KneeboardLogger.ClientLog(level, module, message);
                    }

                    await ResponseJsonAsync(ctx, "{\"success\":true}");
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.APIError($"Error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        // ============================================================================
        // OpenAIP Cache Methods
        // ============================================================================

        /// <summary>
        /// Gets the cache file path for a tile
        /// </summary>
        private static string GetTileCachePath(string tilePart)
        {
            // tilePart is like "10/532/341.png"
            return Path.Combine(CACHE_DIR, "tiles", tilePart.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// Gets the cache file path for API data
        /// </summary>
        private static string GetApiCachePath(string endpoint, string lat, string lng, string dist)
        {
            // Sanitize coordinates for filename (replace dots and minus signs)
            string safeLat = lat.Replace(".", "_").Replace("-", "n");
            string safeLng = lng.Replace(".", "_").Replace("-", "n");
            string safeDist = dist.Replace(".", "_");
            string filename = $"{endpoint}_{safeLat}_{safeLng}_{safeDist}.json";
            return Path.Combine(CACHE_DIR, "api", filename);
        }

        /// <summary>
        /// Checks if a cache file exists and is still valid (not expired)
        /// </summary>
        private static bool IsCacheValid(string cachePath)
        {
            if (!File.Exists(cachePath))
                return false;

            var fileInfo = new FileInfo(cachePath);
            return (DateTime.Now - fileInfo.LastWriteTime) < CACHE_TTL;
        }

        /// <summary>
        /// Reads data from cache file
        /// </summary>
        private static byte[] ReadFromCache(string cachePath)
        {
            lock (_cacheLock)
            {
                return File.ReadAllBytes(cachePath);
            }
        }

        /// <summary>
        /// Writes data to cache file, creating directories as needed
        /// Respects max cache size setting (0 = unlimited)
        /// </summary>
        private static void WriteToCache(string cachePath, byte[] data)
        {
            try
            {
                lock (_cacheLock)
                {
                    // Check max cache size before writing
                    long maxSizeMB = Properties.Settings.Default.maxCacheSizeMB;
                    if (maxSizeMB > 0)
                    {
                        long currentSize = GetCacheSizeInternal();
                        long maxSizeBytes = maxSizeMB * 1024 * 1024;

                        // If adding this file would exceed limit, clean up old files first
                        if (currentSize + data.Length > maxSizeBytes)
                        {
                            CleanupOldestCacheFiles(data.Length);
                        }
                    }

                    string directory = Path.GetDirectoryName(cachePath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.WriteAllBytes(cachePath, data);
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.CacheError($"Cache write error: {ex.Message}");
            }
        }

        /// <summary>
        /// Internal method to get cache size (assumes lock is held)
        /// </summary>
        private static long GetCacheSizeInternal()
        {
            if (!Directory.Exists(CACHE_DIR))
                return 0;

            return Directory.GetFiles(CACHE_DIR, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
        }

        /// <summary>
        /// Removes oldest cache files to make room for new data
        /// </summary>
        private static void CleanupOldestCacheFiles(long bytesNeeded)
        {
            if (!Directory.Exists(CACHE_DIR))
                return;

            try
            {
                long maxSizeMB = Properties.Settings.Default.maxCacheSizeMB;
                long maxSizeBytes = maxSizeMB * 1024 * 1024;
                long currentSize = GetCacheSizeInternal();
                long targetSize = maxSizeBytes - bytesNeeded - (10 * 1024 * 1024); // Leave 10MB buffer

                if (currentSize <= targetSize)
                    return;

                // Get all cache files sorted by last access time (oldest first)
                var files = Directory.GetFiles(CACHE_DIR, "*", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .OrderBy(f => f.LastAccessTime)
                    .ToList();

                long bytesFreed = 0;
                long bytesToFree = currentSize - targetSize;

                foreach (var file in files)
                {
                    if (bytesFreed >= bytesToFree)
                        break;

                    try
                    {
                        long fileSize = file.Length;
                        file.Delete();
                        bytesFreed += fileSize;
                    }
                    catch (Exception ex)
                    {
                        KneeboardLogger.CacheError($"Failed to delete file: {ex.Message}");
                    }
                }

                KneeboardLogger.Cache($"Cache cleanup: freed {bytesFreed / 1024 / 1024}MB");
            }
            catch (Exception ex)
            {
                KneeboardLogger.CacheError($"Cache cleanup error: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears the entire OpenAIP cache directory
        /// </summary>
        public static void ClearOpenAipCache()
        {
            lock (_cacheLock)
            {
                if (Directory.Exists(CACHE_DIR))
                {
                    Directory.Delete(CACHE_DIR, true);
                    KneeboardLogger.Cache("OpenAIP cache cleared");
                }
            }
        }

        /// <summary>
        /// Clears the in-memory boundaries cache (IVAO/VATSIM FIR boundaries)
        /// Forces reload from disk on next request
        /// </summary>
        public static void ClearBoundariesCache()
        {
            lock (_boundariesCacheLock)
            {
                _cachedIvaoBoundaries = null;
                _ivaoBoundariesCacheTime = DateTime.MinValue;
                _cachedVatsimBoundaries = null;
                _vatsimBoundariesCacheTime = DateTime.MinValue;
                _cachedVatsimTraconBoundaries = null;
                _vatsimTraconBoundariesCacheTime = DateTime.MinValue;
                _preprocessedIvaoBoundaries = null;
                _preprocessedIvaoBoundariesTime = DateTime.MinValue;
                _preprocessedVatsimBoundaries = null;
                _preprocessedVatsimBoundariesTime = DateTime.MinValue;
                KneeboardLogger.Boundaries("Boundaries memory cache cleared - will reload from disk on next request");
            }
        }

        /// <summary>
        /// Clears the elevation cache (both memory and file cache)
        /// Note: Does NOT delete SRTM data files
        /// </summary>
        public static void ClearElevationCache()
        {
            // Clear memory cache
            lock (_elevationCacheLock)
            {
                _elevationCache.Clear();
            }

            // Clear file cache directory
            if (Directory.Exists(_elevationCacheDir))
            {
                try
                {
                    Directory.Delete(_elevationCacheDir, true);
                    KneeboardLogger.Cache("Elevation cache cleared");
                }
                catch (Exception ex)
                {
                    KneeboardLogger.CacheError($"Error clearing elevation cache: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets the current cache size in bytes (OpenAIP + Elevation)
        /// </summary>
        public static long GetCacheSize()
        {
            long totalSize = 0;

            // OpenAIP cache
            if (Directory.Exists(CACHE_DIR))
            {
                totalSize += Directory.GetFiles(CACHE_DIR, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
            }

            // Elevation cache
            if (Directory.Exists(_elevationCacheDir))
            {
                totalSize += Directory.GetFiles(_elevationCacheDir, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
            }

            return totalSize;
        }

        // ===== HYBRID-ANSATZ: Server-seitige Piloten-Klassifizierung =====

        /// <summary>
        /// Bestimmt die Aircraft-Kategorie basierend auf dem ICAO-Type
        /// </summary>
        private string GetAircraftCategory(string aircraftType)
        {
            if (string.IsNullOrEmpty(aircraftType)) return "N";

            // Extrahiere ICAO-Type (erste 4 Zeichen nach Slash oder der Type selbst)
            string icaoType = aircraftType;
            if (aircraftType.Contains("/"))
            {
                var parts = aircraftType.Split('/');
                if (parts.Length > 1)
                    icaoType = parts[1].Length > 4 ? parts[1].Substring(0, 4) : parts[1];
            }
            else if (icaoType.Length > 4)
            {
                icaoType = icaoType.Substring(0, 4);
            }

            icaoType = icaoType.Trim().ToUpperInvariant();

            // Kategorie bestimmen
            if (HELI_TYPES.Contains(icaoType) || icaoType.StartsWith("H") && icaoType.Length <= 4) return "R"; // Rotorcraft
            if (SUPER_HEAVY_TYPES.Contains(icaoType)) return "J"; // Super/Jumbo
            if (HEAVY_TYPES.Contains(icaoType)) return "H"; // Heavy
            if (TURBOPROP_TYPES.Contains(icaoType)) return "M"; // Medium/Turboprop

            // Standard Jets (default)
            if (icaoType.StartsWith("A3") || icaoType.StartsWith("B7") || icaoType.StartsWith("E") ||
                icaoType.StartsWith("CRJ") || icaoType.StartsWith("E1") || icaoType.StartsWith("E2") ||
                icaoType.StartsWith("A2") || icaoType.StartsWith("B73"))
            {
                return "N"; // Normal Jet
            }

            // Light aircraft
            if (icaoType.StartsWith("C1") || icaoType.StartsWith("PA") || icaoType.StartsWith("BE") ||
                icaoType.StartsWith("SR") || icaoType.StartsWith("DA") || icaoType.StartsWith("P28"))
            {
                return "L"; // Light
            }

            return "N"; // Default: Normal/Jet
        }

        /// <summary>
        /// Prüft ob ein Flugzeug militärisch ist
        /// </summary>
        private bool IsMilitaryAircraft(string callsign, string aircraftType)
        {
            if (string.IsNullOrEmpty(callsign)) return false;

            // Prüfe Callsign-Präfix
            string prefix = callsign.Length >= 3 ? callsign.Substring(0, 3).ToUpperInvariant() : callsign.ToUpperInvariant();
            string prefix4 = callsign.Length >= 4 ? callsign.Substring(0, 4).ToUpperInvariant() : prefix;
            string prefix5 = callsign.Length >= 5 ? callsign.Substring(0, 5).ToUpperInvariant() : prefix4;

            if (MILITARY_PREFIXES.Contains(prefix) || MILITARY_PREFIXES.Contains(prefix4) || MILITARY_PREFIXES.Contains(prefix5))
                return true;

            // Prüfe Aircraft-Type
            if (!string.IsNullOrEmpty(aircraftType))
            {
                string icaoType = aircraftType;
                if (aircraftType.Contains("/"))
                {
                    var parts = aircraftType.Split('/');
                    if (parts.Length > 1)
                        icaoType = parts[1].Length > 4 ? parts[1].Substring(0, 4) : parts[1];
                }
                icaoType = icaoType.Trim().ToUpperInvariant();

                if (MILITARY_AIRCRAFT_TYPES.Contains(icaoType))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Prüft ob ein Flug ein Santa/Weihnachtsflug ist
        /// IVAO: Callsign enthält "SANTA"
        /// VATSIM: Callsign enthält "SANTA" oder "XMAS"
        /// </summary>
        private bool IsSantaFlight(string callsign)
        {
            if (string.IsNullOrEmpty(callsign)) return false;
            string upper = callsign.ToUpperInvariant();
            return upper.Contains("SANTA") || upper.Contains("XMAS") || upper.Contains("HOHO") || upper.Contains("SLEIGH");
        }

        /// <summary>
        /// Holt und cached VATSIM Piloten-Daten mit Server-seitiger Vorverarbeitung
        /// </summary>
        private async Task HandleVatsimPilots(IHttpContext ctx)
        {
            try
            {
                string pilotsJson;

                // Check cache
                string cachedData = null;
                lock (_pilotsCacheLock)
                {
                    if (_cachedVatsimPilots != null && (DateTime.Now - _vatsimPilotsCacheTime) < PILOTS_CACHE_TTL)
                    {
                        cachedData = _cachedVatsimPilots;
                    }
                }

                if (cachedData != null)
                {
                    ctx.Response.Headers.Add("X-Cache", "HIT");
                    await ResponseJsonAsync(ctx, cachedData);
                    return;
                }

                // Fetch fresh data
                using (var client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    string rawData = client.DownloadString("https://data.vatsim.net/v3/vatsim-data.json");

                    // Parse und vorverarbeiten mit Newtonsoft.Json
                    dynamic jsonObj = Newtonsoft.Json.JsonConvert.DeserializeObject(rawData);
                    var pilots = jsonObj.pilots;

                    var processedPilots = new List<object>();
                    foreach (var pilot in pilots)
                    {
                        string callsign = (string)pilot.callsign ?? "";
                        string cid = pilot.cid != null ? pilot.cid.ToString() : "";  // VATSIM CID for favorites
                        string pilotName = (string)pilot.name ?? "";  // Pilot's real name
                        string aircraft = "";
                        if (pilot.flight_plan != null)
                        {
                            aircraft = (string)pilot.flight_plan.aircraft_short ?? "";
                        }

                        double lat = (double?)pilot.latitude ?? 0;
                        double lon = (double?)pilot.longitude ?? 0;
                        int heading = (int?)pilot.heading ?? 0;
                        int altitude = (int?)pilot.altitude ?? 0;
                        int groundspeed = (int?)pilot.groundspeed ?? 0;

                        string departure = "";
                        string arrival = "";
                        if (pilot.flight_plan != null)
                        {
                            departure = (string)pilot.flight_plan.departure ?? "";
                            arrival = (string)pilot.flight_plan.arrival ?? "";
                        }

                        // Server-seitige Klassifizierung
                        string category = GetAircraftCategory(aircraft);
                        bool military = IsMilitaryAircraft(callsign, aircraft);
                        bool santa = IsSantaFlight(callsign);

                        // Spezial: KFR im Callsign bekommt Eisvogel-Icon (Kategorie B)
                        string upperCs = callsign?.ToUpperInvariant() ?? "";
                        if (upperCs.Contains("KFR"))
                        {
                            category = "B";  // Bird/Kingfisher Icon
                        }

                        processedPilots.Add(new
                        {
                            callsign = callsign,
                            id = cid,                 // VATSIM CID for favorites matching
                            name = pilotName,         // Pilot's real name
                            latitude = lat,
                            longitude = lon,
                            heading = heading,
                            altitude = altitude,
                            groundspeed = groundspeed,
                            aircraft = aircraft,
                            departure = departure,
                            arrival = arrival,
                            category = category,      // Vorberechnet! (B für KFR)
                            military = military,      // Vorberechnet!
                            santa = santa             // Weihnachtsflug!
                        });
                    }

                    pilotsJson = Newtonsoft.Json.JsonConvert.SerializeObject(processedPilots);

                    // Cache aktualisieren
                    lock (_pilotsCacheLock)
                    {
                        _cachedVatsimPilots = pilotsJson;
                        _vatsimPilotsCacheTime = DateTime.Now;
                    }
                }

                ctx.Response.Headers.Add("X-Cache", "MISS");
                await ResponseJsonAsync(ctx, pilotsJson);
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"VATSIM pilots error: {ex.Message}");
                ctx.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                // EmbedIO manages stream lifecycle
            }
        }

        /// <summary>
        /// Holt und cached IVAO Piloten-Daten mit Server-seitiger Vorverarbeitung
        /// </summary>
        private async Task HandleIvaoPilots(IHttpContext ctx)
        {
            try
            {
                string pilotsJson;

                // Check cache
                string cachedData = null;
                lock (_pilotsCacheLock)
                {
                    if (_cachedIvaoPilots != null && (DateTime.Now - _ivaoPilotsCacheTime) < PILOTS_CACHE_TTL)
                    {
                        cachedData = _cachedIvaoPilots;
                    }
                }

                if (cachedData != null)
                {
                    ctx.Response.Headers.Add("X-Cache", "HIT");
                    await ResponseJsonAsync(ctx, cachedData);
                    return;
                }

                // Fetch fresh data
                using (var client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    string rawData = client.DownloadString("https://api.ivao.aero/v2/tracker/whazzup");

                    // Parse und vorverarbeiten mit Newtonsoft.Json
                    dynamic jsonObj = Newtonsoft.Json.JsonConvert.DeserializeObject(rawData);
                    var pilots = jsonObj.clients.pilots;

                    var processedPilots = new List<object>();
                    foreach (var pilot in pilots)
                    {
                        string callsign = (string)pilot.callsign ?? "";
                        string oduserId = pilot.userId != null ? pilot.userId.ToString() : "";  // IVAO userId for favorites

                        double lat = 0, lon = 0;
                        int heading = 0, altitude = 0, groundspeed = 0;
                        if (pilot.lastTrack != null)
                        {
                            lat = (double?)pilot.lastTrack.latitude ?? 0;
                            lon = (double?)pilot.lastTrack.longitude ?? 0;
                            heading = (int?)pilot.lastTrack.heading ?? 0;
                            altitude = (int?)pilot.lastTrack.altitude ?? 0;
                            groundspeed = (int?)pilot.lastTrack.groundSpeed ?? 0;
                        }

                        string aircraft = "";
                        string departure = "";
                        string arrival = "";
                        if (pilot.flightPlan != null)
                        {
                            aircraft = (string)pilot.flightPlan.aircraftId ?? "";
                            departure = (string)pilot.flightPlan.departureId ?? "";
                            arrival = (string)pilot.flightPlan.arrivalId ?? "";
                        }

                        // Server-seitige Klassifizierung
                        string category = GetAircraftCategory(aircraft);
                        bool military = IsMilitaryAircraft(callsign, aircraft);
                        bool santa = IsSantaFlight(callsign);

                        // Spezial: KFR im Callsign bekommt Eisvogel-Icon (Kategorie B)
                        string upperCs = callsign?.ToUpperInvariant() ?? "";
                        if (upperCs.Contains("KFR"))
                        {
                            category = "B";  // Bird/Kingfisher Icon
                        }

                        processedPilots.Add(new
                        {
                            callsign = callsign,
                            id = oduserId,            // IVAO userId for favorites matching
                            name = "",                // IVAO API doesn't provide pilot names
                            latitude = lat,
                            longitude = lon,
                            heading = heading,
                            altitude = altitude,
                            groundspeed = groundspeed,
                            aircraft = aircraft,
                            departure = departure,
                            arrival = arrival,
                            category = category,      // Vorberechnet! (B für KFR)
                            military = military,      // Vorberechnet!
                            santa = santa             // Weihnachtsflug!
                        });
                    }

                    pilotsJson = Newtonsoft.Json.JsonConvert.SerializeObject(processedPilots);

                    // Cache aktualisieren
                    lock (_pilotsCacheLock)
                    {
                        _cachedIvaoPilots = pilotsJson;
                        _ivaoPilotsCacheTime = DateTime.Now;
                    }
                }

                ctx.Response.Headers.Add("X-Cache", "MISS");
                await ResponseJsonAsync(ctx, pilotsJson);
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"IVAO pilots error: {ex.Message}");
                ctx.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                // EmbedIO manages stream lifecycle
            }
        }

        // ===== HYBRID-ANSATZ: GeoJSON mit vorberechneten Bounding-Boxes =====

        /// <summary>
        /// Berechnet die Bounding-Box für ein GeoJSON Feature (Newtonsoft.Json Version)
        /// </summary>
        private double[] CalculateBoundingBox(Newtonsoft.Json.Linq.JToken coordinates, string geometryType)
        {
            double minLat = double.MaxValue, maxLat = double.MinValue;
            double minLng = double.MaxValue, maxLng = double.MinValue;

            void ProcessPoint(Newtonsoft.Json.Linq.JToken point)
            {
                if (point is Newtonsoft.Json.Linq.JArray arr && arr.Count >= 2)
                {
                    double lng = (double)arr[0];
                    double lat = (double)arr[1];
                    if (lng < minLng) minLng = lng;
                    if (lng > maxLng) maxLng = lng;
                    if (lat < minLat) minLat = lat;
                    if (lat > maxLat) maxLat = lat;
                }
            }

            void ProcessRing(Newtonsoft.Json.Linq.JToken ring)
            {
                if (ring is Newtonsoft.Json.Linq.JArray arr)
                {
                    foreach (var point in arr)
                    {
                        ProcessPoint(point);
                    }
                }
            }

            void ProcessPolygon(Newtonsoft.Json.Linq.JToken polygon)
            {
                if (polygon is Newtonsoft.Json.Linq.JArray arr)
                {
                    foreach (var ring in arr)
                    {
                        ProcessRing(ring);
                    }
                }
            }

            try
            {
                if (geometryType == "Polygon")
                {
                    ProcessPolygon(coordinates);
                }
                else if (geometryType == "MultiPolygon")
                {
                    if (coordinates is Newtonsoft.Json.Linq.JArray arr)
                    {
                        foreach (var polygon in arr)
                        {
                            ProcessPolygon(polygon);
                        }
                    }
                }
            }
            catch
            {
                return null;
            }

            if (minLat == double.MaxValue || maxLat == double.MinValue)
                return null;

            return new double[] { minLat, maxLat, minLng, maxLng };
        }

        /// <summary>
        /// Verarbeitet GeoJSON und fügt Bounding-Boxes hinzu (Newtonsoft.Json Version)
        /// </summary>
        private string PreprocessGeoJsonWithBoundingBoxes(string geoJson)
        {
            try
            {
                var jsonObj = Newtonsoft.Json.Linq.JObject.Parse(geoJson);
                var features = jsonObj["features"] as Newtonsoft.Json.Linq.JArray;

                if (features == null)
                    return geoJson;

                foreach (var feature in features)
                {
                    var geometry = feature["geometry"] as Newtonsoft.Json.Linq.JObject;
                    if (geometry != null)
                    {
                        var coords = geometry["coordinates"];
                        var geomType = (string)geometry["type"];

                        if (coords != null && geomType != null)
                        {
                            // SCHRITT 1: Geometrie-Koordinaten reparieren (Antimeridian-Crossings)
                            var fixedCoords = FixPolygonCoordinates(coords, geomType);
                            if (fixedCoords != null)
                            {
                                geometry["coordinates"] = fixedCoords;
                                coords = fixedCoords;
                            }

                            // SCHRITT 2: Bounding-Box berechnen
                            var bbox = CalculateBoundingBox(coords, geomType);
                            if (bbox != null)
                            {
                                geometry["bbox"] = new Newtonsoft.Json.Linq.JArray(bbox);
                            }
                        }
                    }
                }

                return jsonObj.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"GeoJSON preprocessing error: {ex.Message}");
                return geoJson; // Fallback auf Original
            }
        }

        /// <summary>
        /// Korrigiert Polygon-Koordinaten: Antimeridian-Crossings und fehlerhafte Punkte
        /// </summary>
        private Newtonsoft.Json.Linq.JToken FixPolygonCoordinates(Newtonsoft.Json.Linq.JToken coords, string geomType)
        {
            try
            {
                if (geomType == "Polygon")
                {
                    var rings = coords as Newtonsoft.Json.Linq.JArray;
                    if (rings == null || rings.Count == 0) return null;

                    var fixedRings = new Newtonsoft.Json.Linq.JArray();
                    foreach (var ring in rings)
                    {
                        var fixedRing = FixPolygonRing(ring as Newtonsoft.Json.Linq.JArray);
                        if (fixedRing != null && fixedRing.Count >= 4)
                        {
                            fixedRings.Add(fixedRing);
                        }
                    }
                    return fixedRings.Count > 0 ? fixedRings : null;
                }
                else if (geomType == "MultiPolygon")
                {
                    var polygons = coords as Newtonsoft.Json.Linq.JArray;
                    if (polygons == null) return null;

                    var fixedPolygons = new Newtonsoft.Json.Linq.JArray();
                    foreach (var polygon in polygons)
                    {
                        var rings = polygon as Newtonsoft.Json.Linq.JArray;
                        if (rings == null || rings.Count == 0) continue;

                        var fixedRings = new Newtonsoft.Json.Linq.JArray();
                        foreach (var ring in rings)
                        {
                            var fixedRing = FixPolygonRing(ring as Newtonsoft.Json.Linq.JArray);
                            if (fixedRing != null && fixedRing.Count >= 4)
                            {
                                fixedRings.Add(fixedRing);
                            }
                        }
                        if (fixedRings.Count > 0)
                        {
                            fixedPolygons.Add(fixedRings);
                        }
                    }
                    return fixedPolygons.Count > 0 ? fixedPolygons : null;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Korrigiert einen einzelnen Polygon-Ring: Normalisiert Koordinaten und behebt Antimeridian-Crossings
        /// </summary>
        private Newtonsoft.Json.Linq.JArray FixPolygonRing(Newtonsoft.Json.Linq.JArray ring)
        {
            if (ring == null || ring.Count < 4) return null;

            var fixedRing = new Newtonsoft.Json.Linq.JArray();
            double? prevLon = null;
            double? prevLat = null;
            double longitudeOffset = 0;

            foreach (var coord in ring)
            {
                var coordArray = coord as Newtonsoft.Json.Linq.JArray;
                if (coordArray == null || coordArray.Count < 2) continue;

                double lon = (double)coordArray[0];
                double lat = (double)coordArray[1];

                // Latitude auf gültigen Bereich klemmen
                if (lat > 90) lat = 90;
                if (lat < -90) lat = -90;

                // Longitude auf -180 bis 180 normalisieren
                while (lon > 180) lon -= 360;
                while (lon < -180) lon += 360;

                if (prevLon.HasValue)
                {
                    double lonDiff = lon - prevLon.Value;
                    double latDiff = Math.Abs(lat - prevLat.Value);

                    // Antimeridian-Crossing erkennen und korrigieren
                    if (lonDiff > 180)
                    {
                        // Sprung von Ost (~180°) nach West (~-180°)
                        longitudeOffset -= 360;
                    }
                    else if (lonDiff < -180)
                    {
                        // Sprung von West (~-180°) nach Ost (~180°)
                        longitudeOffset += 360;
                    }

                    // Fehlerhafte Punkte mit riesigen Lat-Sprüngen überspringen
                    if (latDiff > 60)
                    {
                        continue; // Punkt überspringen
                    }
                }

                double correctedLon = lon + longitudeOffset;
                prevLon = correctedLon;
                prevLat = lat;

                fixedRing.Add(new Newtonsoft.Json.Linq.JArray(correctedLon, lat));
            }

            // Polygon schließen wenn nötig
            if (fixedRing.Count >= 3)
            {
                var first = fixedRing[0] as Newtonsoft.Json.Linq.JArray;
                var last = fixedRing[fixedRing.Count - 1] as Newtonsoft.Json.Linq.JArray;
                if (first != null && last != null)
                {
                    double firstLon = (double)first[0];
                    double firstLat = (double)first[1];
                    double lastLon = (double)last[0];
                    double lastLat = (double)last[1];

                    // Closing-Jump korrigieren
                    double closingLonDiff = lastLon - firstLon;
                    if (Math.Abs(closingLonDiff) > 180)
                    {
                        if (closingLonDiff > 180)
                            lastLon -= 360;
                        else if (closingLonDiff < -180)
                            lastLon += 360;
                        fixedRing[fixedRing.Count - 1] = new Newtonsoft.Json.Linq.JArray(lastLon, lastLat);
                    }

                    if (firstLon != lastLon || firstLat != lastLat)
                    {
                        fixedRing.Add(new Newtonsoft.Json.Linq.JArray(firstLon, firstLat));
                    }
                }
            }

            return fixedRing;
        }

        /// <summary>
        /// Liefert VATSIM Boundaries mit vorberechneten Bounding-Boxes
        /// </summary>
        private async Task HandleVatsimBoundariesWithBbox(IHttpContext ctx)
        {
            try
            {
                // Check preprocessed cache
                string cachedData = null;
                lock (_boundariesCacheLock)
                {
                    if (_preprocessedVatsimBoundaries != null && (DateTime.Now - _preprocessedVatsimBoundariesTime) < BOUNDARIES_CACHE_TTL)
                    {
                        cachedData = _preprocessedVatsimBoundaries;
                    }
                }

                if (cachedData != null)
                {
                    ctx.Response.Headers.Add("X-Cache", "HIT");
                    await ResponseJsonAsync(ctx, cachedData);
                    return;
                }

                // Get raw boundaries (from existing cache or fetch)
                string rawBoundaries = null;
                lock (_boundariesCacheLock)
                {
                    if (_cachedVatsimBoundaries != null && (DateTime.Now - _vatsimBoundariesCacheTime) < BOUNDARIES_CACHE_TTL)
                    {
                        rawBoundaries = _cachedVatsimBoundaries;
                    }
                }

                if (rawBoundaries == null)
                {
                    // Fetch from GitHub
                    using (var client = new WebClient())
                    {
                        client.Encoding = Encoding.UTF8;
                        client.Headers.Add(HttpRequestHeader.UserAgent, "KneeboardServer/1.0");
                        rawBoundaries = client.DownloadString("https://raw.githubusercontent.com/vatsimnetwork/vatspy-data-project/master/Boundaries.geojson");

                        lock (_boundariesCacheLock)
                        {
                            _cachedVatsimBoundaries = rawBoundaries;
                            _vatsimBoundariesCacheTime = DateTime.Now;
                        }
                    }
                }

                // Preprocess with bounding boxes
                string preprocessed = PreprocessGeoJsonWithBoundingBoxes(rawBoundaries);

                lock (_boundariesCacheLock)
                {
                    _preprocessedVatsimBoundaries = preprocessed;
                    _preprocessedVatsimBoundariesTime = DateTime.Now;
                }

                KneeboardLogger.Boundaries("VATSIM Boundaries: preprocessed with bounding boxes");
                ctx.Response.Headers.Add("X-Cache", "PREPROCESSED");
                await ResponseJsonAsync(ctx, preprocessed);
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"VATSIM Boundaries preprocessing error: {ex.Message}");
                ctx.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                // EmbedIO manages stream lifecycle
            }
        }

        /// <summary>
        /// Liefert vereinfachten VATSIM Offline-FIR-Layer
        /// Generiert aus VATSIM Boundaries.geojson, gefiltert auf FIR-Grenzen
        /// </summary>
        private async Task HandleVatsimOfflineFir(IHttpContext ctx)
        {
            try
            {
                string offlineFilePath = Path.Combine(BOUNDARIES_CACHE_DIR, "vatsim_offline_fir.json");

                if (File.Exists(offlineFilePath))
                {
                    string content = File.ReadAllText(offlineFilePath);
                    ctx.Response.Headers.Add("X-Cache", "STATIC");
                    await ResponseJsonAsync(ctx, content);
                    return;
                }

                // Fallback: Generiere Offline-FIR aus gecachten VATSIM-Boundaries
                string vatsimCachePath = Path.Combine(BOUNDARIES_CACHE_DIR, "vatsim_boundaries.json");
                string vatsimDataPath = Path.Combine(BOUNDARIES_DATA_DIR, "vatsim_boundaries.json");

                string rawBoundaries = null;
                if (File.Exists(vatsimCachePath))
                {
                    rawBoundaries = File.ReadAllText(vatsimCachePath);
                }
                else if (File.Exists(vatsimDataPath))
                {
                    rawBoundaries = File.ReadAllText(vatsimDataPath);
                }

                if (!string.IsNullOrEmpty(rawBoundaries))
                {
                    KneeboardLogger.Boundaries("VATSIM Offline FIR: generating from cached boundaries...");
                    string offlineFir = GenerateVatsimOfflineFirFromBoundaries(rawBoundaries);

                    // Cache für zukünftige Requests
                    try
                    {
                        Directory.CreateDirectory(BOUNDARIES_CACHE_DIR);
                        File.WriteAllText(offlineFilePath, offlineFir);
                        KneeboardLogger.Boundaries("VATSIM Offline FIR: saved to cache");
                    }
                    catch (Exception cacheEx)
                    {
                        KneeboardLogger.Boundaries($"VATSIM Offline FIR: could not save cache: {cacheEx.Message}");
                    }

                    ctx.Response.Headers.Add("X-Cache", "GENERATED");
                    await ResponseJsonAsync(ctx, offlineFir);
                    return;
                }

                // Letzter Fallback: leere FeatureCollection
                KneeboardLogger.Boundaries("VATSIM Offline FIR: no source data available, returning empty collection");
                await ResponseJsonAsync(ctx, "{\"type\":\"FeatureCollection\",\"features\":[]}");
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"VATSIM Offline FIR error: {ex.Message}");
                ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                // EmbedIO manages stream lifecycle
            }
        }

        /// <summary>
        /// Generiert eine vereinfachte Offline-FIR-Sammlung aus den vollständigen VATSIM-Boundaries.
        /// Filtert nur FIR-Polygone und extrahiert ein Feature pro ICAO-Prefix.
        /// </summary>
        private string GenerateVatsimOfflineFirFromBoundaries(string rawBoundaries)
        {
            var features = new List<string>();
            var seenPrefixes = new HashSet<string>();

            try
            {
                // Parse JSON manuell (wie in anderen Methoden)
                int featuresStart = rawBoundaries.IndexOf("\"features\"");
                if (featuresStart == -1) return "{\"type\":\"FeatureCollection\",\"features\":[]}";

                int arrayStart = rawBoundaries.IndexOf('[', featuresStart);
                if (arrayStart == -1) return "{\"type\":\"FeatureCollection\",\"features\":[]}";

                // Finde alle Features
                int searchPos = arrayStart;
                int featureCount = 0;

                while (searchPos < rawBoundaries.Length && featureCount < 500)
                {
                    int featureStart = rawBoundaries.IndexOf("{\"type\":\"Feature\"", searchPos);
                    if (featureStart == -1) break;

                    // Finde das Ende dieses Features
                    int braceCount = 0;
                    int featureEnd = featureStart;
                    for (int i = featureStart; i < rawBoundaries.Length; i++)
                    {
                        if (rawBoundaries[i] == '{') braceCount++;
                        else if (rawBoundaries[i] == '}') braceCount--;

                        if (braceCount == 0)
                        {
                            featureEnd = i + 1;
                            break;
                        }
                    }

                    string featureJson = rawBoundaries.Substring(featureStart, featureEnd - featureStart);

                    // VATSIM Boundaries haben "id" als ICAO-Prefix (z.B. "EDGG", "KZLA")
                    // Jedes Feature ist eine FIR-Grenze
                    string prefix = ExtractVatsimPrefixFromFeature(featureJson);

                    // Nur ein Feature pro Prefix (verhindert Duplikate)
                    if (!string.IsNullOrEmpty(prefix) && !seenPrefixes.Contains(prefix))
                    {
                        seenPrefixes.Add(prefix);

                        // Füge zusätzliche Properties für Kompatibilität hinzu
                        // Das Feature muss "prefix" und "position" haben für das Frontend
                        string enhancedFeature = featureJson;

                        // Wenn "prefix" nicht existiert, füge es hinzu
                        if (!featureJson.Contains("\"prefix\""))
                        {
                            int propertiesEnd = featureJson.LastIndexOf("}");
                            int geometryStart = featureJson.IndexOf("\"geometry\"");
                            if (geometryStart > 0)
                            {
                                int propertiesBlockEnd = featureJson.LastIndexOf("}", geometryStart - 1);
                                if (propertiesBlockEnd > 0)
                                {
                                    enhancedFeature = featureJson.Substring(0, propertiesBlockEnd) +
                                                     $",\"prefix\":\"{prefix}\",\"position\":\"CTR\"" +
                                                     featureJson.Substring(propertiesBlockEnd);
                                }
                            }
                        }

                        features.Add(enhancedFeature);
                        featureCount++;
                    }

                    searchPos = featureEnd;
                }

                KneeboardLogger.Boundaries($"VATSIM Offline FIR: extracted {features.Count} unique FIR zones");
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"VATSIM Offline FIR generation error: {ex.Message}");
            }

            return "{\"type\":\"FeatureCollection\",\"features\":[" + string.Join(",", features) + "]}";
        }

        /// <summary>
        /// Extrahiert den ICAO-Prefix aus einem VATSIM-Feature-JSON
        /// VATSIM hat das Format: { "properties": { "id": "EDGG", ... }, ... }
        /// </summary>
        private string ExtractVatsimPrefixFromFeature(string featureJson)
        {
            // VATSIM Boundaries haben direkt "id" als ICAO-Code
            int idStart = featureJson.IndexOf("\"id\"");
            if (idStart != -1)
            {
                int colonPos = featureJson.IndexOf(':', idStart);
                if (colonPos != -1)
                {
                    int quoteStart = featureJson.IndexOf('"', colonPos + 1);
                    int quoteEnd = featureJson.IndexOf('"', quoteStart + 1);
                    if (quoteStart != -1 && quoteEnd != -1)
                    {
                        string id = featureJson.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                        // VATSIM IDs sind ICAO-Codes (4 Buchstaben)
                        if (!string.IsNullOrEmpty(id) && id.Length >= 3 && id.Length <= 5)
                        {
                            return id.ToUpperInvariant();
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Liefert vereinfachten IVAO Offline-FIR-Layer (165 FIRs statt 4103 Features)
        /// Reduziert Serverlast um ~96%
        /// </summary>
        private async Task HandleIvaoOfflineFir(IHttpContext ctx)
        {
            try
            {
                string offlineFilePath = Path.Combine(BOUNDARIES_CACHE_DIR, "ivao_offline_fir.json");

                if (File.Exists(offlineFilePath))
                {
                    string content = File.ReadAllText(offlineFilePath);
                    ctx.Response.Headers.Add("X-Cache", "STATIC");
                    ctx.Response.Headers.Add("X-FIR-Count", "165");
                    await ResponseJsonAsync(ctx, content);
                    return;
                }

                // Fallback: Generiere Offline-FIR aus gecachten IVAO-Boundaries
                string ivaoCachePath = Path.Combine(BOUNDARIES_CACHE_DIR, "ivao_boundaries_geojson.json");
                if (File.Exists(ivaoCachePath))
                {
                    KneeboardLogger.Boundaries("IVAO Offline FIR: generating from cached boundaries...");
                    string rawBoundaries = File.ReadAllText(ivaoCachePath);
                    string offlineFir = GenerateOfflineFirFromBoundaries(rawBoundaries);

                    // Cache für zukünftige Requests
                    try
                    {
                        Directory.CreateDirectory(BOUNDARIES_CACHE_DIR);
                        File.WriteAllText(offlineFilePath, offlineFir);
                        KneeboardLogger.Boundaries("IVAO Offline FIR: saved to cache");
                    }
                    catch (Exception cacheEx)
                    {
                        KneeboardLogger.Boundaries($"IVAO Offline FIR: could not save cache: {cacheEx.Message}");
                    }

                    ctx.Response.Headers.Add("X-Cache", "GENERATED");
                    await ResponseJsonAsync(ctx, offlineFir);
                    return;
                }

                // Letzter Fallback: leere FeatureCollection
                KneeboardLogger.Boundaries("IVAO Offline FIR: no source data available, returning empty collection");
                await ResponseJsonAsync(ctx, "{\"type\":\"FeatureCollection\",\"features\":[]}");
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"IVAO Offline FIR error: {ex.Message}");
                ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                // EmbedIO manages stream lifecycle
            }
        }

        /// <summary>
        /// Generiert eine vereinfachte Offline-FIR-Sammlung aus den vollständigen IVAO-Boundaries.
        /// Filtert nur CTR-Zonen und gruppiert sie nach ICAO-Prefix.
        /// </summary>
        private string GenerateOfflineFirFromBoundaries(string rawBoundaries)
        {
            var features = new List<string>();
            var seenPrefixes = new HashSet<string>();

            try
            {
                // Parse JSON manuell (wie in anderen Methoden)
                int featuresStart = rawBoundaries.IndexOf("\"features\"");
                if (featuresStart == -1) return "{\"type\":\"FeatureCollection\",\"features\":[]}";

                int arrayStart = rawBoundaries.IndexOf('[', featuresStart);
                if (arrayStart == -1) return "{\"type\":\"FeatureCollection\",\"features\":[]}";

                // Finde alle Features mit position=CTR oder FIR
                int searchPos = arrayStart;
                int featureCount = 0;

                while (searchPos < rawBoundaries.Length && featureCount < 500)
                {
                    int featureStart = rawBoundaries.IndexOf("{\"type\":\"Feature\"", searchPos);
                    if (featureStart == -1) break;

                    // Finde das Ende dieses Features
                    int braceCount = 0;
                    int featureEnd = featureStart;
                    for (int i = featureStart; i < rawBoundaries.Length; i++)
                    {
                        if (rawBoundaries[i] == '{') braceCount++;
                        else if (rawBoundaries[i] == '}') braceCount--;

                        if (braceCount == 0)
                        {
                            featureEnd = i + 1;
                            break;
                        }
                    }

                    string featureJson = rawBoundaries.Substring(featureStart, featureEnd - featureStart);

                    // Prüfe ob es eine CTR oder FIR Zone ist
                    bool isCtr = featureJson.Contains("\"position\":\"CTR\"") ||
                                 featureJson.Contains("\"position\": \"CTR\"");
                    bool isFir = featureJson.Contains("\"position\":\"FIR\"") ||
                                 featureJson.Contains("\"position\": \"FIR\"");

                    if (isCtr || isFir)
                    {
                        // Extrahiere Prefix/ICAO aus der ID
                        string prefix = ExtractPrefixFromFeature(featureJson);

                        // Nur ein Feature pro Prefix (verhindert Duplikate)
                        if (!string.IsNullOrEmpty(prefix) && !seenPrefixes.Contains(prefix))
                        {
                            seenPrefixes.Add(prefix);
                            features.Add(featureJson);
                            featureCount++;
                        }
                    }

                    searchPos = featureEnd;
                }

                KneeboardLogger.Boundaries($"IVAO Offline FIR: extracted {features.Count} unique FIR/CTR zones");
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"IVAO Offline FIR generation error: {ex.Message}");
            }

            return "{\"type\":\"FeatureCollection\",\"features\":[" + string.Join(",", features) + "]}";
        }

        /// <summary>
        /// Extrahiert den ICAO-Prefix aus einem Feature-JSON
        /// </summary>
        private string ExtractPrefixFromFeature(string featureJson)
        {
            // Versuche airport_id zu finden
            int airportIdStart = featureJson.IndexOf("\"airport_id\"");
            if (airportIdStart != -1)
            {
                int colonPos = featureJson.IndexOf(':', airportIdStart);
                if (colonPos != -1)
                {
                    int quoteStart = featureJson.IndexOf('"', colonPos + 1);
                    int quoteEnd = featureJson.IndexOf('"', quoteStart + 1);
                    if (quoteStart != -1 && quoteEnd != -1)
                    {
                        string airportId = featureJson.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                        if (!string.IsNullOrEmpty(airportId) && airportId.Length == 4)
                        {
                            return airportId.ToUpperInvariant();
                        }
                    }
                }
            }

            // Fallback: Extrahiere aus ID
            int idStart = featureJson.IndexOf("\"id\"");
            if (idStart != -1)
            {
                int colonPos = featureJson.IndexOf(':', idStart);
                if (colonPos != -1)
                {
                    int quoteStart = featureJson.IndexOf('"', colonPos + 1);
                    int quoteEnd = featureJson.IndexOf('"', quoteStart + 1);
                    if (quoteStart != -1 && quoteEnd != -1)
                    {
                        string id = featureJson.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                        // ID Format: NAME_POSITION oder NAME_SECTOR_POSITION
                        // Versuche ICAO aus dem Namen zu extrahieren (4 Buchstaben am Anfang)
                        if (id.Length >= 4)
                        {
                            string potentialIcao = id.Substring(0, 4).ToUpperInvariant();
                            if (potentialIcao.All(c => char.IsLetter(c)))
                            {
                                return potentialIcao;
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Liefert IVAO Boundaries mit vorberechneten Bounding-Boxes
        /// </summary>
        private async Task HandleIvaoBoundariesWithBbox(IHttpContext ctx)
        {
            try
            {
                // Check preprocessed cache
                string cachedData = null;
                lock (_boundariesCacheLock)
                {
                    if (_preprocessedIvaoBoundaries != null && (DateTime.Now - _preprocessedIvaoBoundariesTime) < BOUNDARIES_CACHE_TTL)
                    {
                        cachedData = _preprocessedIvaoBoundaries;
                    }
                }

                if (cachedData != null)
                {
                    ctx.Response.Headers.Add("X-Cache", "HIT");
                    await ResponseJsonAsync(ctx, cachedData);
                    return;
                }

                // Get raw boundaries (from existing cache or fetch)
                string rawBoundaries = null;
                lock (_boundariesCacheLock)
                {
                    if (_cachedIvaoBoundaries != null && (DateTime.Now - _ivaoBoundariesCacheTime) < BOUNDARIES_CACHE_TTL)
                    {
                        rawBoundaries = _cachedIvaoBoundaries;
                    }
                }

                if (rawBoundaries == null)
                {
                    // Fetch from IVAO API
                    using (var client = new WebClient())
                    {
                        client.Encoding = Encoding.UTF8;
                        client.Headers.Add(HttpRequestHeader.UserAgent, "KneeboardServer/1.0");
                        rawBoundaries = client.DownloadString("https://api.ivao.aero/v2/firs");

                        lock (_boundariesCacheLock)
                        {
                            _cachedIvaoBoundaries = rawBoundaries;
                            _ivaoBoundariesCacheTime = DateTime.Now;
                        }
                    }
                }

                // Preprocess with bounding boxes
                string preprocessed = PreprocessGeoJsonWithBoundingBoxes(rawBoundaries);

                lock (_boundariesCacheLock)
                {
                    _preprocessedIvaoBoundaries = preprocessed;
                    _preprocessedIvaoBoundariesTime = DateTime.Now;
                }

                KneeboardLogger.Boundaries("IVAO Boundaries: preprocessed with bounding boxes");
                ctx.Response.Headers.Add("X-Cache", "PREPROCESSED");
                await ResponseJsonAsync(ctx, preprocessed);
            }
            catch (Exception ex)
            {
                KneeboardLogger.BoundariesError($"IVAO Boundaries preprocessing error: {ex.Message}");
                ctx.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                // EmbedIO manages stream lifecycle
            }
        }

        /// <summary>
        /// Load pilot favorites from disk
        /// </summary>
        private async Task HandleGetFavorites(IHttpContext ctx)
        {
            try
            {
                string json = null;
                lock (_favoritesLock)
                {
                    if (File.Exists(FAVORITES_FILE))
                    {
                        json = File.ReadAllText(FAVORITES_FILE, Encoding.UTF8);
                        // Remove UTF-8 BOM if present (backwards compatibility)
                        if (json.Length > 0 && json[0] == '\uFEFF')
                        {
                            json = json.Substring(1);
                        }
                    }
                }

                if (json != null)
                {
                    await ResponseJsonAsync(ctx, json);
                }
                else
                {
                    await ResponseJsonAsync(ctx, "{}");
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.Server($"Error loading favorites: {ex.Message}");
                await ResponseJsonAsync(ctx, "{}");
            }
        }

        /// <summary>
        /// Save pilot favorites to disk
        /// </summary>
        private async Task HandleSaveFavorites(IHttpContext ctx)
        {
            try
            {
                using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                {
                    string json = reader.ReadToEnd();

                    lock (_favoritesLock)
                    {
                        // Ensure cache directory exists
                        var cacheDir = Path.GetDirectoryName(FAVORITES_FILE);
                        if (!Directory.Exists(cacheDir))
                        {
                            Directory.CreateDirectory(cacheDir);
                        }

                        // Write UTF-8 without BOM to ensure JavaScript JSON.parse() compatibility
                        File.WriteAllText(FAVORITES_FILE, json, new UTF8Encoding(false));
                    }

                    await ResponseJsonAsync(ctx, "{\"success\":true}");
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.Server($"Error saving favorites: {ex.Message}");
                ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await ResponseJsonAsync(ctx, "{\"error\":\"" + ex.Message.Replace("\"", "\\\"") + "\"}");
            }
        }

        /// <summary>
        /// Handles ICAO-based airport lookup via OpenAIP API.
        /// Returns airport coordinates for a given ICAO code.
        /// Endpoint: api/openaip/airport-by-icao/{ICAO}
        /// </summary>
        private async Task HandleOpenAipAirportByIcao(IHttpContext ctx, string icaoCode)
        {
            if (string.IsNullOrWhiteSpace(icaoCode) || icaoCode.Length < 3 || icaoCode.Length > 4)
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await ResponseJsonAsync(ctx, "{\"error\":\"Invalid ICAO code\"}");
                return;
            }

            // Cache path for ICAO lookups
            var cachePath = Path.Combine(CACHE_DIR, "icao", $"{icaoCode}.json");

            // Check cache first
            byte[] cachedData = null;
            lock (_cacheLock)
            {
                if (File.Exists(cachePath))
                {
                    var fileInfo = new FileInfo(cachePath);
                    if (DateTime.Now - fileInfo.LastWriteTime < CACHE_TTL)
                    {
                        try
                        {
                            cachedData = File.ReadAllBytes(cachePath);
                        }
                        catch (Exception ex)
                        {
                            KneeboardLogger.APIError($"Cache read error: {ex.Message}");
                        }
                    }
                }
            }

            if (cachedData != null)
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                // ctx.Response.ContentLength = cachedData.Length;
                ctx.Response.Headers.Add("X-Cache", "HIT");
                await ctx.Response.OutputStream.WriteAsync(cachedData, 0, cachedData.Length);
                // EmbedIO manages stream
                return;
            }

            // Build OpenAIP API URL with search parameter
            var apiUrl = $"https://api.core.openaip.net/api/airports?page=1&limit=10&search={Uri.EscapeDataString(icaoCode)}&apiKey={Uri.EscapeDataString(OPENAIP_API_KEY)}";

            HttpWebResponse upstreamResponse = null;
            try
            {
                var outboundRequest = (HttpWebRequest)WebRequest.Create(apiUrl);
                outboundRequest.Method = "GET";
                outboundRequest.Accept = "application/json";
                outboundRequest.Headers.Add("x-openaip-client-id", OPENAIP_API_KEY);
                outboundRequest.Timeout = 5000;
                outboundRequest.ReadWriteTimeout = 5000;
                upstreamResponse = (HttpWebResponse)outboundRequest.GetResponse();

                string responseBody;
                using (var reader = new StreamReader(upstreamResponse.GetResponseStream()))
                {
                    responseBody = reader.ReadToEnd();
                }

                // Parse the response to find the exact ICAO match
                dynamic result = null;
                try
                {
                    dynamic jsonResponse = Newtonsoft.Json.JsonConvert.DeserializeObject(responseBody);
                    if (jsonResponse?.items != null)
                    {
                        foreach (var airport in jsonResponse.items)
                        {
                            string airportIcao = airport.icaoCode?.ToString()?.ToUpperInvariant();
                            if (airportIcao == icaoCode)
                            {
                                // Extract geometry coordinates [lng, lat]
                                var geometry = airport.geometry;
                                if (geometry?.coordinates != null)
                                {
                                    double lng = (double)geometry.coordinates[0];
                                    double lat = (double)geometry.coordinates[1];
                                    result = new
                                    {
                                        icao = icaoCode,
                                        name = (string)airport.name,
                                        lat = lat,
                                        lng = lng,
                                        found = true
                                    };
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception parseEx)
                {
                    KneeboardLogger.APIError($"Parse error: {parseEx.Message}");
                }

                // If no exact match found
                if (result == null)
                {
                    result = new { icao = icaoCode, found = false };
                }

                var resultJson = Newtonsoft.Json.JsonConvert.SerializeObject(result);
                var resultBytes = System.Text.Encoding.UTF8.GetBytes(resultJson);

                // Cache the result
                if (result.found)
                {
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            var cacheDir = Path.GetDirectoryName(cachePath);
                            if (!Directory.Exists(cacheDir))
                                Directory.CreateDirectory(cacheDir);
                            File.WriteAllBytes(cachePath, resultBytes);
                        }
                        catch (Exception cacheEx)
                        {
                            KneeboardLogger.APIError($"Cache write error: {cacheEx.Message}");
                        }
                    });
                }

                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                // ctx.Response.ContentLength = resultBytes.Length;
                ctx.Response.Headers.Add("X-Cache", "MISS");
                await ctx.Response.OutputStream.WriteAsync(resultBytes, 0, resultBytes.Length);
            }
            catch (WebException ex)
            {
                var httpResponse = ex.Response as HttpWebResponse;
                KneeboardLogger.API($"Error fetching {icaoCode}: {ex.Message}");
                ctx.Response.StatusCode = httpResponse != null
                    ? (int)httpResponse.StatusCode
                    : (int)HttpStatusCode.BadGateway;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message.Replace("\"", "\\\"")}\",\"icao\":\"{icaoCode}\",\"found\":false}}");
            }
            catch (Exception ex)
            {
                KneeboardLogger.APIError($"Unexpected error: {ex.Message}");
                ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message.Replace("\"", "\\\"")}\",\"icao\":\"{icaoCode}\",\"found\":false}}");
            }
            finally
            {
                upstreamResponse?.Close();
                // EmbedIO manages stream lifecycle
            }
        }

        /// <summary>
        /// Fetches runway data from OpenAIP API
        /// Endpoint: api/openaip/runway/{icao}/{runwayId}
        /// Example: api/openaip/runway/KPDX/10L
        /// </summary>
        private async Task HandleOpenAipRunway(IHttpContext ctx, string icao, string runwayId)
        {
            if (string.IsNullOrWhiteSpace(icao) || icao.Length < 3 || icao.Length > 4)
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await ResponseJsonAsync(ctx, "{\"error\":\"Invalid ICAO code\"}");
                return;
            }

            // Cache path for runway lookups
            var cachePath = Path.Combine(CACHE_DIR, "runways", $"{icao}_{runwayId}.json");

            // Check cache first
            byte[] cachedData = null;
            lock (_cacheLock)
            {
                if (File.Exists(cachePath))
                {
                    var fileInfo = new FileInfo(cachePath);
                    if (DateTime.Now - fileInfo.LastWriteTime < CACHE_TTL)
                    {
                        try
                        {
                            cachedData = File.ReadAllBytes(cachePath);
                        }
                        catch (Exception ex)
                        {
                            KneeboardLogger.APIError($"Cache read error: {ex.Message}");
                        }
                    }
                }
            }

            if (cachedData != null)
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                // ctx.Response.ContentLength = cachedData.Length;
                ctx.Response.Headers.Add("X-Cache", "HIT");
                await ctx.Response.OutputStream.WriteAsync(cachedData, 0, cachedData.Length);
                // EmbedIO manages stream
                return;
            }

            HttpWebResponse upstreamResponse = null;
            try
            {
                // Query openAIP for all runways at this airport
                var apiUrl = $"https://api.core.openaip.net/api/runways?icao={Uri.EscapeDataString(icao)}&limit=50&apiKey={Uri.EscapeDataString(OPENAIP_API_KEY)}";

                var outboundRequest = (HttpWebRequest)WebRequest.Create(apiUrl);
                outboundRequest.Method = "GET";
                outboundRequest.Accept = "application/json";
                outboundRequest.Headers.Add("x-openaip-client-id", OPENAIP_API_KEY);
                outboundRequest.Timeout = 5000;
                outboundRequest.ReadWriteTimeout = 5000;
                upstreamResponse = (HttpWebResponse)outboundRequest.GetResponse();

                string responseBody;
                using (var reader = new StreamReader(upstreamResponse.GetResponseStream()))
                {
                    responseBody = reader.ReadToEnd();
                }

                dynamic jsonResponse = Newtonsoft.Json.JsonConvert.DeserializeObject(responseBody);
                
                // Find matching runway by identifier
                dynamic matchedRunway = null;
                if (jsonResponse?.items != null)
                {
                    foreach (var runway in jsonResponse.items)
                    {
                        string rwyId = runway.identifier?.ToString()?.ToUpperInvariant();
                        if (rwyId == runwayId)
                        {
                            matchedRunway = runway;
                            break;
                        }
                    }
                }

                if (matchedRunway == null)
                {
                    KneeboardLogger.API($"Runway {runwayId} not found at {icao}");
                    ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    await ResponseJsonAsync(ctx, $"{{\"error\":\"Runway {runwayId} not found at {icao}\"}}");
                    return;
                }

                // Extract threshold coordinates
                double thresholdLat = 0;
                double thresholdLon = 0;
                
                if (matchedRunway.thresholdCoordinates != null)
                {
                    thresholdLat = (double)matchedRunway.thresholdCoordinates.latitude;
                    thresholdLon = (double)matchedRunway.thresholdCoordinates.longitude;
                }
                else if (matchedRunway.geometry?.coordinates != null)
                {
                    // Fallback to geometry coordinates [lng, lat]
                    thresholdLon = (double)matchedRunway.geometry.coordinates[0];
                    thresholdLat = (double)matchedRunway.geometry.coordinates[1];
                }

                var result = new
                {
                    airport = icao,
                    runway = matchedRunway.identifier?.ToString(),
                    thresholdLat = thresholdLat,
                    thresholdLon = thresholdLon,
                    heading = matchedRunway.magneticBearing ?? matchedRunway.trueBearing,
                    length = matchedRunway.tora ?? matchedRunway.toda,
                    width = matchedRunway.width,
                    elevation = matchedRunway.thresholdElevation,
                    source = "openAIP"
                };

                string resultJson = Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);
                byte[] resultBytes = Encoding.UTF8.GetBytes(resultJson);

                // Cache the result asynchronously
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var cacheDir = Path.GetDirectoryName(cachePath);
                        if (!Directory.Exists(cacheDir))
                            Directory.CreateDirectory(cacheDir);
                        File.WriteAllBytes(cachePath, resultBytes);
                    }
                    catch (Exception cacheEx)
                    {
                        KneeboardLogger.APIError($"Cache write error: {cacheEx.Message}");
                    }
                });

                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                // ctx.Response.ContentLength = resultBytes.Length;
                ctx.Response.Headers.Add("X-Cache", "MISS");
                await ctx.Response.OutputStream.WriteAsync(resultBytes, 0, resultBytes.Length);
            }
            catch (WebException ex)
            {
                var httpResponse = ex.Response as HttpWebResponse;
                KneeboardLogger.API($"Error fetching {icao}/{runwayId}: {ex.Message}");
                ctx.Response.StatusCode = httpResponse != null
                    ? (int)httpResponse.StatusCode
                    : (int)HttpStatusCode.BadGateway;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message.Replace("\"", "\\\"")}\",\"icao\":\"{icao}\",\"runway\":\"{runwayId}\",\"found\":false}}");
            }
            catch (Exception ex)
            {
                KneeboardLogger.APIError($"Unexpected error: {ex.Message}");
                ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await ResponseJsonAsync(ctx, $"{{\"error\":\"{ex.Message.Replace("\"", "\\\"")}\",\"icao\":\"{icao}\",\"runway\":\"{runwayId}\",\"found\":false}}");
            }
            finally
            {
                upstreamResponse?.Close();
                // EmbedIO manages stream lifecycle
            }
        }

        // Global airport ICAO index - loaded once from OpenAIP
        private static Dictionary<string, (double lat, double lng, string name)> _globalAirportIndex = null;
        // IATA to ICAO mapping (e.g., "JFK" -> "KJFK", "YYZ" -> "CYYZ")
        private static Dictionary<string, string> _iataToIcaoIndex = null;
        private static readonly object _airportIndexLock = new object();
        private static bool _airportIndexLoading = false;
        private static readonly string GLOBAL_AIRPORTS_CACHE = Path.Combine(CACHE_BASE_DIR, "global_airports.json");
        private static readonly string IATA_ICAO_CACHE = Path.Combine(CACHE_BASE_DIR, "iata_icao_mapping.json");

        /// <summary>
        /// Starts loading the global airport index in the background.
        /// Call this at server startup.
        /// </summary>
        public void StartGlobalAirportIndexLoad()
        {
            if (_globalAirportIndex != null) return;
            if (_airportIndexLoading) return;

            System.Threading.Tasks.Task.Run(() => LoadGlobalAirportIndex());
        }

        /// <summary>
        /// Loads ALL airports from OpenAIP API and builds a global ICAO index.
        /// Uses aggressive caching (30 days) since airport data rarely changes.
        /// </summary>
        private void LoadGlobalAirportIndex()
        {
            if (_globalAirportIndex != null) return;

            lock (_airportIndexLock)
            {
                if (_globalAirportIndex != null) return;
                if (_airportIndexLoading) return;
                _airportIndexLoading = true;
            }

            try
            {
                Logging.KneeboardLogger.Server($"[OpenAIP] Cache path: {GLOBAL_AIRPORTS_CACHE}");

                // Try to load from cache first
                if (File.Exists(GLOBAL_AIRPORTS_CACHE))
                {
                    var fileInfo = new FileInfo(GLOBAL_AIRPORTS_CACHE);
                    var cacheAge = DateTime.Now - fileInfo.LastWriteTime;
                    Logging.KneeboardLogger.Server($"[OpenAIP] Cache found, age: {cacheAge.TotalDays:F1} days, size: {fileInfo.Length} bytes");

                    if (cacheAge < TimeSpan.FromDays(30))
                    {
                        try
                        {
                            var cachedJson = File.ReadAllText(GLOBAL_AIRPORTS_CACHE);
                            var cached = Newtonsoft.Json.Linq.JObject.Parse(cachedJson);
                            var tempIndex = new Dictionary<string, (double lat, double lng, string name)>();
                            foreach (var prop in cached.Properties())
                            {
                                var entry = prop.Value;
                                tempIndex[prop.Name] = (
                                    entry.Value<double>("lat"),
                                    entry.Value<double>("lng"),
                                    entry.Value<string>("name") ?? ""
                                );
                            }
                            _globalAirportIndex = tempIndex;

                            // Also load IATA->ICAO mapping from cache
                            if (File.Exists(IATA_ICAO_CACHE))
                            {
                                try
                                {
                                    var iataJson = File.ReadAllText(IATA_ICAO_CACHE);
                                    _iataToIcaoIndex = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(iataJson);
                                    Logging.KneeboardLogger.Server($"[OpenAIP] Loaded {_iataToIcaoIndex.Count} IATA->ICAO mappings from cache");
                                }
                                catch (Exception iataEx)
                                {
                                    Logging.KneeboardLogger.Error("OpenAIP", $"IATA cache read error: {iataEx.Message}");
                                    _iataToIcaoIndex = new Dictionary<string, string>();
                                }
                            }
                            else
                            {
                                _iataToIcaoIndex = new Dictionary<string, string>();
                            }

                            Logging.KneeboardLogger.Server($"[OpenAIP] Loaded {_globalAirportIndex.Count} airports from cache");
                            _kneeboardServer?.SetStatusText($"Status: {_globalAirportIndex.Count} airports loaded. Server is running...");
                            return;
                        }
                        catch (Exception ex)
                        {
                            Logging.KneeboardLogger.Error("OpenAIP", $"Cache read error: {ex.Message}\n{ex.StackTrace}");
                        }
                    }
                    else
                    {
                        Logging.KneeboardLogger.Server($"[OpenAIP] Cache expired ({cacheAge.TotalDays:F0} days old), reloading from API");
                    }
                }
                else
                {
                    Logging.KneeboardLogger.Server("[OpenAIP] No cache file found, loading from API");
                }

                // Load ALL airports from OpenAIP API (paginated)
                Logging.KneeboardLogger.Server("[OpenAIP] Loading global airport database from API...");
                _kneeboardServer?.SetStatusText("Status: Loading airports from OpenAIP...");
                var allAirports = new Dictionary<string, (double lat, double lng, string name)>();
                var iataMapping = new Dictionary<string, string>(); // IATA -> ICAO mapping
                int page = 1;
                int totalPages = 1;
                int limit = 1000;

                while (page <= totalPages)
                {
                    var apiUrl = $"https://api.core.openaip.net/api/airports?page={page}&limit={limit}&apiKey={Uri.EscapeDataString(OPENAIP_API_KEY)}";

                    try
                    {
                        var request = (HttpWebRequest)WebRequest.Create(apiUrl);
                        request.Method = "GET";
                        request.Accept = "application/json";
                        request.Headers.Add("x-openaip-client-id", OPENAIP_API_KEY);
                        request.Timeout = 30000;

                        using (var response = (HttpWebResponse)request.GetResponse())
                        using (var reader = new StreamReader(response.GetResponseStream()))
                        {
                            var json = reader.ReadToEnd();
                            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(json);

                            if (page == 1 && data.totalPages != null)
                            {
                                totalPages = (int)data.totalPages;
                                Logging.KneeboardLogger.Server($"[OpenAIP] Total pages: {totalPages}");
                            }

                            if (data.items != null)
                            {
                                foreach (var airport in data.items)
                                {
                                    string icao = airport.icaoCode?.ToString()?.ToUpperInvariant();
                                    string iata = airport.iataCode?.ToString()?.ToUpperInvariant();
                                    if (!string.IsNullOrEmpty(icao) && airport.geometry?.coordinates != null)
                                    {
                                        double lng = (double)airport.geometry.coordinates[0];
                                        double lat = (double)airport.geometry.coordinates[1];
                                        string name = airport.name?.ToString() ?? "";
                                        allAirports[icao] = (lat, lng, name);

                                        // Store IATA -> ICAO mapping if IATA code exists
                                        if (!string.IsNullOrEmpty(iata) && iata.Length == 3)
                                        {
                                            iataMapping[iata] = icao;
                                        }
                                    }
                                }
                            }

                            Logging.KneeboardLogger.Server($"[OpenAIP] Page {page}/{totalPages} - Airports with ICAO: {allAirports.Count}");
                            _kneeboardServer?.SetStatusText($"Status: Loading airports... {page}/{totalPages} ({allAirports.Count} ICAO)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.KneeboardLogger.Error("OpenAIP", $"Error loading page {page}: {ex.Message}");
                        break;
                    }

                    page++;

                    // Small delay to avoid rate limiting
                    if (page <= totalPages)
                        System.Threading.Thread.Sleep(50);
                }

                _globalAirportIndex = allAirports;
                _iataToIcaoIndex = iataMapping;
                Logging.KneeboardLogger.Server($"[OpenAIP] Loaded {_globalAirportIndex.Count} airports with ICAO codes, {_iataToIcaoIndex.Count} IATA mappings");
                _kneeboardServer?.SetStatusText($"Status: {_globalAirportIndex.Count} airports loaded. Server is running...");

                // Save to cache
                if (allAirports.Count > 0)
                {
                    try
                    {
                        var cacheDir = Path.GetDirectoryName(GLOBAL_AIRPORTS_CACHE);
                        if (!Directory.Exists(cacheDir))
                            Directory.CreateDirectory(cacheDir);

                        var cacheData = new Dictionary<string, object>();
                        foreach (var kvp in _globalAirportIndex)
                        {
                            cacheData[kvp.Key] = new { lat = kvp.Value.lat, lng = kvp.Value.lng, name = kvp.Value.name };
                        }
                        File.WriteAllText(GLOBAL_AIRPORTS_CACHE, Newtonsoft.Json.JsonConvert.SerializeObject(cacheData));

                        // Save IATA->ICAO mapping cache
                        File.WriteAllText(IATA_ICAO_CACHE, Newtonsoft.Json.JsonConvert.SerializeObject(iataMapping));
                        Logging.KneeboardLogger.Server($"[OpenAIP] Saved airport cache ({allAirports.Count} airports) and {iataMapping.Count} IATA mappings to {GLOBAL_AIRPORTS_CACHE}");
                    }
                    catch (Exception ex)
                    {
                        Logging.KneeboardLogger.Error("OpenAIP", $"Cache save error: {ex.Message}\n{ex.StackTrace}");
                    }
                }
            }
            finally
            {
                _airportIndexLoading = false;
            }
        }

        /// <summary>
        /// Handles IATA to ICAO conversion request.
        /// Endpoint: api/openaip/iata-to-icao/{IATA}
        /// </summary>
        private async Task HandleIataToIcao(IHttpContext ctx, string iataCode)
        {
            try
            {
                if (string.IsNullOrEmpty(iataCode) || iataCode.Length != 3)
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"found\":false,\"error\":\"Invalid IATA code\"}");
                    return;
                }

                string icao = null;
                if (_iataToIcaoIndex != null && _iataToIcaoIndex.TryGetValue(iataCode, out icao))
                {
                    await ResponseJsonAsync(ctx, $"{{\"found\":true,\"iata\":\"{iataCode}\",\"icao\":\"{icao}\"}}");
                }
                else
                {
                    await ResponseJsonAsync(ctx, $"{{\"found\":false,\"iata\":\"{iataCode}\"}}");
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.APIError($"IATA-to-ICAO error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, "{\"found\":false,\"error\":\"Internal error\"}");
            }
        }

        /// <summary>
        /// Returns the full IATA->ICAO mapping for frontend caching.
        /// Endpoint: api/openaip/iata-icao-mapping
        /// </summary>
        private async Task HandleIataIcaoMapping(IHttpContext ctx)
        {
            try
            {
                if (_iataToIcaoIndex == null || _iataToIcaoIndex.Count == 0)
                {
                    await ResponseJsonAsync(ctx, "{\"ready\":false,\"count\":0,\"mapping\":{}}");
                    return;
                }

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    ready = true,
                    count = _iataToIcaoIndex.Count,
                    mapping = _iataToIcaoIndex
                });
                await ResponseJsonAsync(ctx, json);
            }
            catch (Exception ex)
            {
                KneeboardLogger.APIError($"IATA-ICAO-mapping error: {ex.Message}");
                ctx.Response.StatusCode = 500;
                await ResponseJsonAsync(ctx, "{\"ready\":false,\"error\":\"Internal error\"}");
            }
        }

        /// <summary>
        /// Looks up airport coordinates from the global index.
        /// If not found in index, tries OpenAIP search API and caches the result.
        /// </summary>
        private (double lat, double lng, string name)? LookupAirportByIcao(string icao)
        {
            if (string.IsNullOrEmpty(icao)) return null;
            icao = icao.ToUpperInvariant();

            // Check global index first
            if (_globalAirportIndex != null && _globalAirportIndex.TryGetValue(icao, out var result))
            {
                return result;
            }

            // If not found, try OpenAIP search API directly
            try
            {
                var apiUrl = $"https://api.core.openaip.net/api/airports?search={Uri.EscapeDataString(icao)}&limit=5&apiKey={Uri.EscapeDataString(OPENAIP_API_KEY)}";
                var request = (HttpWebRequest)WebRequest.Create(apiUrl);
                request.Method = "GET";
                request.Accept = "application/json";
                request.Headers.Add("x-openaip-client-id", OPENAIP_API_KEY);
                request.Timeout = 5000;

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    var json = reader.ReadToEnd();
                    dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(json);

                    if (data.items != null)
                    {
                        foreach (var airport in data.items)
                        {
                            string foundIcao = airport.icaoCode?.ToString()?.ToUpperInvariant();
                            if (foundIcao == icao && airport.geometry?.coordinates != null)
                            {
                                double lng = (double)airport.geometry.coordinates[0];
                                double lat = (double)airport.geometry.coordinates[1];
                                string name = airport.name?.ToString() ?? "";

                                // Cache the result for future lookups
                                if (_globalAirportIndex != null)
                                {
                                    _globalAirportIndex[icao] = (lat, lng, name);
                                }

                                KneeboardLogger.API($"Found {icao} via search: {name}");
                                return (lat, lng, name);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.API($"Search error for {icao}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Batch endpoint to fetch DEP and ARR airport coordinates in one request.
        /// Uses the global airport index for instant O(1) lookup.
        /// Endpoint: api/openaip/airports-batch?dep=KMIA&arr=LIPE
        /// </summary>
        private async Task HandleOpenAipAirportsBatch(IHttpContext ctx)
        {
            // If index is still loading, return a "loading" status
            if (_globalAirportIndex == null)
            {
                // Start loading if not already started
                if (!_airportIndexLoading)
                {
                    StartGlobalAirportIndexLoad();
                }

                var loadingResponse = Newtonsoft.Json.JsonConvert.SerializeObject(new { loading = true, message = "Airport database is being loaded..." });
                var loadingBytes = System.Text.Encoding.UTF8.GetBytes(loadingResponse);
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                // ctx.Response.ContentLength = loadingBytes.Length;
                await ctx.Response.OutputStream.WriteAsync(loadingBytes, 0, loadingBytes.Length);
                // EmbedIO manages stream lifecycle
                return;
            }

            var depIcao = (ctx.Request.QueryString["dep"] ?? "").Trim().ToUpperInvariant();
            var arrIcao = (ctx.Request.QueryString["arr"] ?? "").Trim().ToUpperInvariant();

            if (string.IsNullOrEmpty(depIcao) && string.IsNullOrEmpty(arrIcao))
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await ResponseJsonAsync(ctx, "{\"error\":\"At least one of dep or arr is required\"}");
                return;
            }

            var result = new Dictionary<string, object>();

            // Lookup DEP
            if (!string.IsNullOrEmpty(depIcao))
            {
                var depResult = LookupAirportByIcao(depIcao);
                if (depResult.HasValue)
                {
                    result["dep"] = new { icao = depIcao, name = depResult.Value.name, lat = depResult.Value.lat, lng = depResult.Value.lng, found = true };
                }
                else
                {
                    result["dep"] = new { icao = depIcao, found = false };
                }
            }

            // Lookup ARR
            if (!string.IsNullOrEmpty(arrIcao))
            {
                var arrResult = LookupAirportByIcao(arrIcao);
                if (arrResult.HasValue)
                {
                    result["arr"] = new { icao = arrIcao, name = arrResult.Value.name, lat = arrResult.Value.lat, lng = arrResult.Value.lng, found = true };
                }
                else
                {
                    result["arr"] = new { icao = arrIcao, found = false };
                }
            }

            var resultJson = Newtonsoft.Json.JsonConvert.SerializeObject(result);
            var resultBytes = System.Text.Encoding.UTF8.GetBytes(resultJson);

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            // ctx.Response.ContentLength = resultBytes.Length;
            await ctx.Response.OutputStream.WriteAsync(resultBytes, 0, resultBytes.Length);
            // EmbedIO manages stream lifecycle
        }

        private async Task HandleOpenAipProxy(IHttpContext ctx, string endpoint)
        {
            var request = ctx.Request;
            var lat = (request.QueryString["lat"] ?? string.Empty).Trim();
            var lng = (request.QueryString["lng"] ?? string.Empty).Trim();
            var dist = (request.QueryString["dist"] ?? string.Empty).Trim();
            bool isTilesEndpoint = endpoint.Equals("tiles", StringComparison.OrdinalIgnoreCase);

            if (!isTilesEndpoint && (string.IsNullOrWhiteSpace(lat) || string.IsNullOrWhiteSpace(lng) || string.IsNullOrWhiteSpace(dist)))
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                // EmbedIO manages stream
                return;
            }

            // Normalize coordinates and limit search radius to prevent timeouts
            if (!isTilesEndpoint)
            {
                // Normalize longitude to -180 to +180 range
                if (double.TryParse(lng, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double lngVal))
                {
                    while (lngVal > 180) lngVal -= 360;
                    while (lngVal < -180) lngVal += 360;
                    lng = lngVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                // Clamp latitude to -90 to +90 range
                if (double.TryParse(lat, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double latVal))
                {
                    latVal = Math.Max(-90, Math.Min(90, latVal));
                    lat = latVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                // Limit search radius to 500 km (500,000 meters) to prevent API timeouts
                const int MAX_DISTANCE = 500000;
                if (int.TryParse(dist, out int distVal) && distVal > MAX_DISTANCE)
                {
                    dist = MAX_DISTANCE.ToString();
                }
            }

            // Determine cache path
            string cachePath = null;
            string tilePart = null;

            if (isTilesEndpoint)
            {
                var path = request.Url.AbsolutePath;
                var idx = path.IndexOf("/api/openaip/tiles/", StringComparison.OrdinalIgnoreCase);
                tilePart = path.Substring(idx + "/api/openaip/tiles/".Length);
                cachePath = GetTileCachePath(tilePart);
            }
            else
            {
                cachePath = GetApiCachePath(endpoint, lat, lng, dist);
            }

            // Check cache first
            if (IsCacheValid(cachePath))
            {
                try
                {
                    byte[] cachedData = ReadFromCache(cachePath);
                    ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                    ctx.Response.ContentType = isTilesEndpoint ? "image/png" : "application/json";
                    // ctx.Response.ContentLength = cachedData.Length;
                    ctx.Response.Headers.Add("X-Cache", "HIT");
                    await ctx.Response.OutputStream.WriteAsync(cachedData, 0, cachedData.Length);
                    // EmbedIO manages stream
                    return;
                }
                catch (Exception ex)
                {
                    KneeboardLogger.CacheError($"Cache read error: {ex.Message}");
                    // Continue to fetch from upstream
                }
            }

            var builder = new StringBuilder();
            if (!isTilesEndpoint)
            {
                builder.Append("https://api.core.openaip.net/api/")
                       .Append(endpoint)
                       .Append("?page=1&limit=1000&pos=")
                       .Append(Uri.EscapeDataString(lat))
                       .Append("%2C")
                       .Append(Uri.EscapeDataString(lng))
                       .Append("&dist=")
                       .Append(Uri.EscapeDataString(dist))
                       .Append("&sortBy=name&sortDesc=true&approved=true&searchOptLwc=false")
                       .Append("&apiKey=")
                       .Append(Uri.EscapeDataString(OPENAIP_API_KEY));
            }
            else
            {
                // OpenAIP tiles API uses subdomains (a, b, c) for load balancing
                // Rotate through subdomains based on tile coordinates for better distribution
                string[] subdomains = { "a", "b", "c" };
                int subdomainIndex = Math.Abs(tilePart.GetHashCode()) % subdomains.Length;
                string subdomain = subdomains[subdomainIndex];
                builder.Append("https://")
                       .Append(subdomain)
                       .Append(".api.tiles.openaip.net/api/data/openaip/")
                       .Append(tilePart)
                       .Append("?apiKey=")
                       .Append(Uri.EscapeDataString(OPENAIP_API_KEY));
            }

            HttpWebResponse upstreamResponse = null;
            try
            {
                var outboundRequest = (HttpWebRequest)WebRequest.Create(builder.ToString());
                outboundRequest.Method = "GET";
                outboundRequest.Accept = isTilesEndpoint ? "image/png" : "application/json";
                // Always add header for authentication
                outboundRequest.Headers.Add("x-openaip-client-id", OPENAIP_API_KEY);
                outboundRequest.Timeout = 7000; // 7 second timeout (optimized)
                outboundRequest.ReadWriteTimeout = 7000; // 7 second read/write timeout (optimized)
                upstreamResponse = (HttpWebResponse)outboundRequest.GetResponse();

                // Read response into memory for caching
                byte[] responseData;
                using (var memoryStream = new MemoryStream())
                {
                    using (var upstreamStream = upstreamResponse.GetResponseStream())
                    {
                        upstreamStream.CopyTo(memoryStream);
                    }
                    responseData = memoryStream.ToArray();
                }

                // Write to cache (async, don't block response)
                if (responseData.Length > 0)
                {
                    ThreadPool.QueueUserWorkItem(_ => WriteToCache(cachePath, responseData));
                }

                ctx.Response.StatusCode = (int)upstreamResponse.StatusCode;
                ctx.Response.ContentType = upstreamResponse.ContentType ?? (isTilesEndpoint ? "image/png" : "application/json");
                // ctx.Response.ContentLength = responseData.Length;
                ctx.Response.Headers.Add("X-Cache", "MISS");
                await ctx.Response.OutputStream.WriteAsync(responseData, 0, responseData.Length);
            }
            catch (WebException ex)
            {
                var httpResponse = ex.Response as HttpWebResponse;
                ctx.Response.StatusCode = httpResponse != null
                    ? (int)httpResponse.StatusCode
                    : (int)HttpStatusCode.BadGateway;

                // Log error details to file AND console
                string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] OpenAIP Proxy Error\n" +
                    $"Message: {ex.Message}\n" +
                    $"URL: {builder.ToString()}\n" +
                    $"Status: {httpResponse?.StatusCode}\n";

                try
                {
                    File.AppendAllText(Path.Combine(_rootDirectory, "openaip_errors.log"), logMessage + "\n");
                }
                catch (Exception logEx)
                {
                    KneeboardLogger.APIError($"Failed to write error log: {logEx.Message}");
                }

                KneeboardLogger.APIError($"OpenAIP Proxy Error: {ex.Message}");
                KneeboardLogger.Server($"Requested URL: {builder.ToString()}");
                if (httpResponse != null)
                {
                    KneeboardLogger.Server($"Upstream Status: {httpResponse.StatusCode}");
                }

                string payload = "{\"error\":\"Unable to reach openAIP\"}";
                if (httpResponse != null)
                {
                    try
                    {
                        using (var reader = new StreamReader(httpResponse.GetResponseStream()))
                        {
                            payload = reader.ReadToEnd();
                            KneeboardLogger.Server($"Upstream Response: {payload}");
                        }
                    }
                    catch
                    {
                        payload = "{\"error\":\"Unable to reach openAIP\"}";
                    }
                }

                // For tiles, return empty PNG instead of JSON error
                if (isTilesEndpoint)
                {
                    ctx.Response.ContentType = "image/png";
                    // ctx.Response.ContentLength = 0;
                }
                else
                {
                    var buffer = Encoding.UTF8.GetBytes(payload);
                    ctx.Response.ContentType = "application/json";
                    // ctx.Response.ContentLength = buffer.Length;
                    await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
            }
            finally
            {
                upstreamResponse?.Close();
                // EmbedIO manages stream
            }
        }

        // ============================================================================
        // Baselayer Tile Proxy with Caching
        // Caches OSM, Google, OpenTopoMap tiles locally for faster loading
        // ============================================================================

        /// <summary>
        /// Gets the cache file path for a baselayer tile
        /// </summary>
        private static string GetBaselayerTileCachePath(string provider, string tilePath)
        {
            // tilePath is like "10/532/341.png" or "10/532/341"
            string safePath = tilePath.Replace('/', Path.DirectorySeparatorChar);
            if (!safePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                safePath += ".png";
            return Path.Combine(BASELAYER_CACHE_DIR, provider, safePath);
        }

        /// <summary>
        /// Checks if a baselayer cache file exists and is still valid
        /// </summary>
        private static bool IsBaselayerCacheValid(string cachePath)
        {
            if (!File.Exists(cachePath))
                return false;

            var fileInfo = new FileInfo(cachePath);
            return (DateTime.Now - fileInfo.LastWriteTime) < BASELAYER_CACHE_TTL;
        }

        /// <summary>
        /// Reads tile data from baselayer cache
        /// </summary>
        private static byte[] ReadFromBaselayerCache(string cachePath)
        {
            lock (_baselayerCacheLock)
            {
                return File.ReadAllBytes(cachePath);
            }
        }

        /// <summary>
        /// Writes tile data to baselayer cache
        /// </summary>
        private static void WriteToBaselayerCache(string cachePath, byte[] data)
        {
            try
            {
                lock (_baselayerCacheLock)
                {
                    string directory = Path.GetDirectoryName(cachePath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.WriteAllBytes(cachePath, data);
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.CacheError($"Write error: {ex.Message}");
            }
        }

        /// <summary>
        /// Proxies baselayer tiles with local caching
        /// Supports: osm, google-hybrid, google-satellite, opentopomap
        /// </summary>
        private async Task HandleBaselayerTileProxy(IHttpContext ctx, string provider)
        {
            HttpWebResponse upstreamResponse = null;
            bool responseClosed = false;

            try
            {
                var request = ctx.Request;
                var path = request.Url.AbsolutePath;

                // Parse tile coordinates from path: /api/tiles/{provider}/{z}/{x}/{y}.png
                string prefix = $"/api/tiles/{provider}/";
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    // EmbedIO manages stream lifecycle
                    responseClosed = true;
                    return;
                }

                string tilePath = path.Substring(prefix.Length); // e.g., "10/532/341.png"
                string cachePath = GetBaselayerTileCachePath(provider, tilePath);

                // Check cache first
                if (IsBaselayerCacheValid(cachePath))
                {
                    try
                    {
                        byte[] cachedData = ReadFromBaselayerCache(cachePath);
                        ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                        ctx.Response.ContentType = "image/png";
                        // ctx.Response.ContentLength = cachedData.Length;
                        ctx.Response.Headers.Add("X-Cache", "HIT");
                        ctx.Response.Headers.Add("Cache-Control", "public, max-age=2592000"); // 30 days
                        await ctx.Response.OutputStream.WriteAsync(cachedData, 0, cachedData.Length);
                        // EmbedIO manages stream
                        responseClosed = true;
                        return;
                    }
                    catch (System.Net.Sockets.SocketException)
                    {
                        // Client disconnected - silently abort
                        responseClosed = true;
                        return;
                    }
                    catch (Exception ex)
                    {
                        // Cache read failed but connection still alive - continue to upstream
                        if (ex.Message.Contains("Netzwerkname") || ex.Message.Contains("network name"))
                        {
                            responseClosed = true;
                            return;
                        }
                    }
                }

                // Build upstream URL based on provider
                string[] parts = tilePath.Replace(".png", "").Split('/');
                if (parts.Length < 3)
                {
                    try
                    {
                        ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        // EmbedIO manages stream
                    }
                    catch { }
                    responseClosed = true;
                    return;
                }

                string z = parts[0];
                string x = parts[1];
                string y = parts[2];
                string upstreamUrl;

                switch (provider.ToLower())
                {
                    case "osm":
                        string[] osmSubdomains = { "a", "b", "c" };
                        string osmSubdomain = osmSubdomains[Math.Abs((x + y).GetHashCode()) % osmSubdomains.Length];
                        upstreamUrl = $"https://{osmSubdomain}.tile.openstreetmap.org/{z}/{x}/{y}.png";
                        break;

                    case "google-hybrid":
                        string[] googleSubdomains = { "mt0", "mt1", "mt2", "mt3" };
                        string googleSubdomain = googleSubdomains[Math.Abs((x + y).GetHashCode()) % googleSubdomains.Length];
                        upstreamUrl = $"https://{googleSubdomain}.google.com/vt/lyrs=s,h&x={x}&y={y}&z={z}";
                        break;

                    case "google-satellite":
                        string[] satSubdomains = { "mt0", "mt1", "mt2", "mt3" };
                        string satSubdomain = satSubdomains[Math.Abs((x + y).GetHashCode()) % satSubdomains.Length];
                        upstreamUrl = $"https://{satSubdomain}.google.com/vt/lyrs=s&x={x}&y={y}&z={z}";
                        break;

                    case "opentopomap":
                        string[] topoSubdomains = { "a", "b", "c" };
                        string topoSubdomain = topoSubdomains[Math.Abs((x + y).GetHashCode()) % topoSubdomains.Length];
                        upstreamUrl = $"https://{topoSubdomain}.tile.opentopomap.org/{z}/{x}/{y}.png";
                        break;

                    default:
                        try
                        {
                            ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                            // EmbedIO manages stream
                        }
                        catch { }
                        responseClosed = true;
                        return;
                }

                var outboundRequest = (HttpWebRequest)WebRequest.Create(upstreamUrl);
                outboundRequest.Method = "GET";
                outboundRequest.Accept = "image/png,image/*";
                outboundRequest.UserAgent = "KneeboardServer/2.0";
                outboundRequest.Timeout = 15000;
                outboundRequest.ReadWriteTimeout = 15000;

                upstreamResponse = (HttpWebResponse)outboundRequest.GetResponse();

                // Read response into memory for caching
                byte[] responseData;
                using (var memoryStream = new MemoryStream())
                {
                    using (var upstreamStream = upstreamResponse.GetResponseStream())
                    {
                        upstreamStream.CopyTo(memoryStream);
                    }
                    responseData = memoryStream.ToArray();
                }

                // Write to cache asynchronously
                if (responseData.Length > 0)
                {
                    ThreadPool.QueueUserWorkItem(_ => WriteToBaselayerCache(cachePath, responseData));
                }

                ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                ctx.Response.ContentType = "image/png";
                // ctx.Response.ContentLength = responseData.Length;
                ctx.Response.Headers.Add("X-Cache", "MISS");
                ctx.Response.Headers.Add("Cache-Control", "public, max-age=2592000");
                await ctx.Response.OutputStream.WriteAsync(responseData, 0, responseData.Length);
            }
            catch (System.Net.Sockets.SocketException)
            {
                // Client disconnected - silently ignore
                responseClosed = true;
            }
            catch (WebException)
            {
                // Upstream error - try to send error response
                try
                {
                    if (!responseClosed)
                        ctx.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                }
                catch { }
            }
            catch (InvalidOperationException)
            {
                // Response already sent - ignore
                responseClosed = true;
            }
            catch (Exception)
            {
                // Other error - try to send error response
                try
                {
                    if (!responseClosed)
                        ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                }
                catch { }
            }
            finally
            {
                upstreamResponse?.Close();
                if (!responseClosed)
                {
                    // EmbedIO manages stream lifecycle
                }
            }
        }

        private async Task ProcessRequestAsync(IHttpContext ctx)
        {
            var request = ctx.Request;
            var response = ctx.Response;

            // Note: CORS is handled by EmbedIO's WithCors() middleware
            // Note: OPTIONS requests are handled by EmbedIO's CORS middleware

            // ✅ Handle HEAD requests for file existence checks (avoids 404 console errors)
            if (request.HttpMethod == "HEAD")
            {
                try
                {
                    string headCommand = ctx.Request.Url.AbsolutePath.Substring(1);
                    KneeboardLogger.ServerDebug($"Request for: {headCommand}");

                    // Replace forward slashes with backslashes for Windows path compatibility
                    headCommand = headCommand.Replace('/', '\\');
                    string headFilePath = Path.Combine(_rootDirectory, headCommand);
                    KneeboardLogger.ServerDebug($"Full path: {headFilePath}");

                    // Case-insensitive file lookup
                    string actualFilePath = FindFileCaseInsensitive(headFilePath);

                    if (actualFilePath != null)
                    {
                        KneeboardLogger.ServerDebug($"File found: {actualFilePath}");
                        response.StatusCode = (int)HttpStatusCode.OK;
                        response.ContentType = _mimeTypeMappings.TryGetValue(Path.GetExtension(actualFilePath), out string headMime) ? headMime : "application/octet-stream";
                    }
                    else
                    {
                        KneeboardLogger.ServerDebug($"File NOT found: {headCommand}");
                        response.StatusCode = (int)HttpStatusCode.NotFound;
                    }
                }
                catch (Exception ex)
                {
                    KneeboardLogger.ServerError($"Exception: {ex.Message}");
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                }
                return;
            }

            string command = ctx.Request.Url.AbsolutePath;

            command = command.Substring(1);
            KneeboardLogger.Server($"{ctx.Request.HttpMethod} /{command}");

            if (string.IsNullOrEmpty(command))
            {
                foreach (string indexFile in _indexFiles)
                {
                    if (System.IO.File.Exists(Path.Combine(_rootDirectory, indexFile)))
                    {
                        command = indexFile;
                        break;
                    }
                }
            }
            else if (command == "checkServerConnection")
            {
                await ResponseStringAsync(ctx, "true");
            }
            else if (command == "setNavlogValues")
            {
                string postedText = await GetPostedTextAsync(ctx);
                KneeboardLogger.Server($"Server received navlog POST, length: {postedText.Length}");

                // Store the new values with timestamp
                values = postedText;
                valuesTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Properties.Settings.Default.values = values;
                Properties.Settings.Default.valuesTimestamp = valuesTimestamp;
                Properties.Settings.Default.Save();

                KneeboardLogger.Server($"Server stored navlog data, new length: {values.Length}, timestamp: {valuesTimestamp}");
                await ResponseStringAsync(ctx, valuesTimestamp.ToString());
            }
            else if (command == "synchronizeFlightplan")
            {
                string flightplan = Kneeboard_Server.syncFlightplan();
                if (flightplan != "")
                {
                    await ResponseStringAsync(ctx, flightplan);
                    // Don't clear flightplan - keep it available for map auto-load
                }
                else
                {
                    await ResponseStringAsync(ctx, "");
                }
            }
            else if (command == "getNavlogValues")
            {
                string navlogData = Properties.Settings.Default.values ?? "";
                long timestamp = Properties.Settings.Default.valuesTimestamp;
                KneeboardLogger.Server($"Server sending navlog data, length: {navlogData.Length}, timestamp: {timestamp}");

                // Send data with timestamp as header
                ctx.Response.Headers.Add("X-Navlog-Timestamp", timestamp.ToString());
                await ResponseStringAsync(ctx, navlogData);
            }
            else if (command == "getNavlogTimestamp")
            {
                long timestamp = Properties.Settings.Default.valuesTimestamp;
                await ResponseStringAsync(ctx, timestamp.ToString());
            }
            else if (command == "clearNavlogValues")
            {
                KneeboardLogger.Server("Server clearing navlog data");
                values = "";
                valuesTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Properties.Settings.Default.values = "";
                Properties.Settings.Default.valuesTimestamp = valuesTimestamp;
                Properties.Settings.Default.Save();
                KneeboardLogger.Server($"Server navlog data cleared, timestamp: {valuesTimestamp}");
                await ResponseStringAsync(ctx, "OK");
            }
            else if (command == "getFlightplan")
            {
                if (Kneeboard_Server.flightplan != null)
                {
                    // Check if flightplan needs enrichment (no procedures or empty procedures)
                    string enrichedFlightplan = Kneeboard_Server.flightplan;
                    if (Kneeboard_Server.FlightplanNeedsEnrichment(enrichedFlightplan))
                    {
                        KneeboardLogger.Server("Enriching flightplan with procedures on demand...");
                        enrichedFlightplan = Kneeboard_Server.EnrichFlightplanWithProceduresAsync(enrichedFlightplan).GetAwaiter().GetResult();
                        // Update cached version if enrichment was successful
                        if (!Kneeboard_Server.FlightplanNeedsEnrichment(enrichedFlightplan))
                        {
                            Kneeboard_Server.flightplan = enrichedFlightplan;
                            Kneeboard_Server.SaveFlightplanDataToSettings();
                            KneeboardLogger.Server("Flightplan enriched and cached");
                        }
                    }
                    await ResponseStringAsync(ctx, enrichedFlightplan);
                }
                else
                {
                    await ResponseStringAsync(ctx, "");
                }
            }
            else if (command == "clearFlightplan")
            {
                // Full clear: removes everything including SimBrief cache
                Kneeboard_Server.flightplan = null;
                Kneeboard_Server.cachedSimbriefTimeGenerated = null;  // Reset SimBrief timestamp cache
                Kneeboard_Server.simbriefOFPData = null;              // Reset OFP data

                // Clear persisted flightplan data
                Kneeboard_Server.ClearPersistedFlightplanData();

                // Also clear navlog data
                Properties.Settings.Default.values = "";
                Properties.Settings.Default.valuesTimestamp = 0;
                Properties.Settings.Default.Save();
                KneeboardLogger.Server("Navlog data and SimBrief cache cleared along with flight plan");

                await ResponseStringAsync(ctx, "cleared");
            }
            else if (command == "clearLocalFlightplan")
            {
                // Soft clear: only clears navlog but keeps SimBrief cache intact
                // Used when user deletes flightplan via map - they can immediately sync again
                Properties.Settings.Default.values = "";
                Properties.Settings.Default.valuesTimestamp = 0;
                Properties.Settings.Default.Save();

                // OFP aus Dokumentenliste entfernen (PDF bleibt, SimBrief cache bleibt)
                Kneeboard_Server.RemoveOFPFromDocumentListOnly();

                // Check if server still has a flightplan from SimBrief
                bool hasServerFlightplan = !string.IsNullOrEmpty(Kneeboard_Server.flightplan);
                KneeboardLogger.Server("Local navlog cleared, OFP hidden, SimBrief cache preserved: " + hasServerFlightplan);

                await ResponseJsonAsync(ctx, $"{{\"cleared\":true,\"hasServerFlightplan\":{hasServerFlightplan.ToString().ToLower()}}}");
            }
            else if (command == "hasFlightplan")
            {
                await ResponseStringAsync(ctx, Kneeboard_Server.flightplan != null ? "true" : "false");
            }
            else if (command == "hasSimbriefId")
            {
                bool hasId = !string.IsNullOrEmpty(Properties.Settings.Default.simbriefId);
                await ResponseJsonAsync(ctx, $"{{\"hasSimbriefId\":{hasId.ToString().ToLower()}}}");
            }
            else if (command == "checkSimbriefUpdate")
            {
                bool updateAvailable = Kneeboard_Server.CheckSimbriefUpdateAvailable();
                await ResponseJsonAsync(ctx, $"{{\"updateAvailable\":{updateAvailable.ToString().ToLower()}}}");
            }
            else if (command == "hasServerFlightplan")
            {
                // Check if server has a flightplan loaded (from background sync or previous session)
                bool hasFlightplan = !string.IsNullOrEmpty(Kneeboard_Server.flightplan);
                await ResponseJsonAsync(ctx, $"{{\"hasFlightplan\":{hasFlightplan.ToString().ToLower()}}}");
            }
            else if (command == "getFlightplanHash")
            {
                // Optimierter Endpoint: Gibt Hash zurück um unnötige Full-Data-Transfers zu vermeiden
                if (!string.IsNullOrEmpty(Kneeboard_Server.flightplan))
                {
                    using (var md5 = System.Security.Cryptography.MD5.Create())
                    {
                        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(Kneeboard_Server.flightplan));
                        string hashString = Convert.ToBase64String(hash).Substring(0, 16);
                        await ResponseJsonAsync(ctx, $"{{\"exists\":true,\"hash\":\"{hashString}\"}}");
                    }
                }
                else
                {
                    await ResponseJsonAsync(ctx, "{\"exists\":false}");
                }
            }
            else if (command.StartsWith("api/procedure/", StringComparison.OrdinalIgnoreCase))
            {
                // Get SID/STAR procedure details: api/procedure/{airport}/{type}/{name}
                // Example: api/procedure/EDDM/SID/GIVMI1N
                await HandleProcedureRequest(ctx, command.Substring("api/procedure/".Length));
                return;
            }
            else if (command.StartsWith("api/procedures/", StringComparison.OrdinalIgnoreCase))
            {
                // Get all SID/STAR procedures for an airport: api/procedures/{airport}
                // Example: api/procedures/EDDM
                await HandleProceduresListRequest(ctx, command.Substring("api/procedures/".Length));
                return;
            }
            else if (command.StartsWith("api/metar/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleNoaaProxy(ctx, command.Substring("api/metar/".Length), false);
                return;
            }
            else if (command.StartsWith("api/taf/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleNoaaProxy(ctx, command.Substring("api/taf/".Length), true);
                return;
            }
            else if (command == "api/boundaries/vatsim")
            {
                await HandleVatsimBoundariesProxy(ctx);
                return;
            }
            else if (command == "api/boundaries/ivao")
            {
                // IVAO bietet KEINE öffentliche API für FIR-Boundaries!
                // Little Navmap ist die einzige funktionierende Quelle
                await HandleIvaoBoundariesProxy(ctx);
                return;
            }
            else if (command == "api/boundaries/vatsim-tracon")
            {
                await HandleVatsimTraconBoundariesProxy(ctx);
                return;
            }
            else if (command == "api/vatspy/firnames")
            {
                await HandleVatspyFirNamesProxy(ctx);
                return;
            }
            else if (command == "synchronizeControllers")
            {
                try
                {
                    using (var client = new WebClient())
                    {
                        client.Encoding = Encoding.UTF8;
                        string controllers = client.DownloadString("https://data.vatsim.net/v3/vatsim-data.json");
                        await ResponseJsonAsync(ctx, controllers);
                    }
                }
                catch (Exception ex)
                {
                    KneeboardLogger.BoundariesError($"VATSIM sync error: {ex.Message}");
                    ctx.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                    // EmbedIO manages stream lifecycle
                }
                return;
            }
            else if (command == "synchronizeTransceivers")
            {
                try
                {
                    using (var client = new WebClient())
                    {
                        client.Encoding = Encoding.UTF8;
                        string transceivers = client.DownloadString("https://data.vatsim.net/v3/transceivers-data.json");
                        await ResponseJsonAsync(ctx, transceivers);
                    }
                }
                catch (Exception ex)
                {
                    KneeboardLogger.BoundariesError($"VATSIM transceivers sync error: {ex.Message}");
                    ctx.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                    // EmbedIO manages stream lifecycle
                }
                return;
            }
            else if (command == "synchronizeIVAO")
            {
                try
                {
                    using (var client = new WebClient())
                    {
                        client.Encoding = Encoding.UTF8;
                        string controllers = client.DownloadString("https://api.ivao.aero/v2/tracker/whazzup");
                        await ResponseJsonAsync(ctx, controllers);
                    }
                }
                catch (Exception ex)
                {
                    KneeboardLogger.BoundariesError($"IVAO sync error: {ex.Message}");
                    ctx.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                    // EmbedIO manages stream lifecycle
                }
                return;
            }
            // ===== HYBRID-ANSATZ: Vorverarbeitete Piloten-Daten =====
            else if (command == "api/pilots/vatsim")
            {
                await HandleVatsimPilots(ctx);
                return;
            }
            else if (command == "api/pilots/ivao")
            {
                await HandleIvaoPilots(ctx);
                return;
            }
            // ===== HYBRID-ANSATZ: Boundaries mit vorberechneten Bounding-Boxes =====
            else if (command == "api/boundaries/vatsim")
            {
                await HandleVatsimBoundariesWithBbox(ctx);
                return;
            }
            else if (command == "api/boundaries/ivao")
            {
                await HandleIvaoBoundariesWithBbox(ctx);
                return;
            }
            else if (command == "api/boundaries/ivao-offline")
            {
                await HandleIvaoOfflineFir(ctx);
                return;
            }
            else if (command == "api/boundaries/vatsim-offline")
            {
                await HandleVatsimOfflineFir(ctx);
                return;
            }
            else if (command.StartsWith("api/openaip/iata-to-icao/", StringComparison.OrdinalIgnoreCase))
            {
                // Convert IATA to ICAO: api/openaip/iata-to-icao/JFK -> KJFK
                var iataCode = command.Substring("api/openaip/iata-to-icao/".Length).ToUpperInvariant();
                await HandleIataToIcao(ctx, iataCode);
                return;
            }
            else if (command == "api/openaip/iata-icao-mapping")
            {
                // Return full IATA->ICAO mapping for frontend caching
                await HandleIataIcaoMapping(ctx);
                return;
            }
            else if (command.StartsWith("api/openaip/airport-by-icao/", StringComparison.OrdinalIgnoreCase))
            {
                // Extract ICAO code from URL: api/openaip/airport-by-icao/KMIA
                var icaoCode = command.Substring("api/openaip/airport-by-icao/".Length).ToUpperInvariant();
                await HandleOpenAipAirportByIcao(ctx, icaoCode);
                return;
            }
            else if (command.StartsWith("api/openaip/runway/", StringComparison.OrdinalIgnoreCase))
            {
                // Extract ICAO and runway: api/openaip/runway/KPDX/10L
                var path = command.Substring("api/openaip/runway/".Length).TrimEnd('/');
                var parts = path.Split('/');
                if (parts.Length >= 2)
                {
                    var icaoCode = parts[0].ToUpperInvariant();
                    var runwayId = string.Join("/", parts.Skip(1)).ToUpperInvariant();
                    await HandleOpenAipRunway(ctx, icaoCode, runwayId);
                }
                else
                {
                    ctx.Response.StatusCode = 400;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Format: /api/openaip/runway/{icao}/{runway}\"}");
                }
                return;
            }
            else if (command.StartsWith("api/openaip/airports-batch", StringComparison.OrdinalIgnoreCase))
            {
                // Batch endpoint: api/openaip/airports-batch?dep=KMIA&arr=LIPE
                await HandleOpenAipAirportsBatch(ctx);
                return;
            }
            else if (command.StartsWith("api/openaip/airports", StringComparison.OrdinalIgnoreCase))
            {
                await HandleOpenAipProxy(ctx, "airports");
                return;
            }
            else if (command.StartsWith("api/openaip/navaids", StringComparison.OrdinalIgnoreCase))
            {
                await HandleOpenAipProxy(ctx, "navaids");
                return;
            }
            else if (command == "api/favorites")
            {
                // GET: Load favorites, POST: Save favorites
                if (ctx.Request.HttpMethod == "GET")
                {
                    await HandleGetFavorites(ctx);
                }
                else if (ctx.Request.HttpMethod == "POST")
                {
                    await HandleSaveFavorites(ctx);
                }
                return;
            }
            else if (command == "api/user-ids")
            {
                // Return configured VATSIM CID and IVAO VID for hiding own aircraft
                var userIds = new {
                    vatsimCid = Properties.Settings.Default.vatsimCid ?? "",
                    ivaoVid = Properties.Settings.Default.ivaoVid ?? ""
                };
                await ResponseJsonAsync(ctx, Newtonsoft.Json.JsonConvert.SerializeObject(userIds));
                return;
            }
            else if (command.StartsWith("api/openaip/reporting-points", StringComparison.OrdinalIgnoreCase))
            {
                await HandleOpenAipProxy(ctx, "reporting-points");
                return;
            }
            else if (command.StartsWith("api/openaip/tiles/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleOpenAipProxy(ctx, "tiles");
                return;
            }
            // Baselayer Tile Proxy with Caching
            else if (command.StartsWith("api/tiles/osm/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleBaselayerTileProxy(ctx, "osm");
                return;
            }
            else if (command.StartsWith("api/tiles/google-hybrid/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleBaselayerTileProxy(ctx, "google-hybrid");
                return;
            }
            else if (command.StartsWith("api/tiles/google-satellite/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleBaselayerTileProxy(ctx, "google-satellite");
                return;
            }
            else if (command.StartsWith("api/tiles/opentopomap/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleBaselayerTileProxy(ctx, "opentopomap");
                return;
            }
            else if (command == "api/elevation")
            {
                await HandleElevationProxy(ctx);
                return;
            }
            else if (command.StartsWith("api/dfs/tiles/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleDfsProxy(ctx);
                return;
            }
            else if (command.StartsWith("api/ofm/tiles/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleOfmProxy(ctx);
                return;
            }
            else if (command.StartsWith("api/nominatim/search", StringComparison.OrdinalIgnoreCase))
            {
                await HandleNominatimProxy(ctx);
                return;
            }
            else if (command.StartsWith("api/wind/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleWindProxy(ctx);
                return;
            }
            else if (command == "api/simconnect/position")
            {
                await HandleSimConnectPositionRequest(ctx);
                return;
            }
            else if (command == "api/simconnect/status")
            {
                await HandleSimConnectStatusRequest(ctx);
                return;
            }
            else if (command == "api/simconnect/teleport")
            {
                await HandleSimConnectTeleportRequest(ctx);
                return;
            }
            else if (command == "api/simconnect/pause")
            {
                await HandleSimConnectPauseRequest(ctx);
                return;
            }
            else if (command == "api/simconnect/radio/frequency")
            {
                await HandleSimConnectRadioFrequencyRequest(ctx);
                return;
            }
            // Procedure API Endpoints (SID/STAR data from Little Navmap)
            else if (command.StartsWith("api/procedures/sids/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleGetSIDListRequest(ctx, command);
                return;
            }
            else if (command.StartsWith("api/procedures/stars/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleGetSTARListRequest(ctx, command);
                return;
            }
            else if (command.StartsWith("api/procedures/approaches/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleGetApproachListRequest(ctx, command);
                return;
            }
            else if (command.StartsWith("api/procedures/approach/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleGetApproachRequest(ctx, command);
                return;
            }
            else if (command.StartsWith("api/procedures/procedure/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleGetProcedureRequest(ctx, command);
                return;
            }
            else if (command == "api/procedures/status")
            {
                await HandleProcedureStatusRequest(ctx);
                return;
            }
            else if (command.StartsWith("api/procedures/debug/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleProcedureDebugRequest(ctx, command);
                return;
            }
            else if (command == "api/procedures/allprocedures")
            {
                await HandleAllProceduresDebugRequest(ctx);
                return;
            }
            else if (command == "api/procedures/folderinfo")
            {
                await HandleNavdataFolderInfoRequest(ctx);
                return;
            }
            else if (command == "api/procedures/simbrief")
            {
                await HandleSimbriefProceduresRequest(ctx);
                return;
            }
            else if (command == "api/procedures/simbrief/sid")
            {
                await HandleSimbriefSidRequest(ctx);
                return;
            }
            else if (command == "api/procedures/simbrief/star")
            {
                await HandleSimbriefStarRequest(ctx);
                return;
            }
            // ILS API Endpoints
            else if (command.StartsWith("api/ils/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleIlsRequest(ctx, command);
                return;
            }
            // Navigraph API Endpoints
            else if (command == "api/navigraph/status")
            {
                await HandleNavigraphStatusRequest(ctx);
                return;
            }
            else if (command.StartsWith("api/navigraph/runway/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleRunwayRequest(ctx, command);
                return;
            }
            else if (command.StartsWith("api/navigraph/runways/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleRunwaysRequest(ctx, command);
                return;
            }
            else if (command.StartsWith("api/navigraph/airport/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleAirportRequest(ctx, command);
                return;
            }
            else if (command.StartsWith("api/navigraph/waypoint/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleWaypointRequest(ctx, command);
                return;
            }
            else if (command.StartsWith("api/navigraph/navaid/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleNavaidRequest(ctx, command);
                return;
            }
            else if (command.StartsWith("api/navigraph/sids/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleSIDsRequest(ctx, command);
                return;
            }
            else if (command.StartsWith("api/navigraph/stars/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleSTARsRequest(ctx, command);
                return;
            }
            else if (command.StartsWith("api/navigraph/approaches/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleApproachesRequest(ctx, command);
                return;
            }
            else if (command.StartsWith("api/navigraph/procedure/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleNavigraphProcedureRequest(ctx, command);
                return;
            }
            else if (command.StartsWith("api/navigraph/approach-debug/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleApproachDebugRequest(ctx, command);
                return;
            }
            else if (command.StartsWith("api/navigraph/approach-test/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleApproachTestRequest(ctx, command);
                return;
            }
            else if (command.StartsWith("api/navigraph/approach-legs/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleApproachLegsRequest(ctx, command);
                return;
            }
            else if (command.StartsWith("api/navigraph/approach-diag/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleApproachDiagRequest(ctx, command);
                return;
            }
            else if (command == "api/debug/config" && ctx.Request.HttpMethod == "GET")
            {
                await HandleGetDebugConfig(ctx);
                return;
            }
            else if (command == "api/debug/config" && ctx.Request.HttpMethod == "POST")
            {
                await HandleSetDebugConfig(ctx);
                return;
            }
            else if (command == "api/log" && ctx.Request.HttpMethod == "POST")
            {
                await HandleClientLogRequest(ctx);
                return;
            }
            else if (command == "getDocumentsList")
            {
                string documentsString = _kneeboardServer.GetFileList();
                await ResponseStringAsync(ctx, documentsString);
            }
            else if (command == "getMapLayers")
            {
                try
                {
                    string layersFilePath = Path.Combine(_rootDirectory, "map-layers.json");
                    if (System.IO.File.Exists(layersFilePath))
                    {
                        string layersJson = System.IO.File.ReadAllText(layersFilePath, Encoding.UTF8);
                        await ResponseJsonAsync(ctx, layersJson);
                    }
                    else
                    {
                        ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        await ResponseJsonAsync(ctx, "{\"error\":\"map-layers.json not found\"}");
                    }
                }
                catch (Exception ex)
                {
                    KneeboardLogger.ServerError($"Error loading map layers: {ex.Message}");
                    ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    await ResponseJsonAsync(ctx, "{\"error\":\"Failed to load map layers\"}");
                }
                return;
            }
            else if (command.StartsWith("getDocumentText"))
            {
                string[] parts = command.Split(new[] { '-' }, 2);
                if (parts.Length < 2)
                {
                    ctx.Response.StatusCode = 400;
                    // ctx.Response.ContentLength = 0;
                    // EmbedIO manages stream   // ✅
                    return;
                }

                string documentNameEncoded = parts[1];
                string documentName = Uri.UnescapeDataString(documentNameEncoded);

                KneeboardLogger.Server("Dokument-Request: " + documentName);

                KneeboardFile fileEntry = null;

                fileEntry = _kneeboardServer.filesList
                    .FirstOrDefault(r => string.Equals(r.Name, documentName, StringComparison.OrdinalIgnoreCase));

                if (fileEntry == null && _kneeboardServer.foldersList != null)
                {
                    foreach (var folder in _kneeboardServer.foldersList)
                    {
                        if (folder?.Files == null) continue;
                        fileEntry = folder.Files.FirstOrDefault(r =>
                            string.Equals(r.Name, documentName, StringComparison.OrdinalIgnoreCase));
                        if (fileEntry != null)
                        {
                            break;
                        }
                    }
                }

                if (fileEntry == null)
                {
                    ctx.Response.StatusCode = 404;
                    // ctx.Response.ContentLength = 0;
                    // EmbedIO manages stream   // ✅
                    return;
                }

                byte[] pdfFile;
                try
                {
                    pdfFile = System.IO.File.ReadAllBytes(fileEntry.Path);
                }
                catch (Exception)
                {
                    ctx.Response.StatusCode = 500;
                    // ctx.Response.ContentLength = 0;
                    // EmbedIO manages stream   // ✅
                    return;
                }

                // Erfolgspfad - Send PDF as base64 text
                string base64 = Convert.ToBase64String(pdfFile);
                await ctx.SendStringAsync(base64, "text/plain", Encoding.UTF8);
                return;
            }
            else
            {
                // Serve static files
                string filePath = Path.Combine(_rootDirectory, command.Replace('/', '\\'));

                // Case-insensitive file lookup for Windows compatibility
                if (!File.Exists(filePath))
                {
                    // Try to find file with different case
                    string directory = Path.GetDirectoryName(filePath);
                    string fileName = Path.GetFileName(filePath);
                    if (Directory.Exists(directory))
                    {
                        var match = Directory.GetFiles(directory)
                            .FirstOrDefault(f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            filePath = match;
                        }
                    }
                }

                if (File.Exists(filePath))
                {
                    try
                    {
                        // Read file completely first (EmbedIO handles response better this way)
                        byte[] fileContent = File.ReadAllBytes(filePath);
                        string contentType = _mimeTypeMappings.TryGetValue(Path.GetExtension(filePath), out string mime) ? mime : "application/octet-stream";

                        // For text-based files, use SendStringAsync
                        string ext = Path.GetExtension(filePath).ToLowerInvariant();
                        if (ext == ".html" || ext == ".htm" || ext == ".css" || ext == ".js" || ext == ".json" || ext == ".xml" || ext == ".txt" || ext == ".svg")
                        {
                            string textContent = Encoding.UTF8.GetString(fileContent);
                            await ctx.SendStringAsync(textContent, contentType, Encoding.UTF8);
                        }
                        else
                        {
                            // For binary files (images, etc.), write directly to OutputStream
                            ctx.Response.ContentType = contentType;
                            await ctx.Response.OutputStream.WriteAsync(fileContent, 0, fileContent.Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        KneeboardLogger.Server($"Error serving file {filePath}: {ex.Message}");
                        ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    }
                }
                else
                {
                    ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
                }
            }

        }

        private static async Task<string> GetPostedTextAsync(IHttpContext ctx)
        {
            using (var reader = ctx.OpenRequestText())
            {
                return await reader.ReadToEndAsync();
            }
        }

        private void Initialize(string path, int port)
        {
            // Increase connection limit for better tile loading performance
            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;

            this._rootDirectory = path;
            this._port = port;

            // Start EmbedIO WebServer (fire-and-forget, runs in background)
            _ = StartServerAsync();
        }
    }

}
