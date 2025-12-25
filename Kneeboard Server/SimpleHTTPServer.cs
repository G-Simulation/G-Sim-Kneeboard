// MIT License - Copyright (c) 2016 Can Güney Aksakalli
// https://aksakalli.github.io/2014/02/24/simple-http-server-with-csparp.html

using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Kneeboard_Server.Kneeboard_Server;

namespace Kneeboard_Server
{
    public class SimpleHTTPServer
    {
        private Kneeboard_Server _kneeboardServer;
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

        // OpenAIP Cache Configuration
        private static readonly string CACHE_DIR = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "openaip");
        private static readonly TimeSpan CACHE_TTL = TimeSpan.FromDays(7);
        private static readonly object _cacheLock = new object();

        // FIR Boundaries - Permanente lokale Speicherung mit 7-Tage Auto-Update
        private static readonly string BOUNDARIES_CACHE_DIR = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "boundaries");
        private static readonly string BOUNDARIES_DATA_DIR = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "boundaries");
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
        private static readonly string FAVORITES_FILE = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "pilot_favorites.json");
        private static readonly object _favoritesLock = new object();

        // Baselayer Tile Cache Configuration
        private static readonly string BASELAYER_CACHE_DIR = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "tiles");
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
        Thread _serverThread;
        private string _rootDirectory;
        HttpListener _listener;
        private int _port;
        public string values = "";
        public long valuesTimestamp = 0;



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



        private volatile bool _isRunning = true;

        /// <summary>
        /// Stop server and dispose all functions.
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping server: {ex.Message}");
            }
        }

        private void Listen()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{_port}/");

            try
            {
                _listener.Start();
            }
            catch (HttpListenerException ex)
            {
                Console.WriteLine($"Failed to start HTTP listener: {ex.Message}");
                Console.WriteLine("Try running as Administrator or use a different port.");
                return;
            }

            while (_isRunning)
            {
                try
                {
                    var context = _listener.GetContext();
                    // Handle each request in a thread pool thread for better concurrency
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            Process(context);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error processing request: {ex.Message}");
                            try
                            {
                                context.Response.StatusCode = 500;
                                context.Response.OutputStream.Close();
                            }
                            catch { }
                        }
                    });
                }
                catch (HttpListenerException)
                {
                    // Listener was stopped
                    if (!_isRunning) break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Server error: {ex.Message}");
                }
            }
        }

        private void ResponseString(HttpListenerContext context, string text)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(text ?? string.Empty);
                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending response: {ex.Message}");
            }
            finally
            {
                try { context.Response.OutputStream.Close(); } catch { }
            }
        }

        private void ResponseJson(HttpListenerContext context, string json)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(json ?? "{}");
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending JSON response: {ex.Message}");
            }
            finally
            {
                try { context.Response.OutputStream.Close(); } catch { }
            }
        }

        private void HandleNoaaProxy(HttpListenerContext context, string icaoRaw, bool isTaf)
        {
            string icao = (icaoRaw ?? string.Empty).Trim().ToUpperInvariant();
            if (icao.Length != 4 || !icao.All(char.IsLetter))
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.OutputStream.Close();
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
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = buffer.Length;
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                }
            }
            catch (WebException ex)
            {
                var httpResponse = ex.Response as HttpWebResponse;
                context.Response.StatusCode = httpResponse != null
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
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            finally
            {
                context.Response.OutputStream.Close();
            }
        }
        private void HandleElevationProxy(HttpListenerContext context)
        {
            var request = context.Request;

            if (request.HttpMethod != "POST")
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                context.Response.OutputStream.Close();
                return;
            }

            string requestBody;
            try
            {
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    requestBody = reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading elevation request body: {ex.Message}");
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.OutputStream.Close();
                return;
            }

            try
            {
                // Parse the incoming request to extract coordinates
                // Expected format: {"locations": [{"latitude": XX, "longitude": XX}]}
                dynamic requestData = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(requestBody);
                var locations = requestData.locations;

                if (locations == null || locations.Count == 0)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    var errorBuffer = Encoding.UTF8.GetBytes("{\"error\":\"No locations provided\"}");
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = errorBuffer.Length;
                    context.Response.OutputStream.Write(errorBuffer, 0, errorBuffer.Length);
                    return;
                }

                // Build comma-separated lat/lng lists for Open-Meteo API
                var latitudes = new List<string>();
                var longitudes = new List<string>();
                foreach (var loc in locations)
                {
                    latitudes.Add(((double)loc.latitude).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    longitudes.Add(((double)loc.longitude).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                // Use Open-Meteo API (more reliable than open-elevation.com)
                string apiUrl = $"https://api.open-meteo.com/v1/elevation?latitude={string.Join(",", latitudes)}&longitude={string.Join(",", longitudes)}";

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

                    // Convert to open-elevation format: {"results": [{"latitude": XX, "longitude": XX, "elevation": XX}]}
                    var results = new List<object>();
                    for (int i = 0; i < locations.Count && i < elevations.Count; i++)
                    {
                        results.Add(new
                        {
                            latitude = (double)locations[i].latitude,
                            longitude = (double)locations[i].longitude,
                            elevation = (double)elevations[i]
                        });
                    }

                    string payload = Newtonsoft.Json.JsonConvert.SerializeObject(new { results = results });
                    var buffer = Encoding.UTF8.GetBytes(payload);

                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = buffer.Length;
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                }
            }
            catch (WebException ex)
            {
                var httpResponse = ex.Response as HttpWebResponse;
                context.Response.StatusCode = httpResponse != null
                    ? (int)httpResponse.StatusCode
                    : (int)HttpStatusCode.BadGateway;

                Console.WriteLine($"Elevation Proxy Error: {ex.Message}");
                if (httpResponse != null)
                {
                    Console.WriteLine($"Upstream Status: {httpResponse.StatusCode}");
                }

                string payload = "{\"error\":\"Unable to reach elevation service\"}";
                var buffer = Encoding.UTF8.GetBytes(payload);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Elevation Proxy Error: {ex.Message}");
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                string payload = "{\"error\":\"Internal server error\"}";
                var buffer = Encoding.UTF8.GetBytes(payload);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            finally
            {
                context.Response.OutputStream.Close();
            }
        }

        private void HandleDfsProxy(HttpListenerContext context)
        {
            var request = context.Request;
            var path = request.Url.AbsolutePath;

            // Extract the tile path (z/x/y.png) from the URL
            var idx = path.IndexOf("/api/dfs/tiles/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.OutputStream.Close();
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
                    context.Response.StatusCode = (int)upstreamResponse.StatusCode;
                    context.Response.ContentType = upstreamResponse.ContentType ?? "image/png";

                    using (var upstreamStream = upstreamResponse.GetResponseStream())
                    {
                        upstreamStream.CopyTo(context.Response.OutputStream);
                    }
                }
            }
            catch (WebException ex)
            {
                var httpResponse = ex.Response as HttpWebResponse;
                context.Response.StatusCode = httpResponse != null
                    ? (int)httpResponse.StatusCode
                    : (int)HttpStatusCode.BadGateway;

                Console.WriteLine($"DFS Proxy Error: {ex.Message}");
                Console.WriteLine($"Requested URL: {targetUrl}");
                if (httpResponse != null)
                {
                    Console.WriteLine($"Upstream Status: {httpResponse.StatusCode}");
                }

                // Return empty PNG for failed tile requests
                context.Response.ContentType = "image/png";
            }
            finally
            {
                context.Response.OutputStream.Close();
            }
        }

        private void HandleOfmProxy(HttpListenerContext context)
        {
            var request = context.Request;
            var path = request.Url.AbsolutePath;

            // Extract the tile path (z/x/y.png) from the URL
            var idx = path.IndexOf("/api/ofm/tiles/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.OutputStream.Close();
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
                    context.Response.StatusCode = (int)upstreamResponse.StatusCode;
                    context.Response.ContentType = upstreamResponse.ContentType ?? "image/png";

                    using (var upstreamStream = upstreamResponse.GetResponseStream())
                    {
                        upstreamStream.CopyTo(context.Response.OutputStream);
                    }
                }
            }
            catch (WebException ex)
            {
                var httpResponse = ex.Response as HttpWebResponse;
                context.Response.StatusCode = httpResponse != null
                    ? (int)httpResponse.StatusCode
                    : (int)HttpStatusCode.BadGateway;

                Console.WriteLine($"OFM Proxy Error: {ex.Message}");
                Console.WriteLine($"Requested URL: {targetUrl}");

                // Return empty PNG for failed tile requests
                context.Response.ContentType = "image/png";
            }
            finally
            {
                context.Response.OutputStream.Close();
            }
        }

        private void HandleVatsimBoundariesProxy(HttpListenerContext context)
        {
            try
            {
                // Check memory cache first
                lock (_boundariesCacheLock)
                {
                    if (_cachedVatsimBoundaries != null && (DateTime.Now - _vatsimBoundariesCacheTime) < BOUNDARIES_CACHE_TTL)
                    {
                        context.Response.AddHeader("X-Cache", "HIT");
                        ResponseJson(context, _cachedVatsimBoundaries);
                        return;
                    }
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
                        context.Response.AddHeader("X-Cache", "DATA");
                        Console.WriteLine("VATSIM Boundaries: loaded from data directory");
                        ResponseJson(context, data);
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
                        string cachedData = File.ReadAllText(diskCachePath);
                        lock (_boundariesCacheLock)
                        {
                            _cachedVatsimBoundaries = cachedData;
                            _vatsimBoundariesCacheTime = fileInfo.LastWriteTime;
                        }
                        context.Response.AddHeader("X-Cache", "DISK");
                        Console.WriteLine("VATSIM Boundaries: loaded from disk cache");
                        ResponseJson(context, cachedData);
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
                        Console.WriteLine("VATSIM Boundaries: saved to disk cache");
                    }
                    catch (Exception cacheEx)
                    {
                        Console.WriteLine($"VATSIM Boundaries: failed to save disk cache: {cacheEx.Message}");
                    }

                    context.Response.AddHeader("X-Cache", "MISS");
                    ResponseJson(context, boundaries);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VATSIM Boundaries fetch error: {ex.Message}");

                // Try to return cached data even if expired
                lock (_boundariesCacheLock)
                {
                    if (_cachedVatsimBoundaries != null)
                    {
                        context.Response.AddHeader("X-Cache", "STALE");
                        ResponseJson(context, _cachedVatsimBoundaries);
                        return;
                    }
                }

                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                ResponseJson(context, "{\"error\":\"Unable to fetch VATSIM boundaries\"}");
            }
        }

        private void HandleVatsimTraconBoundariesProxy(HttpListenerContext context)
        {
            try
            {
                // Check memory cache first
                lock (_boundariesCacheLock)
                {
                    if (_cachedVatsimTraconBoundaries != null && (DateTime.Now - _vatsimTraconBoundariesCacheTime) < BOUNDARIES_CACHE_TTL)
                    {
                        context.Response.AddHeader("X-Cache", "HIT");
                        ResponseJson(context, _cachedVatsimTraconBoundaries);
                        return;
                    }
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
                        context.Response.AddHeader("X-Cache", "DATA");
                        Console.WriteLine("VATSIM TRACON Boundaries: loaded from data directory");
                        ResponseJson(context, data);
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
                        string cachedData = File.ReadAllText(diskCachePath);
                        lock (_boundariesCacheLock)
                        {
                            _cachedVatsimTraconBoundaries = cachedData;
                            _vatsimTraconBoundariesCacheTime = fileInfo.LastWriteTime;
                        }
                        context.Response.AddHeader("X-Cache", "DISK");
                        Console.WriteLine("VATSIM TRACON Boundaries: loaded from disk cache");
                        ResponseJson(context, cachedData);
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
                        Console.WriteLine("VATSIM TRACON Boundaries: saved to disk cache");
                    }
                    catch (Exception cacheEx)
                    {
                        Console.WriteLine($"VATSIM TRACON Boundaries: failed to save disk cache: {cacheEx.Message}");
                    }

                    context.Response.AddHeader("X-Cache", "MISS");
                    ResponseJson(context, boundaries);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VATSIM TRACON Boundaries fetch error: {ex.Message}");

                // Try to return cached data even if expired
                lock (_boundariesCacheLock)
                {
                    if (_cachedVatsimTraconBoundaries != null)
                    {
                        context.Response.AddHeader("X-Cache", "STALE");
                        ResponseJson(context, _cachedVatsimTraconBoundaries);
                        return;
                    }
                }

                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                ResponseJson(context, "{\"error\":\"Unable to fetch VATSIM TRACON boundaries\"}");
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
                                Console.WriteLine($"TRACON boundaries URL: {url}");
                                return url;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting TRACON release URL: {ex.Message}");
            }

            // Fallback to known working version
            Console.WriteLine("TRACON boundaries: using fallback URL v1.2.1");
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

                    Console.WriteLine("[Boundaries] Starting background update check...");

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

                    Console.WriteLine("[Boundaries] Background update check completed");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Boundaries] Background update error: {ex.Message}");
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
                        Console.WriteLine($"[Boundaries] Migrated {fileName} from cache to data directory");
                    }
                    catch { }
                }

                if (!File.Exists(filePath))
                {
                    needsUpdate = true;
                    Console.WriteLine($"[Boundaries] {fileName}: file missing, downloading...");
                }
            }

            if (!needsUpdate && File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                var age = DateTime.Now - fileInfo.LastWriteTime;
                if (age > BOUNDARIES_CACHE_TTL)
                {
                    needsUpdate = true;
                    Console.WriteLine($"[Boundaries] {fileName}: file is {age.TotalDays:F1} days old, updating...");
                }
                else
                {
                    Console.WriteLine($"[Boundaries] {fileName}: up to date ({age.TotalDays:F1} days old)");

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
                        Console.WriteLine($"[Boundaries] {fileName}: downloaded and saved ({data.Length / 1024}KB)");

                        // Also save to cache dir for compatibility
                        try { File.WriteAllText(cachePath, data); } catch { }

                        // Pre-load into memory cache
                        PreloadBoundaryToCache(fileName, data, DateTime.Now);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Boundaries] {fileName}: download failed - {ex.Message}");

                    // If download fails but old file exists, use it anyway
                    if (File.Exists(filePath))
                    {
                        Console.WriteLine($"[Boundaries] {fileName}: using existing file despite age");
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

        private void HandleVatspyFirNamesProxy(HttpListenerContext context)
        {
            try
            {
                // Check memory cache first
                lock (_boundariesCacheLock)
                {
                    if (_cachedVatspyFirNames != null && (DateTime.Now - _vatspyFirNamesCacheTime) < BOUNDARIES_CACHE_TTL)
                    {
                        context.Response.AddHeader("X-Cache", "HIT");
                        ResponseJson(context, _cachedVatspyFirNames);
                        return;
                    }
                }

                // Check disk cache
                string diskCachePath = Path.Combine(BOUNDARIES_CACHE_DIR, "vatspy_firnames.json");
                if (File.Exists(diskCachePath))
                {
                    var fileInfo = new FileInfo(diskCachePath);
                    if ((DateTime.Now - fileInfo.LastWriteTime) < BOUNDARIES_CACHE_TTL)
                    {
                        string cachedData = File.ReadAllText(diskCachePath);
                        lock (_boundariesCacheLock)
                        {
                            _cachedVatspyFirNames = cachedData;
                            _vatspyFirNamesCacheTime = fileInfo.LastWriteTime;
                        }
                        context.Response.AddHeader("X-Cache", "DISK");
                        Console.WriteLine("VATSpy FIR names: loaded from disk cache");
                        ResponseJson(context, cachedData);
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
                        Console.WriteLine($"VATSpy FIR names: saved to disk cache");
                    }
                    catch (Exception cacheEx)
                    {
                        Console.WriteLine($"VATSpy FIR names: failed to save disk cache: {cacheEx.Message}");
                    }

                    Console.WriteLine($"VATSpy FIR names loaded: {firNames.Count} entries");
                    context.Response.AddHeader("X-Cache", "MISS");
                    ResponseJson(context, jsonResult);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VATSpy FIR names fetch error: {ex.Message}");

                // Try to return cached data even if expired
                lock (_boundariesCacheLock)
                {
                    if (_cachedVatspyFirNames != null)
                    {
                        context.Response.AddHeader("X-Cache", "STALE");
                        ResponseJson(context, _cachedVatspyFirNames);
                        return;
                    }
                }

                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                ResponseJson(context, "{}");
            }
        }

        private void HandleIvaoBoundariesProxy(HttpListenerContext context)
        {
            try
            {
                // Check memory cache first
                lock (_boundariesCacheLock)
                {
                    if (_cachedIvaoBoundaries != null && (DateTime.Now - _ivaoBoundariesCacheTime) < BOUNDARIES_CACHE_TTL)
                    {
                        context.Response.AddHeader("X-Cache", "HIT");
                        ResponseJson(context, _cachedIvaoBoundaries);
                        return;
                    }
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
                        context.Response.AddHeader("X-Cache", "DATA");
                        Console.WriteLine("IVAO Boundaries: loaded from data directory");
                        ResponseJson(context, data);
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
                        string cachedData = File.ReadAllText(diskCachePath);
                        lock (_boundariesCacheLock)
                        {
                            _cachedIvaoBoundaries = cachedData;
                            _ivaoBoundariesCacheTime = fileInfo.LastWriteTime;
                        }
                        context.Response.AddHeader("X-Cache", "DISK");
                        Console.WriteLine("IVAO Boundaries: loaded from disk cache");
                        ResponseJson(context, cachedData);
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
                        Console.WriteLine("IVAO Boundaries: saved to disk cache (GeoJSON format)");
                    }
                    catch (Exception cacheEx)
                    {
                        Console.WriteLine($"IVAO Boundaries: failed to save disk cache: {cacheEx.Message}");
                    }

                    context.Response.AddHeader("X-Cache", "MISS");
                    ResponseJson(context, geoJsonContent);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"IVAO Boundaries fetch error: {ex.Message}");

                // Try to return cached data even if expired
                lock (_boundariesCacheLock)
                {
                    if (_cachedIvaoBoundaries != null)
                    {
                        context.Response.AddHeader("X-Cache", "STALE");
                        ResponseJson(context, _cachedIvaoBoundaries);
                        return;
                    }
                }

                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                ResponseJson(context, "{\"error\":\"Unable to fetch IVAO boundaries\"}");
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
                        Console.WriteLine($"IVAO boundaries URL: {url}");
                        return url;
                    }
                    else
                    {
                        Console.WriteLine("No IVAO files found in directory listing");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting IVAO file list: {ex.Message}");
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
                Console.WriteLine($"Error extracting IVAO ZIP: {ex.Message}");
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

                Console.WriteLine($"IVAO filter: {filteredItems.Count} items with valid geometry (minimized)");
                return "[" + string.Join(",", filteredItems) + "]";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"IVAO filter error: {ex.Message}, returning raw data");
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

                Console.WriteLine($"IVAO -> GeoJSON: {features.Count} features converted");

                // Debug: Log some sample feature IDs for verification
                if (features.Count > 0)
                {
                    var sampleIds = features.Take(20).Select(f => {
                        int idStart = f.IndexOf("\"id\":\"") + 6;
                        int idEnd = f.IndexOf("\"", idStart);
                        return idStart > 5 && idEnd > idStart ? f.Substring(idStart, idEnd - idStart) : "?";
                    });
                    Console.WriteLine($"IVAO Sample IDs: {string.Join(", ", sampleIds)}");
                }

                return "{\"type\":\"FeatureCollection\",\"features\":[" + string.Join(",", features) + "]}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"IVAO GeoJSON conversion error: {ex.Message}");
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

        private void HandleNominatimProxy(HttpListenerContext context)
        {
            var request = context.Request;
            var queryString = request.Url.Query;

            // Check if query is too short (Nominatim blocks very short queries)
            var queryParam = request.QueryString["q"] ?? string.Empty;
            if (queryParam.Length < 2)
            {
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                string payload = "[]"; // Return empty results for very short queries
                var buffer = Encoding.UTF8.GetBytes(payload);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
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
                    context.Response.StatusCode = (int)upstreamResponse.StatusCode;
                    context.Response.ContentType = upstreamResponse.ContentType ?? "application/json";

                    using (var upstreamStream = upstreamResponse.GetResponseStream())
                    {
                        upstreamStream.CopyTo(context.Response.OutputStream);
                    }
                }
            }
            catch (WebException ex)
            {
                var httpResponse = ex.Response as HttpWebResponse;
                context.Response.StatusCode = httpResponse != null
                    ? (int)httpResponse.StatusCode
                    : (int)HttpStatusCode.BadGateway;

                Console.WriteLine($"Nominatim Proxy Error: {ex.Message}");
                Console.WriteLine($"Requested URL: {targetUrl}");
                if (httpResponse != null)
                {
                    Console.WriteLine($"Upstream Status: {httpResponse.StatusCode}");
                }

                // Return JSON error instead of HTML from Nominatim
                string payload = "{\"error\":\"Nominatim service unavailable or request blocked. Try a more specific search query.\"}";
                var buffer = Encoding.UTF8.GetBytes(payload);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            finally
            {
                context.Response.OutputStream.Close();
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

        private void HandleWindProxy(HttpListenerContext context)
        {
            var request = context.Request;
            var path = request.Url.AbsolutePath;
            var queryString = request.Url.Query;

            // Extract the GFS path from /api/wind/gfs{date}/gfs_1p00_{run}z.ascii?...
            var idx = path.IndexOf("/api/wind/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.OutputStream.Close();
                return;
            }

            var gfsPart = path.Substring(idx + "/api/wind/".Length);
            var targetUrl = $"https://nomads.ncep.noaa.gov/dods/{gfsPart}{queryString}";

            Console.WriteLine($"[Wind Proxy] Requesting: {targetUrl}");

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
                            Console.WriteLine($"[Wind Proxy] Redirect {statusCode} without Location header");
                            break;
                        }

                        // Handle relative redirects
                        if (!redirectUrl.IsAbsoluteUri)
                        {
                            redirectUrl = new Uri(new Uri(currentUrl), redirectUrl);
                        }

                        currentUrl = redirectUrl.ToString();
                        Console.WriteLine($"[Wind Proxy] Following {statusCode} redirect to: {currentUrl}");
                        response.Dispose();
                        continue;
                    }

                    break; // Not a redirect, use this response
                }

                Console.WriteLine($"[Wind Proxy] Final Response: {response.StatusCode}, ContentLength: {response.Content.Headers.ContentLength}");

                if (response.IsSuccessStatusCode)
                {
                    var contentTask = response.Content.ReadAsByteArrayAsync();
                    contentTask.Wait();
                    var content = contentTask.Result;

                    context.Response.StatusCode = (int)response.StatusCode;
                    context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "text/plain";
                    context.Response.ContentLength64 = content.Length;
                    context.Response.OutputStream.Write(content, 0, content.Length);
                }
                else
                {
                    Console.WriteLine($"[Wind Proxy] Non-success status: {response.StatusCode}");
                    context.Response.StatusCode = (int)response.StatusCode;
                    context.Response.ContentType = "text/plain";
                }
            }
            catch (AggregateException ae)
            {
                var innerEx = ae.InnerException ?? ae;
                Console.WriteLine($"[Wind Proxy] Error: {innerEx.Message}");
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                context.Response.ContentType = "text/plain";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Wind Proxy] Error: {ex.Message}");
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                context.Response.ContentType = "text/plain";
            }
            finally
            {
                context.Response.OutputStream.Close();
            }
        }

        // ============================================================================
        // SimConnect API Handlers
        // ============================================================================

        private void HandleSimConnectPositionRequest(HttpListenerContext context)
        {
            try
            {
                var position = _kneeboardServer.GetSimConnectPosition();

                if (position.HasValue)
                {
                    var pos = position.Value;

                    var json = new
                    {
                        connected = true,
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
                    ResponseJson(context, jsonString);
                }
                else
                {
                    ResponseJson(context, "{\"connected\":false}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SimConnect API] Position request error: {ex.Message}");
                context.Response.StatusCode = 500;
                ResponseJson(context, "{\"connected\":false,\"error\":\"Internal error\"}");
            }
        }

        private void HandleSimConnectStatusRequest(HttpListenerContext context)
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

                ResponseJson(context, Newtonsoft.Json.JsonConvert.SerializeObject(json));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SimConnect API] Status request error: {ex.Message}");
                context.Response.StatusCode = 500;
                ResponseJson(context, "{\"connected\":false}");
            }
        }

        private void HandleSimConnectTeleportRequest(HttpListenerContext context)
        {
            if (context.Request.HttpMethod != "POST")
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                ResponseJson(context, "{\"success\":false,\"error\":\"Method not allowed\"}");
                return;
            }

            try
            {
                string requestBody = GetPostedText(context.Request);
                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(requestBody);

                double lat = data.lat;
                double lng = data.lng;
                double? altitude = data.altitude != null ? (double?)data.altitude : null;
                double? heading = data.heading != null ? (double?)data.heading : null;
                double? speed = data.speed != null ? (double?)data.speed : null;

                _kneeboardServer.SimConnectTeleport(lat, lng, altitude, heading, speed);

                ResponseJson(context, "{\"success\":true,\"error\":null}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SimConnect API] Teleport error: {ex.Message}");
                context.Response.StatusCode = 500;
                string errorJson = Newtonsoft.Json.JsonConvert.SerializeObject(new { success = false, error = ex.Message });
                ResponseJson(context, errorJson);
            }
        }

        private void HandleSimConnectPauseRequest(HttpListenerContext context)
        {
            if (context.Request.HttpMethod != "POST")
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                ResponseJson(context, "{\"success\":false,\"error\":\"Method not allowed\"}");
                return;
            }

            try
            {
                string requestBody = GetPostedText(context.Request);
                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(requestBody);

                bool paused = data.paused;

                // DUMMY IMPLEMENTATION - MSFS 2024 Pause Events funktionieren nicht!
                // Pause/Resume Events (PAUSE_ON, PAUSE_OFF, PAUSE_TOGGLE, SLEW_TOGGLE)
                // sind alle in MSFS 2024 kaputt via SimConnect.
                // Wir geben einfach success zurück ohne wirklich zu pausieren.
                Console.WriteLine($"[SimConnect API] Pause request received (paused={paused}) - IGNORED (MSFS 2024 bug)");

                string responseJson = Newtonsoft.Json.JsonConvert.SerializeObject(new { success = true, paused = paused });
                ResponseJson(context, responseJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SimConnect API] Pause error: {ex.Message}");
                context.Response.StatusCode = 500;
                string errorJson = Newtonsoft.Json.JsonConvert.SerializeObject(new { success = false, error = ex.Message });
                ResponseJson(context, errorJson);
            }
        }

        private void HandleSimConnectRadioFrequencyRequest(HttpListenerContext context)
        {
            if (context.Request.HttpMethod != "POST")
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                ResponseJson(context, "{\"success\":false,\"error\":\"Method not allowed\"}");
                return;
            }

            try
            {
                string requestBody = GetPostedText(context.Request);
                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(requestBody);

                string radio = data.radio;
                uint frequencyHz = data.frequencyHz;

                _kneeboardServer.SimConnectSetRadioFrequency(radio, frequencyHz);

                ResponseJson(context, "{\"success\":true,\"error\":null}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SimConnect API] Radio frequency error: {ex.Message}");
                context.Response.StatusCode = 500;
                string errorJson = Newtonsoft.Json.JsonConvert.SerializeObject(new { success = false, error = ex.Message });
                ResponseJson(context, errorJson);
            }
        }

        // ============================================================================
        // Procedure API Methods (disabled - navdata integration removed)
        // ============================================================================

        private void HandleGetSIDListRequest(HttpListenerContext context, string command)
        {
            context.Response.StatusCode = 501;
            ResponseJson(context, "{\"error\":\"Navdata API not available. Use /api/procedures/simbrief instead.\"}");
        }

        private void HandleGetSTARListRequest(HttpListenerContext context, string command)
        {
            context.Response.StatusCode = 501;
            ResponseJson(context, "{\"error\":\"Navdata API not available. Use /api/procedures/simbrief instead.\"}");
        }

        private void HandleGetApproachListRequest(HttpListenerContext context, string command)
        {
            context.Response.StatusCode = 501;
            ResponseJson(context, "{\"error\":\"Navdata API not available. Use /api/procedures/simbrief instead.\"}");
        }

        private void HandleGetApproachRequest(HttpListenerContext context, string command)
        {
            context.Response.StatusCode = 501;
            ResponseJson(context, "{\"error\":\"Navdata API not available.\"}");
        }

        private void HandleGetProcedureRequest(HttpListenerContext context, string command)
        {
            context.Response.StatusCode = 501;
            ResponseJson(context, "{\"error\":\"Navdata API not available. Use /api/procedures/simbrief instead.\"}");
        }

        private void HandleProcedureStatusRequest(HttpListenerContext context)
        {
            context.Response.StatusCode = 501;
            ResponseJson(context, "{\"error\":\"Navdata API not available.\"}");
        }

        private void HandleNavdataFolderInfoRequest(HttpListenerContext context)
        {
            context.Response.StatusCode = 501;
            ResponseJson(context, "{\"error\":\"Navdata API not available.\"}");
        }

        private void HandleProcedureDebugRequest(HttpListenerContext context, string command)
        {
            context.Response.StatusCode = 501;
            ResponseJson(context, "{\"error\":\"Navdata API not available.\"}");
        }

        private void HandleAllProceduresDebugRequest(HttpListenerContext context)
        {
            context.Response.StatusCode = 501;
            ResponseJson(context, "{\"error\":\"Navdata API not available.\"}");
        }

        /// <summary>
        /// Gets combined SID and STAR waypoints from SimBrief navlog
        /// URL: /api/procedures/simbrief
        /// </summary>
        private void HandleSimbriefProceduresRequest(HttpListenerContext context)
        {
            try
            {
                var procedures = Kneeboard_Server.GetSimbriefProcedures();
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(procedures, Newtonsoft.Json.Formatting.Indented);
                ResponseJson(context, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Procedure API] SimBrief procedures error: {ex.Message}");
                context.Response.StatusCode = 500;
                ResponseJson(context, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Gets SID waypoints from SimBrief navlog
        /// URL: /api/procedures/simbrief/sid
        /// </summary>
        private void HandleSimbriefSidRequest(HttpListenerContext context)
        {
            try
            {
                var sidData = Kneeboard_Server.GetSidWaypointsFromSimbrief();
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(sidData, Newtonsoft.Json.Formatting.Indented);
                ResponseJson(context, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Procedure API] SimBrief SID error: {ex.Message}");
                context.Response.StatusCode = 500;
                ResponseJson(context, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Gets STAR waypoints from SimBrief navlog
        /// URL: /api/procedures/simbrief/star
        /// </summary>
        private void HandleSimbriefStarRequest(HttpListenerContext context)
        {
            try
            {
                var starData = Kneeboard_Server.GetStarWaypointsFromSimbrief();
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(starData, Newtonsoft.Json.Formatting.Indented);
                ResponseJson(context, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Procedure API] SimBrief STAR error: {ex.Message}");
                context.Response.StatusCode = 500;
                ResponseJson(context, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        // ============================================================================
        // ILS API Methods (disabled - navdata integration removed)
        // ============================================================================

        /// <summary>
        /// Handles ILS API requests - currently disabled
        /// </summary>
        private void HandleIlsRequest(HttpListenerContext context, string command)
        {
            context.Response.StatusCode = 501;
            ResponseJson(context, "{\"error\":\"ILS API is not available. Navdata integration has been removed.\"}");
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
                Console.WriteLine($"Cache write error: {ex.Message}");
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
                        Console.WriteLine($"[Cache] Failed to delete file: {ex.Message}");
                    }
                }

                Console.WriteLine($"Cache cleanup: freed {bytesFreed / 1024 / 1024}MB");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Cache cleanup error: {ex.Message}");
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
                    Console.WriteLine("OpenAIP cache cleared");
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
                Console.WriteLine("Boundaries memory cache cleared - will reload from disk on next request");
            }
        }

        /// <summary>
        /// Gets the current cache size in bytes
        /// </summary>
        public static long GetCacheSize()
        {
            if (!Directory.Exists(CACHE_DIR))
                return 0;

            return Directory.GetFiles(CACHE_DIR, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
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
        private void HandleVatsimPilots(HttpListenerContext context)
        {
            try
            {
                string pilotsJson;

                // Check cache
                lock (_pilotsCacheLock)
                {
                    if (_cachedVatsimPilots != null && (DateTime.Now - _vatsimPilotsCacheTime) < PILOTS_CACHE_TTL)
                    {
                        context.Response.AddHeader("X-Cache", "HIT");
                        ResponseJson(context, _cachedVatsimPilots);
                        return;
                    }
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

                        // Spezial: SANTA+KFR bekommt Santa-Gesicht (Kategorie K)
                        string upperCs = callsign?.ToUpperInvariant() ?? "";
                        if (upperCs.Contains("SANTA") && upperCs.Contains("KFR"))
                        {
                            category = "K";  // Santa-Gesicht Icon
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
                            category = category,      // Vorberechnet! (K für SANT+KFR)
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

                context.Response.AddHeader("X-Cache", "MISS");
                ResponseJson(context, pilotsJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VATSIM pilots error: {ex.Message}");
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                try { context.Response.OutputStream.Close(); } catch { }
            }
        }

        /// <summary>
        /// Holt und cached IVAO Piloten-Daten mit Server-seitiger Vorverarbeitung
        /// </summary>
        private void HandleIvaoPilots(HttpListenerContext context)
        {
            try
            {
                string pilotsJson;

                // Check cache
                lock (_pilotsCacheLock)
                {
                    if (_cachedIvaoPilots != null && (DateTime.Now - _ivaoPilotsCacheTime) < PILOTS_CACHE_TTL)
                    {
                        context.Response.AddHeader("X-Cache", "HIT");
                        ResponseJson(context, _cachedIvaoPilots);
                        return;
                    }
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

                        // Spezial: SANTA+KFR bekommt Santa-Gesicht (Kategorie K)
                        string upperCs = callsign?.ToUpperInvariant() ?? "";
                        if (upperCs.Contains("SANTA") && upperCs.Contains("KFR"))
                        {
                            category = "K";  // Santa-Gesicht Icon
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
                            category = category,      // Vorberechnet! (K für SANTA+KFR)
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

                context.Response.AddHeader("X-Cache", "MISS");
                ResponseJson(context, pilotsJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"IVAO pilots error: {ex.Message}");
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                try { context.Response.OutputStream.Close(); } catch { }
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
                Console.WriteLine($"GeoJSON preprocessing error: {ex.Message}");
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
        private void HandleVatsimBoundariesWithBbox(HttpListenerContext context)
        {
            try
            {
                // Check preprocessed cache
                lock (_boundariesCacheLock)
                {
                    if (_preprocessedVatsimBoundaries != null && (DateTime.Now - _preprocessedVatsimBoundariesTime) < BOUNDARIES_CACHE_TTL)
                    {
                        context.Response.AddHeader("X-Cache", "HIT");
                        ResponseJson(context, _preprocessedVatsimBoundaries);
                        return;
                    }
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

                Console.WriteLine("VATSIM Boundaries: preprocessed with bounding boxes");
                context.Response.AddHeader("X-Cache", "PREPROCESSED");
                ResponseJson(context, preprocessed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VATSIM Boundaries preprocessing error: {ex.Message}");
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                try { context.Response.OutputStream.Close(); } catch { }
            }
        }

        /// <summary>
        /// Liefert vereinfachten VATSIM Offline-FIR-Layer
        /// Generiert aus VATSIM Boundaries.geojson, gefiltert auf FIR-Grenzen
        /// </summary>
        private void HandleVatsimOfflineFir(HttpListenerContext context)
        {
            try
            {
                string offlineFilePath = Path.Combine(BOUNDARIES_CACHE_DIR, "vatsim_offline_fir.json");

                if (File.Exists(offlineFilePath))
                {
                    string content = File.ReadAllText(offlineFilePath);
                    context.Response.AddHeader("X-Cache", "STATIC");
                    ResponseJson(context, content);
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
                    Console.WriteLine("VATSIM Offline FIR: generating from cached boundaries...");
                    string offlineFir = GenerateVatsimOfflineFirFromBoundaries(rawBoundaries);

                    // Cache für zukünftige Requests
                    try
                    {
                        Directory.CreateDirectory(BOUNDARIES_CACHE_DIR);
                        File.WriteAllText(offlineFilePath, offlineFir);
                        Console.WriteLine("VATSIM Offline FIR: saved to cache");
                    }
                    catch (Exception cacheEx)
                    {
                        Console.WriteLine($"VATSIM Offline FIR: could not save cache: {cacheEx.Message}");
                    }

                    context.Response.AddHeader("X-Cache", "GENERATED");
                    ResponseJson(context, offlineFir);
                    return;
                }

                // Letzter Fallback: leere FeatureCollection
                Console.WriteLine("VATSIM Offline FIR: no source data available, returning empty collection");
                ResponseJson(context, "{\"type\":\"FeatureCollection\",\"features\":[]}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VATSIM Offline FIR error: {ex.Message}");
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                try { context.Response.OutputStream.Close(); } catch { }
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

                Console.WriteLine($"VATSIM Offline FIR: extracted {features.Count} unique FIR zones");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VATSIM Offline FIR generation error: {ex.Message}");
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
        private void HandleIvaoOfflineFir(HttpListenerContext context)
        {
            try
            {
                string offlineFilePath = Path.Combine(BOUNDARIES_CACHE_DIR, "ivao_offline_fir.json");

                if (File.Exists(offlineFilePath))
                {
                    string content = File.ReadAllText(offlineFilePath);
                    context.Response.AddHeader("X-Cache", "STATIC");
                    context.Response.AddHeader("X-FIR-Count", "165");
                    ResponseJson(context, content);
                    return;
                }

                // Fallback: Generiere Offline-FIR aus gecachten IVAO-Boundaries
                string ivaoCachePath = Path.Combine(BOUNDARIES_CACHE_DIR, "ivao_boundaries_geojson.json");
                if (File.Exists(ivaoCachePath))
                {
                    Console.WriteLine("IVAO Offline FIR: generating from cached boundaries...");
                    string rawBoundaries = File.ReadAllText(ivaoCachePath);
                    string offlineFir = GenerateOfflineFirFromBoundaries(rawBoundaries);

                    // Cache für zukünftige Requests
                    try
                    {
                        Directory.CreateDirectory(BOUNDARIES_CACHE_DIR);
                        File.WriteAllText(offlineFilePath, offlineFir);
                        Console.WriteLine("IVAO Offline FIR: saved to cache");
                    }
                    catch (Exception cacheEx)
                    {
                        Console.WriteLine($"IVAO Offline FIR: could not save cache: {cacheEx.Message}");
                    }

                    context.Response.AddHeader("X-Cache", "GENERATED");
                    ResponseJson(context, offlineFir);
                    return;
                }

                // Letzter Fallback: leere FeatureCollection
                Console.WriteLine("IVAO Offline FIR: no source data available, returning empty collection");
                ResponseJson(context, "{\"type\":\"FeatureCollection\",\"features\":[]}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"IVAO Offline FIR error: {ex.Message}");
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                try { context.Response.OutputStream.Close(); } catch { }
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

                Console.WriteLine($"IVAO Offline FIR: extracted {features.Count} unique FIR/CTR zones");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"IVAO Offline FIR generation error: {ex.Message}");
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
        private void HandleIvaoBoundariesWithBbox(HttpListenerContext context)
        {
            try
            {
                // Check preprocessed cache
                lock (_boundariesCacheLock)
                {
                    if (_preprocessedIvaoBoundaries != null && (DateTime.Now - _preprocessedIvaoBoundariesTime) < BOUNDARIES_CACHE_TTL)
                    {
                        context.Response.AddHeader("X-Cache", "HIT");
                        ResponseJson(context, _preprocessedIvaoBoundaries);
                        return;
                    }
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

                Console.WriteLine("IVAO Boundaries: preprocessed with bounding boxes");
                context.Response.AddHeader("X-Cache", "PREPROCESSED");
                ResponseJson(context, preprocessed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"IVAO Boundaries preprocessing error: {ex.Message}");
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                try { context.Response.OutputStream.Close(); } catch { }
            }
        }

        /// <summary>
        /// Load pilot favorites from disk
        /// </summary>
        private void HandleGetFavorites(HttpListenerContext context)
        {
            try
            {
                lock (_favoritesLock)
                {
                    if (File.Exists(FAVORITES_FILE))
                    {
                        string json = File.ReadAllText(FAVORITES_FILE, Encoding.UTF8);
                        // Remove UTF-8 BOM if present (backwards compatibility)
                        if (json.Length > 0 && json[0] == '\uFEFF')
                        {
                            json = json.Substring(1);
                        }
                        ResponseJson(context, json);
                    }
                    else
                    {
                        ResponseJson(context, "{}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading favorites: {ex.Message}");
                ResponseJson(context, "{}");
            }
        }

        /// <summary>
        /// Save pilot favorites to disk
        /// </summary>
        private void HandleSaveFavorites(HttpListenerContext context)
        {
            try
            {
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
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

                    ResponseJson(context, "{\"success\":true}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving favorites: {ex.Message}");
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                ResponseJson(context, "{\"error\":\"" + ex.Message.Replace("\"", "\\\"") + "\"}");
            }
        }

        /// <summary>
        /// Handles ICAO-based airport lookup via OpenAIP API.
        /// Returns airport coordinates for a given ICAO code.
        /// Endpoint: api/openaip/airport-by-icao/{ICAO}
        /// </summary>
        private void HandleOpenAipAirportByIcao(HttpListenerContext context, string icaoCode)
        {
            if (string.IsNullOrWhiteSpace(icaoCode) || icaoCode.Length < 3 || icaoCode.Length > 4)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                ResponseJson(context, "{\"error\":\"Invalid ICAO code\"}");
                return;
            }

            // Cache path for ICAO lookups
            var cachePath = Path.Combine(CACHE_DIR, "icao", $"{icaoCode}.json");

            // Check cache first
            lock (_cacheLock)
            {
                if (File.Exists(cachePath))
                {
                    var fileInfo = new FileInfo(cachePath);
                    if (DateTime.Now - fileInfo.LastWriteTime < CACHE_TTL)
                    {
                        try
                        {
                            var cachedData = File.ReadAllBytes(cachePath);
                            context.Response.StatusCode = 200;
                            context.Response.ContentType = "application/json";
                            context.Response.ContentLength64 = cachedData.Length;
                            context.Response.AddHeader("X-Cache", "HIT");
                            context.Response.OutputStream.Write(cachedData, 0, cachedData.Length);
                            context.Response.OutputStream.Close();
                            return;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[OpenAIP ICAO] Cache read error: {ex.Message}");
                        }
                    }
                }
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
                    Console.WriteLine($"[OpenAIP ICAO] Parse error: {parseEx.Message}");
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
                            Console.WriteLine($"[OpenAIP ICAO] Cache write error: {cacheEx.Message}");
                        }
                    });
                }

                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = resultBytes.Length;
                context.Response.AddHeader("X-Cache", "MISS");
                context.Response.OutputStream.Write(resultBytes, 0, resultBytes.Length);
            }
            catch (WebException ex)
            {
                var httpResponse = ex.Response as HttpWebResponse;
                Console.WriteLine($"[OpenAIP ICAO] Error fetching {icaoCode}: {ex.Message}");
                context.Response.StatusCode = httpResponse != null
                    ? (int)httpResponse.StatusCode
                    : (int)HttpStatusCode.BadGateway;
                ResponseJson(context, $"{{\"error\":\"{ex.Message.Replace("\"", "\\\"")}\",\"icao\":\"{icaoCode}\",\"found\":false}}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OpenAIP ICAO] Unexpected error: {ex.Message}");
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                ResponseJson(context, $"{{\"error\":\"{ex.Message.Replace("\"", "\\\"")}\",\"icao\":\"{icaoCode}\",\"found\":false}}");
            }
            finally
            {
                upstreamResponse?.Close();
                try { context.Response.OutputStream.Close(); } catch { }
            }
        }

        // Global airport ICAO index - loaded once from OpenAIP
        private static Dictionary<string, (double lat, double lng, string name)> _globalAirportIndex = null;
        // IATA to ICAO mapping (e.g., "JFK" -> "KJFK", "YYZ" -> "CYYZ")
        private static Dictionary<string, string> _iataToIcaoIndex = null;
        private static readonly object _airportIndexLock = new object();
        private static bool _airportIndexLoading = false;
        private static readonly string GLOBAL_AIRPORTS_CACHE = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "global_airports.json");
        private static readonly string IATA_ICAO_CACHE = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "iata_icao_mapping.json");

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
                // Try to load from cache first
                if (File.Exists(GLOBAL_AIRPORTS_CACHE))
                {
                    var fileInfo = new FileInfo(GLOBAL_AIRPORTS_CACHE);
                    if (DateTime.Now - fileInfo.LastWriteTime < TimeSpan.FromDays(30))
                    {
                        try
                        {
                            var cachedJson = File.ReadAllText(GLOBAL_AIRPORTS_CACHE);
                            var cached = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(cachedJson);
                            var tempIndex = new Dictionary<string, (double lat, double lng, string name)>();
                            foreach (var kvp in cached)
                            {
                                tempIndex[kvp.Key] = ((double)kvp.Value.lat, (double)kvp.Value.lng, (string)kvp.Value.name);
                            }
                            _globalAirportIndex = tempIndex;

                            // Also load IATA->ICAO mapping from cache
                            if (File.Exists(IATA_ICAO_CACHE))
                            {
                                try
                                {
                                    var iataJson = File.ReadAllText(IATA_ICAO_CACHE);
                                    _iataToIcaoIndex = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(iataJson);
                                    Console.WriteLine($"[OpenAIP] Loaded {_iataToIcaoIndex.Count} IATA->ICAO mappings from cache");
                                }
                                catch (Exception iataEx)
                                {
                                    Console.WriteLine($"[OpenAIP] IATA cache read error: {iataEx.Message}");
                                    _iataToIcaoIndex = new Dictionary<string, string>();
                                }
                            }
                            else
                            {
                                _iataToIcaoIndex = new Dictionary<string, string>();
                            }

                            Console.WriteLine($"[OpenAIP] Loaded {_globalAirportIndex.Count} airports from cache");
                            _kneeboardServer?.SetStatusText($"Status: {_globalAirportIndex.Count} airports loaded. Server is running...");
                            return;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[OpenAIP] Cache read error: {ex.Message}");
                        }
                    }
                }

                // Load ALL airports from OpenAIP API (paginated)
                Console.WriteLine("[OpenAIP] Loading global airport database...");
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
                                Console.WriteLine($"[OpenAIP] Total pages: {totalPages}");
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

                            Console.WriteLine($"[OpenAIP] Page {page}/{totalPages} - Airports with ICAO: {allAirports.Count}");
                            _kneeboardServer?.SetStatusText($"Status: Loading airports... {page}/{totalPages} ({allAirports.Count} ICAO)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[OpenAIP] Error loading page {page}: {ex.Message}");
                        break;
                    }

                    page++;

                    // Small delay to avoid rate limiting
                    if (page <= totalPages)
                        System.Threading.Thread.Sleep(50);
                }

                _globalAirportIndex = allAirports;
                _iataToIcaoIndex = iataMapping;
                Console.WriteLine($"[OpenAIP] Loaded {_globalAirportIndex.Count} airports with ICAO codes, {_iataToIcaoIndex.Count} IATA mappings");
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
                        Console.WriteLine($"[OpenAIP] Saved airport cache and {iataMapping.Count} IATA mappings");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[OpenAIP] Cache save error: {ex.Message}");
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
        private void HandleIataToIcao(HttpListenerContext context, string iataCode)
        {
            try
            {
                if (string.IsNullOrEmpty(iataCode) || iataCode.Length != 3)
                {
                    context.Response.StatusCode = 400;
                    ResponseJson(context, "{\"found\":false,\"error\":\"Invalid IATA code\"}");
                    return;
                }

                string icao = null;
                if (_iataToIcaoIndex != null && _iataToIcaoIndex.TryGetValue(iataCode, out icao))
                {
                    ResponseJson(context, $"{{\"found\":true,\"iata\":\"{iataCode}\",\"icao\":\"{icao}\"}}");
                }
                else
                {
                    ResponseJson(context, $"{{\"found\":false,\"iata\":\"{iataCode}\"}}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] IATA-to-ICAO error: {ex.Message}");
                context.Response.StatusCode = 500;
                ResponseJson(context, "{\"found\":false,\"error\":\"Internal error\"}");
            }
        }

        /// <summary>
        /// Returns the full IATA->ICAO mapping for frontend caching.
        /// Endpoint: api/openaip/iata-icao-mapping
        /// </summary>
        private void HandleIataIcaoMapping(HttpListenerContext context)
        {
            try
            {
                if (_iataToIcaoIndex == null || _iataToIcaoIndex.Count == 0)
                {
                    ResponseJson(context, "{\"ready\":false,\"count\":0,\"mapping\":{}}");
                    return;
                }

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    ready = true,
                    count = _iataToIcaoIndex.Count,
                    mapping = _iataToIcaoIndex
                });
                ResponseJson(context, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] IATA-ICAO-mapping error: {ex.Message}");
                context.Response.StatusCode = 500;
                ResponseJson(context, "{\"ready\":false,\"error\":\"Internal error\"}");
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

                                Console.WriteLine($"[OpenAIP] Found {icao} via search: {name}");
                                return (lat, lng, name);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OpenAIP] Search error for {icao}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Batch endpoint to fetch DEP and ARR airport coordinates in one request.
        /// Uses the global airport index for instant O(1) lookup.
        /// Endpoint: api/openaip/airports-batch?dep=KMIA&arr=LIPE
        /// </summary>
        private void HandleOpenAipAirportsBatch(HttpListenerContext context)
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
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = loadingBytes.Length;
                context.Response.OutputStream.Write(loadingBytes, 0, loadingBytes.Length);
                try { context.Response.OutputStream.Close(); } catch { }
                return;
            }

            var depIcao = (context.Request.QueryString["dep"] ?? "").Trim().ToUpperInvariant();
            var arrIcao = (context.Request.QueryString["arr"] ?? "").Trim().ToUpperInvariant();

            if (string.IsNullOrEmpty(depIcao) && string.IsNullOrEmpty(arrIcao))
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                ResponseJson(context, "{\"error\":\"At least one of dep or arr is required\"}");
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

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = resultBytes.Length;
            context.Response.OutputStream.Write(resultBytes, 0, resultBytes.Length);
            try { context.Response.OutputStream.Close(); } catch { }
        }

        private void HandleOpenAipProxy(HttpListenerContext context, string endpoint)
        {
            var request = context.Request;
            var lat = (request.QueryString["lat"] ?? string.Empty).Trim();
            var lng = (request.QueryString["lng"] ?? string.Empty).Trim();
            var dist = (request.QueryString["dist"] ?? string.Empty).Trim();
            bool isTilesEndpoint = endpoint.Equals("tiles", StringComparison.OrdinalIgnoreCase);

            if (!isTilesEndpoint && (string.IsNullOrWhiteSpace(lat) || string.IsNullOrWhiteSpace(lng) || string.IsNullOrWhiteSpace(dist)))
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.OutputStream.Close();
                return;
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
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                    context.Response.ContentType = isTilesEndpoint ? "image/png" : "application/json";
                    context.Response.ContentLength64 = cachedData.Length;
                    context.Response.AddHeader("X-Cache", "HIT");
                    context.Response.OutputStream.Write(cachedData, 0, cachedData.Length);
                    context.Response.OutputStream.Close();
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Cache read error: {ex.Message}");
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

                context.Response.StatusCode = (int)upstreamResponse.StatusCode;
                context.Response.ContentType = upstreamResponse.ContentType ?? (isTilesEndpoint ? "image/png" : "application/json");
                context.Response.ContentLength64 = responseData.Length;
                context.Response.AddHeader("X-Cache", "MISS");
                context.Response.OutputStream.Write(responseData, 0, responseData.Length);
            }
            catch (WebException ex)
            {
                var httpResponse = ex.Response as HttpWebResponse;
                context.Response.StatusCode = httpResponse != null
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
                    Console.WriteLine($"[OpenAIP] Failed to write error log: {logEx.Message}");
                }

                Console.WriteLine($"OpenAIP Proxy Error: {ex.Message}");
                Console.WriteLine($"Requested URL: {builder.ToString()}");
                if (httpResponse != null)
                {
                    Console.WriteLine($"Upstream Status: {httpResponse.StatusCode}");
                }

                string payload = "{\"error\":\"Unable to reach openAIP\"}";
                if (httpResponse != null)
                {
                    try
                    {
                        using (var reader = new StreamReader(httpResponse.GetResponseStream()))
                        {
                            payload = reader.ReadToEnd();
                            Console.WriteLine($"Upstream Response: {payload}");
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
                    context.Response.ContentType = "image/png";
                    context.Response.ContentLength64 = 0;
                }
                else
                {
                    var buffer = Encoding.UTF8.GetBytes(payload);
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = buffer.Length;
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                }
            }
            finally
            {
                upstreamResponse?.Close();
                context.Response.OutputStream.Close();
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
                Console.WriteLine($"[Baselayer Cache] Write error: {ex.Message}");
            }
        }

        /// <summary>
        /// Proxies baselayer tiles with local caching
        /// Supports: osm, google-hybrid, google-satellite, opentopomap
        /// </summary>
        private void HandleBaselayerTileProxy(HttpListenerContext context, string provider)
        {
            HttpWebResponse upstreamResponse = null;
            bool responseClosed = false;

            try
            {
                var request = context.Request;
                var path = request.Url.AbsolutePath;

                // Parse tile coordinates from path: /api/tiles/{provider}/{z}/{x}/{y}.png
                string prefix = $"/api/tiles/{provider}/";
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    try { context.Response.OutputStream.Close(); } catch { }
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
                        context.Response.StatusCode = (int)HttpStatusCode.OK;
                        context.Response.ContentType = "image/png";
                        context.Response.ContentLength64 = cachedData.Length;
                        context.Response.AddHeader("X-Cache", "HIT");
                        context.Response.AddHeader("Cache-Control", "public, max-age=2592000"); // 30 days
                        context.Response.OutputStream.Write(cachedData, 0, cachedData.Length);
                        context.Response.OutputStream.Close();
                        responseClosed = true;
                        return;
                    }
                    catch (System.Net.HttpListenerException)
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
                        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        context.Response.OutputStream.Close();
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
                            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                            context.Response.OutputStream.Close();
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

                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "image/png";
                context.Response.ContentLength64 = responseData.Length;
                context.Response.AddHeader("X-Cache", "MISS");
                context.Response.AddHeader("Cache-Control", "public, max-age=2592000");
                context.Response.OutputStream.Write(responseData, 0, responseData.Length);
            }
            catch (System.Net.HttpListenerException)
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
                        context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
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
                        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                }
                catch { }
            }
            finally
            {
                upstreamResponse?.Close();
                if (!responseClosed)
                {
                    try { context.Response.OutputStream.Close(); } catch { }
                }
            }
        }

        private void Process(HttpListenerContext context)
        {

            HttpListenerRequest request = context.Request;



            HttpListenerResponse response = context.Response;

            // ✅ Allow CORS
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            // ✅ Handle preflight OPTIONS request
            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = (int)HttpStatusCode.OK;
                response.OutputStream.Close();
                return;
            }

            // ✅ Handle HEAD requests for file existence checks (avoids 404 console errors)
            if (request.HttpMethod == "HEAD")
            {
                try
                {
                    string headCommand = context.Request.Url.AbsolutePath.Substring(1);
                    // Replace forward slashes with backslashes for Windows path compatibility
                    headCommand = headCommand.Replace('/', '\\');
                    string headFilePath = Path.Combine(_rootDirectory, headCommand);

                    if (System.IO.File.Exists(headFilePath))
                    {
                        response.StatusCode = (int)HttpStatusCode.OK;
                        response.ContentType = _mimeTypeMappings.TryGetValue(Path.GetExtension(headFilePath), out string headMime) ? headMime : "application/octet-stream";
                        response.ContentLength64 = new FileInfo(headFilePath).Length;
                    }
                    else
                    {
                        response.StatusCode = (int)HttpStatusCode.NotFound;
                    }
                }
                catch (Exception)
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                }
                response.OutputStream.Close();
                return;
            }

            string command = context.Request.Url.AbsolutePath;

            command = command.Substring(1);
            //Console.WriteLine("Command: " + command);

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
                ResponseString(context, "true");
            }
            else if (command == "setNavlogValues")
            {
                string postedText = GetPostedText(request);
                Console.WriteLine($"[NavlogSync] Server received navlog POST, length: {postedText.Length}");

                // Store the new values with timestamp
                values = postedText;
                valuesTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Properties.Settings.Default.values = values;
                Properties.Settings.Default.valuesTimestamp = valuesTimestamp;
                Properties.Settings.Default.Save();

                Console.WriteLine($"[NavlogSync] Server stored navlog data, new length: {values.Length}, timestamp: {valuesTimestamp}");
                ResponseString(context, valuesTimestamp.ToString());
            }
            else if (command == "synchronizeFlightplan")
            {
                string flightplan = Kneeboard_Server.syncFlightplan();
                if (flightplan != "")
                {
                    ResponseString(context, flightplan);
                    // Don't clear flightplan - keep it available for map auto-load
                }
                else
                {
                    ResponseString(context, "");
                }
            }
            else if (command == "getNavlogValues")
            {
                string navlogData = Properties.Settings.Default.values ?? "";
                long timestamp = Properties.Settings.Default.valuesTimestamp;
                Console.WriteLine($"[NavlogSync] Server sending navlog data, length: {navlogData.Length}, timestamp: {timestamp}");

                // Send data with timestamp as header
                context.Response.Headers.Add("X-Navlog-Timestamp", timestamp.ToString());
                ResponseString(context, navlogData);
            }
            else if (command == "getNavlogTimestamp")
            {
                long timestamp = Properties.Settings.Default.valuesTimestamp;
                ResponseString(context, timestamp.ToString());
            }
            else if (command == "clearNavlogValues")
            {
                Console.WriteLine("[NavlogSync] Server clearing navlog data");
                values = "";
                valuesTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Properties.Settings.Default.values = "";
                Properties.Settings.Default.valuesTimestamp = valuesTimestamp;
                Properties.Settings.Default.Save();
                Console.WriteLine($"[NavlogSync] Server navlog data cleared, timestamp: {valuesTimestamp}");
                ResponseString(context, "OK");
            }
            else if (command == "getFlightplan")
            {
                if (Kneeboard_Server.flightplan != null)
                {
                    ResponseString(context, Kneeboard_Server.flightplan);
                    // Don't clear flightplan - keep it available for map auto-load
                }
                else
                {
                    ResponseString(context, "");
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
                Console.WriteLine("[NavlogSync] Navlog data and SimBrief cache cleared along with flight plan");

                ResponseString(context, "cleared");
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
                Console.WriteLine("[NavlogSync] Local navlog cleared, OFP hidden, SimBrief cache preserved: " + hasServerFlightplan);

                ResponseJson(context, $"{{\"cleared\":true,\"hasServerFlightplan\":{hasServerFlightplan.ToString().ToLower()}}}");
            }
            else if (command == "hasFlightplan")
            {
                ResponseString(context, Kneeboard_Server.flightplan != null ? "true" : "false");
            }
            else if (command == "hasSimbriefId")
            {
                bool hasId = !string.IsNullOrEmpty(Properties.Settings.Default.simbriefId);
                ResponseJson(context, $"{{\"hasSimbriefId\":{hasId.ToString().ToLower()}}}");
            }
            else if (command == "checkSimbriefUpdate")
            {
                bool updateAvailable = Kneeboard_Server.CheckSimbriefUpdateAvailable();
                ResponseJson(context, $"{{\"updateAvailable\":{updateAvailable.ToString().ToLower()}}}");
            }
            else if (command == "hasServerFlightplan")
            {
                // Check if server has a flightplan loaded (from background sync or previous session)
                bool hasFlightplan = !string.IsNullOrEmpty(Kneeboard_Server.flightplan);
                ResponseJson(context, $"{{\"hasFlightplan\":{hasFlightplan.ToString().ToLower()}}}");
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
                        ResponseJson(context, $"{{\"exists\":true,\"hash\":\"{hashString}\"}}");
                    }
                }
                else
                {
                    ResponseJson(context, "{\"exists\":false}");
                }
            }
            else if (command.StartsWith("api/metar/", StringComparison.OrdinalIgnoreCase))
            {
                HandleNoaaProxy(context, command.Substring("api/metar/".Length), false);
                return;
            }
            else if (command.StartsWith("api/taf/", StringComparison.OrdinalIgnoreCase))
            {
                HandleNoaaProxy(context, command.Substring("api/taf/".Length), true);
                return;
            }
            else if (command == "api/boundaries/vatsim")
            {
                HandleVatsimBoundariesProxy(context);
                return;
            }
            else if (command == "api/boundaries/ivao")
            {
                // IVAO bietet KEINE öffentliche API für FIR-Boundaries!
                // Little Navmap ist die einzige funktionierende Quelle
                HandleIvaoBoundariesProxy(context);
                return;
            }
            else if (command == "api/boundaries/vatsim-tracon")
            {
                HandleVatsimTraconBoundariesProxy(context);
                return;
            }
            else if (command == "api/vatspy/firnames")
            {
                HandleVatspyFirNamesProxy(context);
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
                        ResponseJson(context, controllers);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"VATSIM sync error: {ex.Message}");
                    context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                    try { context.Response.OutputStream.Close(); } catch { }
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
                        ResponseJson(context, transceivers);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"VATSIM transceivers sync error: {ex.Message}");
                    context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                    try { context.Response.OutputStream.Close(); } catch { }
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
                        ResponseJson(context, controllers);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"IVAO sync error: {ex.Message}");
                    context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                    try { context.Response.OutputStream.Close(); } catch { }
                }
                return;
            }
            // ===== HYBRID-ANSATZ: Vorverarbeitete Piloten-Daten =====
            else if (command == "api/pilots/vatsim")
            {
                HandleVatsimPilots(context);
                return;
            }
            else if (command == "api/pilots/ivao")
            {
                HandleIvaoPilots(context);
                return;
            }
            // ===== HYBRID-ANSATZ: Boundaries mit vorberechneten Bounding-Boxes =====
            else if (command == "api/boundaries/vatsim")
            {
                HandleVatsimBoundariesWithBbox(context);
                return;
            }
            else if (command == "api/boundaries/ivao")
            {
                HandleIvaoBoundariesWithBbox(context);
                return;
            }
            else if (command == "api/boundaries/ivao-offline")
            {
                HandleIvaoOfflineFir(context);
                return;
            }
            else if (command == "api/boundaries/vatsim-offline")
            {
                HandleVatsimOfflineFir(context);
                return;
            }
            else if (command.StartsWith("api/openaip/iata-to-icao/", StringComparison.OrdinalIgnoreCase))
            {
                // Convert IATA to ICAO: api/openaip/iata-to-icao/JFK -> KJFK
                var iataCode = command.Substring("api/openaip/iata-to-icao/".Length).ToUpperInvariant();
                HandleIataToIcao(context, iataCode);
                return;
            }
            else if (command == "api/openaip/iata-icao-mapping")
            {
                // Return full IATA->ICAO mapping for frontend caching
                HandleIataIcaoMapping(context);
                return;
            }
            else if (command.StartsWith("api/openaip/airport-by-icao/", StringComparison.OrdinalIgnoreCase))
            {
                // Extract ICAO code from URL: api/openaip/airport-by-icao/KMIA
                var icaoCode = command.Substring("api/openaip/airport-by-icao/".Length).ToUpperInvariant();
                HandleOpenAipAirportByIcao(context, icaoCode);
                return;
            }
            else if (command.StartsWith("api/openaip/airports-batch", StringComparison.OrdinalIgnoreCase))
            {
                // Batch endpoint: api/openaip/airports-batch?dep=KMIA&arr=LIPE
                HandleOpenAipAirportsBatch(context);
                return;
            }
            else if (command.StartsWith("api/openaip/airports", StringComparison.OrdinalIgnoreCase))
            {
                HandleOpenAipProxy(context, "airports");
                return;
            }
            else if (command.StartsWith("api/openaip/navaids", StringComparison.OrdinalIgnoreCase))
            {
                HandleOpenAipProxy(context, "navaids");
                return;
            }
            else if (command == "api/favorites")
            {
                // GET: Load favorites, POST: Save favorites
                if (context.Request.HttpMethod == "GET")
                {
                    HandleGetFavorites(context);
                }
                else if (context.Request.HttpMethod == "POST")
                {
                    HandleSaveFavorites(context);
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
                ResponseJson(context, Newtonsoft.Json.JsonConvert.SerializeObject(userIds));
                return;
            }
            else if (command.StartsWith("api/openaip/reporting-points", StringComparison.OrdinalIgnoreCase))
            {
                HandleOpenAipProxy(context, "reporting-points");
                return;
            }
            else if (command.StartsWith("api/openaip/tiles/", StringComparison.OrdinalIgnoreCase))
            {
                HandleOpenAipProxy(context, "tiles");
                return;
            }
            // Baselayer Tile Proxy with Caching
            else if (command.StartsWith("api/tiles/osm/", StringComparison.OrdinalIgnoreCase))
            {
                HandleBaselayerTileProxy(context, "osm");
                return;
            }
            else if (command.StartsWith("api/tiles/google-hybrid/", StringComparison.OrdinalIgnoreCase))
            {
                HandleBaselayerTileProxy(context, "google-hybrid");
                return;
            }
            else if (command.StartsWith("api/tiles/google-satellite/", StringComparison.OrdinalIgnoreCase))
            {
                HandleBaselayerTileProxy(context, "google-satellite");
                return;
            }
            else if (command.StartsWith("api/tiles/opentopomap/", StringComparison.OrdinalIgnoreCase))
            {
                HandleBaselayerTileProxy(context, "opentopomap");
                return;
            }
            else if (command == "api/elevation")
            {
                HandleElevationProxy(context);
                return;
            }
            else if (command.StartsWith("api/dfs/tiles/", StringComparison.OrdinalIgnoreCase))
            {
                HandleDfsProxy(context);
                return;
            }
            else if (command.StartsWith("api/ofm/tiles/", StringComparison.OrdinalIgnoreCase))
            {
                HandleOfmProxy(context);
                return;
            }
            else if (command.StartsWith("api/nominatim/search", StringComparison.OrdinalIgnoreCase))
            {
                HandleNominatimProxy(context);
                return;
            }
            else if (command.StartsWith("api/wind/", StringComparison.OrdinalIgnoreCase))
            {
                HandleWindProxy(context);
                return;
            }
            else if (command == "api/simconnect/position")
            {
                HandleSimConnectPositionRequest(context);
                return;
            }
            else if (command == "api/simconnect/status")
            {
                HandleSimConnectStatusRequest(context);
                return;
            }
            else if (command == "api/simconnect/teleport")
            {
                HandleSimConnectTeleportRequest(context);
                return;
            }
            else if (command == "api/simconnect/pause")
            {
                HandleSimConnectPauseRequest(context);
                return;
            }
            else if (command == "api/simconnect/radio/frequency")
            {
                HandleSimConnectRadioFrequencyRequest(context);
                return;
            }
            // Procedure API Endpoints (SID/STAR data from Little Navmap)
            else if (command.StartsWith("api/procedures/sids/", StringComparison.OrdinalIgnoreCase))
            {
                HandleGetSIDListRequest(context, command);
                return;
            }
            else if (command.StartsWith("api/procedures/stars/", StringComparison.OrdinalIgnoreCase))
            {
                HandleGetSTARListRequest(context, command);
                return;
            }
            else if (command.StartsWith("api/procedures/approaches/", StringComparison.OrdinalIgnoreCase))
            {
                HandleGetApproachListRequest(context, command);
                return;
            }
            else if (command.StartsWith("api/procedures/approach/", StringComparison.OrdinalIgnoreCase))
            {
                HandleGetApproachRequest(context, command);
                return;
            }
            else if (command.StartsWith("api/procedures/procedure/", StringComparison.OrdinalIgnoreCase))
            {
                HandleGetProcedureRequest(context, command);
                return;
            }
            else if (command == "api/procedures/status")
            {
                HandleProcedureStatusRequest(context);
                return;
            }
            else if (command.StartsWith("api/procedures/debug/", StringComparison.OrdinalIgnoreCase))
            {
                HandleProcedureDebugRequest(context, command);
                return;
            }
            else if (command == "api/procedures/allprocedures")
            {
                HandleAllProceduresDebugRequest(context);
                return;
            }
            else if (command == "api/procedures/folderinfo")
            {
                HandleNavdataFolderInfoRequest(context);
                return;
            }
            else if (command == "api/procedures/simbrief")
            {
                HandleSimbriefProceduresRequest(context);
                return;
            }
            else if (command == "api/procedures/simbrief/sid")
            {
                HandleSimbriefSidRequest(context);
                return;
            }
            else if (command == "api/procedures/simbrief/star")
            {
                HandleSimbriefStarRequest(context);
                return;
            }
            // ILS API Endpoints
            else if (command.StartsWith("api/ils/", StringComparison.OrdinalIgnoreCase))
            {
                HandleIlsRequest(context, command);
                return;
            }
            else if (command == "getDocumentsList")
            {
                string documentsString = _kneeboardServer.GetFileList();
                ResponseString(context, documentsString);
            }
            else if (command == "getMapLayers")
            {
                try
                {
                    string layersFilePath = Path.Combine(_rootDirectory, "map-layers.json");
                    if (System.IO.File.Exists(layersFilePath))
                    {
                        string layersJson = System.IO.File.ReadAllText(layersFilePath, Encoding.UTF8);
                        ResponseJson(context, layersJson);
                    }
                    else
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        ResponseJson(context, "{\"error\":\"map-layers.json not found\"}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading map layers: {ex.Message}");
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    ResponseJson(context, "{\"error\":\"Failed to load map layers\"}");
                }
                return;
            }
            else if (command.StartsWith("getDocumentText"))
            {
                string[] parts = command.Split(new[] { '-' }, 2);
                if (parts.Length < 2)
                {
                    context.Response.StatusCode = 400;
                    context.Response.ContentLength64 = 0;
                    context.Response.OutputStream.Close();   // ✅
                    return;
                }

                string documentNameEncoded = parts[1];
                string documentName = Uri.UnescapeDataString(documentNameEncoded);

                Console.WriteLine("Dokument-Request: " + documentName);

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
                    context.Response.StatusCode = 404;
                    context.Response.ContentLength64 = 0;
                    context.Response.OutputStream.Close();   // ✅
                    return;
                }

                byte[] pdfFile;
                try
                {
                    pdfFile = System.IO.File.ReadAllBytes(fileEntry.Path);
                }
                catch (Exception)
                {
                    context.Response.StatusCode = 500;
                    context.Response.ContentLength64 = 0;
                    context.Response.OutputStream.Close();   // ✅
                    return;
                }

                // Erfolgspfad
                string base64 = Convert.ToBase64String(pdfFile);

                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(base64);
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "text/plain";
                context.Response.ContentLength64 = buffer.Length;

                using (System.IO.Stream output = context.Response.OutputStream)
                {
                    output.Write(buffer, 0, buffer.Length);
                }
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
                        using (var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            // Set response headers
                            context.Response.ContentType = _mimeTypeMappings.TryGetValue(Path.GetExtension(filePath), out string mime) ? mime : "application/octet-stream";
                            context.Response.ContentLength64 = input.Length;
                            context.Response.AddHeader("Date", DateTime.Now.ToString("r"));
                            context.Response.AddHeader("Last-Modified", File.GetLastWriteTime(filePath).ToString("r"));

                            // Add cache headers for static assets
                            string ext = Path.GetExtension(filePath).ToLowerInvariant();
                            if (ext == ".jpg" || ext == ".png" || ext == ".gif" || ext == ".ico")
                            {
                                context.Response.AddHeader("Cache-Control", "public, max-age=3600");
                            }

                            // Stream file with larger buffer for better performance
                            byte[] buffer = new byte[64 * 1024]; // 64KB buffer
                            int bytesRead;
                            while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                context.Response.OutputStream.Write(buffer, 0, bytesRead);
                            }
                        }

                        context.Response.StatusCode = (int)HttpStatusCode.OK;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error serving file {filePath}: {ex.Message}");
                        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    }
                }
                else
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                }
            }

            try { context.Response.OutputStream.Close(); } catch { }
        }

        private static string GetPostedText(HttpListenerRequest request)
        {
            string text = "";
            using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                text = reader.ReadToEnd();
            }
            return text;
        }

        private void Initialize(string path, int port)
        {
            // Increase connection limit for better tile loading performance
            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;

            this._rootDirectory = path;
            this._port = port;
            _serverThread = new Thread(this.Listen)
            {
                IsBackground = true
            };
            _serverThread.Start();
        }
    }

}
