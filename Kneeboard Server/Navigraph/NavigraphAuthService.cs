using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Kneeboard_Server.Properties;

namespace Kneeboard_Server.Navigraph
{
    /// <summary>
    /// Navigraph Authentication Service using OAuth Device Authorization Flow with PKCE
    /// </summary>
    public class NavigraphAuthService
    {
        // OAuth Endpoints
        private const string AUTH_BASE = "https://identity.api.navigraph.com";
        private const string DEVICE_AUTH_ENDPOINT = AUTH_BASE + "/connect/deviceauthorization";
        private const string TOKEN_ENDPOINT = AUTH_BASE + "/connect/token";
        private const string SUBSCRIPTIONS_ENDPOINT = "https://api.navigraph.com/v1/subscriptions/valid";
        private const string USERINFO_ENDPOINT = AUTH_BASE + "/connect/userinfo";

        // OAuth Scopes - fmsdata scope required for navigation data packages access
        private const string SCOPE = "openid fmsdata offline_access";

        // PKCE
        private string _codeVerifier;
        private string _codeChallenge;

        // HttpClient
        private readonly HttpClient _httpClient;

        // Events
        public event EventHandler<string> OnLog;
        public event EventHandler<NavigraphStatus> OnStatusChanged;

        // Properties
        public string AccessToken { get; private set; }
        public string RefreshToken { get; private set; }
        public DateTime TokenExpiry { get; private set; }
        public string Username { get; private set; }
        public bool HasFmsDataSubscription { get; private set; }

        public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken) && TokenExpiry > DateTime.UtcNow;

        // Client ID from secrets.config
        public string ClientId => ConfigurationManager.AppSettings["NavigraphClientId"]
            ?? Settings.Default.NavigraphClientId
            ?? "gsimulations-kneeboard";

        // Client Secret from secrets.config
        private string ClientSecret => ConfigurationManager.AppSettings["NavigraphClientSecret"]
            ?? "";

        public NavigraphAuthService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "KneeboardServer/2.0");

            // Load saved tokens
            LoadTokensFromSettings();
        }

        /// <summary>
        /// Load tokens from application settings
        /// </summary>
        private void LoadTokensFromSettings()
        {
            try
            {
                AccessToken = Settings.Default.NavigraphAccessToken;
                RefreshToken = Settings.Default.NavigraphRefreshToken;
                Username = Settings.Default.NavigraphUsername;

                if (!string.IsNullOrEmpty(Settings.Default.NavigraphTokenExpiry))
                {
                    if (DateTime.TryParse(Settings.Default.NavigraphTokenExpiry, out DateTime expiry))
                    {
                        TokenExpiry = expiry;
                    }
                }

                // Check if token is valid and get subscription status
                if (IsAuthenticated)
                {
                    Log($"Navigraph: Gespeicherte Anmeldung gefunden für {Username}");
                    _ = Task.Run(async () => await CheckSubscriptionAsync());
                }
            }
            catch (Exception ex)
            {
                Log($"Navigraph: Fehler beim Laden der Token: {ex.Message}");
            }
        }

        /// <summary>
        /// Save tokens to application settings
        /// </summary>
        private void SaveTokensToSettings()
        {
            try
            {
                Settings.Default.NavigraphAccessToken = AccessToken ?? "";
                Settings.Default.NavigraphRefreshToken = RefreshToken ?? "";
                Settings.Default.NavigraphUsername = Username ?? "";
                Settings.Default.NavigraphTokenExpiry = TokenExpiry.ToString("O");
                Settings.Default.Save();
            }
            catch (Exception ex)
            {
                Log($"Navigraph: Fehler beim Speichern der Token: {ex.Message}");
            }
        }

        /// <summary>
        /// Generate PKCE Code Verifier (43-128 characters)
        /// </summary>
        private void GeneratePkceValues()
        {
            // Generate 32 random bytes for code verifier
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] bytes = new byte[32];
                rng.GetBytes(bytes);
                _codeVerifier = Base64UrlEncode(bytes);
            }

            // Generate code challenge using SHA256
            using (var sha256 = SHA256.Create())
            {
                byte[] challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(_codeVerifier));
                _codeChallenge = Base64UrlEncode(challengeBytes);
            }

            Log($"Navigraph: PKCE Werte generiert");
        }

        /// <summary>
        /// Base64 URL Encode (RFC 7636)
        /// </summary>
        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        /// <summary>
        /// Start the Device Authorization Flow
        /// Returns device code response with user_code and verification_uri
        /// </summary>
        public async Task<DeviceCodeResponse> StartDeviceAuthFlowAsync()
        {
            try
            {
                Log("Navigraph: Starte Device Authorization Flow...");

                // Generate new PKCE values
                GeneratePkceValues();

                // Build request
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "client_id", ClientId },
                    { "client_secret", ClientSecret },
                    { "scope", SCOPE },
                    { "code_challenge", _codeChallenge },
                    { "code_challenge_method", "S256" }
                });

                Log($"Navigraph: Sende Device Auth Request an {DEVICE_AUTH_ENDPOINT}");
                Log($"Navigraph: Client ID: {ClientId}");

                var response = await _httpClient.PostAsync(DEVICE_AUTH_ENDPOINT, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                Log($"Navigraph: Device Auth Response: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    Log($"Navigraph: Device Auth Fehler: {response.StatusCode} - {responseBody}");
                    return null;
                }

                var json = JObject.Parse(responseBody);

                var result = new DeviceCodeResponse
                {
                    DeviceCode = json["device_code"]?.ToString(),
                    UserCode = json["user_code"]?.ToString(),
                    VerificationUri = json["verification_uri"]?.ToString(),
                    VerificationUriComplete = json["verification_uri_complete"]?.ToString(),
                    ExpiresIn = json["expires_in"]?.ToObject<int>() ?? 600,
                    Interval = json["interval"]?.ToObject<int>() ?? 5
                };

                Log($"Navigraph: Benutzercode: {result.UserCode}");
                Log($"Navigraph: Öffne {result.VerificationUri} und gib den Code ein");

                return result;
            }
            catch (Exception ex)
            {
                Log($"Navigraph: Device Auth Fehler: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Poll for token after user has authorized the device
        /// </summary>
        public async Task<bool> PollForTokenAsync(string deviceCode, int interval, CancellationToken cancellationToken = default)
        {
            try
            {
                Log("Navigraph: Warte auf Benutzerautorisierung...");

                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(interval * 1000, cancellationToken);

                    var content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { "client_id", ClientId },
                        { "client_secret", ClientSecret },
                        { "grant_type", "urn:ietf:params:oauth:grant-type:device_code" },
                        { "device_code", deviceCode },
                        { "code_verifier", _codeVerifier },
                        { "scope", SCOPE }
                    });

                    var response = await _httpClient.PostAsync(TOKEN_ENDPOINT, content, cancellationToken);
                    var responseBody = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        // Success - parse token
                        var json = JObject.Parse(responseBody);

                        AccessToken = json["access_token"]?.ToString();
                        RefreshToken = json["refresh_token"]?.ToString();

                        int expiresIn = json["expires_in"]?.ToObject<int>() ?? 3600;
                        TokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);

                        // Extract username from token
                        ExtractUsernameFromToken();

                        // Save tokens
                        SaveTokensToSettings();

                        Log($"Navigraph: Erfolgreich angemeldet als {Username}");

                        // Check subscription
                        await CheckSubscriptionAsync();

                        // Notify status change
                        OnStatusChanged?.Invoke(this, GetStatus());

                        return true;
                    }

                    // Parse error
                    var errorJson = JObject.Parse(responseBody);
                    var error = errorJson["error"]?.ToString();

                    switch (error)
                    {
                        case "authorization_pending":
                            // User hasn't authorized yet, continue polling
                            continue;

                        case "slow_down":
                            // We're polling too fast, increase interval
                            interval += 5;
                            continue;

                        case "expired_token":
                            Log("Navigraph: Autorisierung abgelaufen");
                            return false;

                        case "access_denied":
                            Log("Navigraph: Zugriff verweigert");
                            return false;

                        default:
                            Log($"Navigraph: Token Fehler: {error}");
                            return false;
                    }
                }

                return false;
            }
            catch (OperationCanceledException)
            {
                Log("Navigraph: Autorisierung abgebrochen");
                return false;
            }
            catch (Exception ex)
            {
                Log($"Navigraph: Token Polling Fehler: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Extract username from JWT access token
        /// </summary>
        private void ExtractUsernameFromToken()
        {
            try
            {
                if (string.IsNullOrEmpty(AccessToken))
                    return;

                var claims = ParseJwtClaims(AccessToken);
                if (claims == null)
                {
                    Username = "Unknown";
                    return;
                }

                // Try different claim types for username
                Username = GetClaimValue(claims, "preferred_username")
                    ?? GetClaimValue(claims, "name")
                    ?? GetClaimValue(claims, "sub")
                    ?? "Unknown";
            }
            catch (Exception ex)
            {
                Log($"Navigraph: Fehler beim Lesen des Tokens: {ex.Message}");
                Username = "Unknown";
            }
        }

        /// <summary>
        /// Parse JWT token claims (manual parsing without external dependencies)
        /// </summary>
        private JObject ParseJwtClaims(string token)
        {
            try
            {
                // JWT format: header.payload.signature
                var parts = token.Split('.');
                if (parts.Length != 3)
                    return null;

                // Decode payload (second part)
                var payload = parts[1];

                // Add padding if needed
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }

                // Replace URL-safe characters
                payload = payload.Replace('-', '+').Replace('_', '/');

                var bytes = Convert.FromBase64String(payload);
                var json = Encoding.UTF8.GetString(bytes);

                return JObject.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get claim value from JWT claims
        /// </summary>
        private string GetClaimValue(JObject claims, string claimType)
        {
            if (claims == null)
                return null;

            return claims[claimType]?.ToString();
        }

        /// <summary>
        /// Check if user has fmsdata subscription
        /// </summary>
        public async Task<bool> CheckSubscriptionAsync()
        {
            try
            {
                if (!IsAuthenticated)
                {
                    // Try to refresh token first
                    if (!string.IsNullOrEmpty(RefreshToken))
                    {
                        await RefreshTokenAsync();
                    }

                    if (!IsAuthenticated)
                    {
                        HasFmsDataSubscription = false;
                        return false;
                    }
                }

                Log("Navigraph: Prüfe Subscription...");

                // First check token claims for subscription info
                if (CheckTokenForFmsDataScope())
                {
                    Log("Navigraph: FMS Data Subscription via Token-Claims gefunden");
                    HasFmsDataSubscription = true;
                    return true;
                }

                // Otherwise query subscriptions API
                var request = new HttpRequestMessage(HttpMethod.Get, SUBSCRIPTIONS_ENDPOINT);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken);

                var response = await _httpClient.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();

                Log($"Navigraph: Subscription API Response: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    // API can return either an array or an object with subscriptions array
                    JToken json;
                    try
                    {
                        json = JToken.Parse(responseBody);
                    }
                    catch
                    {
                        Log($"Navigraph: Konnte Subscription Response nicht parsen: {responseBody.Substring(0, Math.Min(200, responseBody.Length))}");
                        HasFmsDataSubscription = false;
                        return false;
                    }

                    // Handle array response (list of subscriptions)
                    JArray subs = null;
                    if (json is JArray array)
                    {
                        subs = array;
                    }
                    else if (json is JObject obj)
                    {
                        // Try to find subscriptions in object
                        subs = obj["subscriptions"] as JArray ?? obj["items"] as JArray;
                    }

                    if (subs != null && subs.Count > 0)
                    {
                        Log($"Navigraph: {subs.Count} Subscriptions gefunden");

                        foreach (var sub in subs)
                        {
                            // API returns: type, subscription_name, date_active, date_expiry
                            var type = sub["type"]?.ToString() ?? "";
                            var subscriptionName = sub["subscription_name"]?.ToString() ?? "";

                            Log($"Navigraph: Subscription: type={type}, name={subscriptionName}");

                            // Check if type is "fmsdata" (exact match from API)
                            if (type.Equals("fmsdata", StringComparison.OrdinalIgnoreCase))
                            {
                                HasFmsDataSubscription = true;
                                Log($"Navigraph: FMS Data Subscription gefunden ({subscriptionName})");
                                return true;
                            }
                        }
                    }
                    else
                    {
                        Log($"Navigraph: Keine Subscriptions in Response gefunden");
                    }
                }
                else
                {
                    Log($"Navigraph: Subscription API Fehler: {responseBody.Substring(0, Math.Min(500, responseBody.Length))}");
                }

                HasFmsDataSubscription = false;
                Log("Navigraph: Keine FMS Data Subscription gefunden - verwende Bundled Database");
                return false;
            }
            catch (Exception ex)
            {
                Log($"Navigraph: Subscription Check Fehler: {ex.Message}");
                HasFmsDataSubscription = false;
                return false;
            }
        }

        /// <summary>
        /// Check if fmsdata scope is in the access token
        /// </summary>
        private bool CheckTokenForFmsDataScope()
        {
            try
            {
                if (string.IsNullOrEmpty(AccessToken))
                    return false;

                var claims = ParseJwtClaims(AccessToken);
                if (claims == null)
                    return false;

                // Check scope claim
                var scope = GetClaimValue(claims, "scope");
                if (!string.IsNullOrEmpty(scope) && scope.Contains("fmsdata"))
                {
                    return true;
                }

                // Check for subscription claim
                var subs = GetClaimValue(claims, "subscriptions");
                if (!string.IsNullOrEmpty(subs) && subs.Contains("fmsdata"))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Refresh the access token using refresh token
        /// </summary>
        public async Task<bool> RefreshTokenAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(RefreshToken))
                {
                    Log("Navigraph: Kein Refresh Token vorhanden");
                    return false;
                }

                Log("Navigraph: Erneuere Token...");

                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "client_id", ClientId },
                    { "client_secret", ClientSecret },
                    { "grant_type", "refresh_token" },
                    { "refresh_token", RefreshToken }
                });

                var response = await _httpClient.PostAsync(TOKEN_ENDPOINT, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Log($"Navigraph: Token Refresh fehlgeschlagen: {response.StatusCode}");
                    // Clear invalid tokens
                    Logout();
                    return false;
                }

                var json = JObject.Parse(responseBody);

                AccessToken = json["access_token"]?.ToString();
                var newRefreshToken = json["refresh_token"]?.ToString();
                if (!string.IsNullOrEmpty(newRefreshToken))
                {
                    RefreshToken = newRefreshToken;
                }

                int expiresIn = json["expires_in"]?.ToObject<int>() ?? 3600;
                TokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);

                // Extract username
                ExtractUsernameFromToken();

                // Save tokens
                SaveTokensToSettings();

                Log($"Navigraph: Token erfolgreich erneuert");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Navigraph: Token Refresh Fehler: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Ensure we have a valid token, refreshing if necessary
        /// </summary>
        public async Task<bool> EnsureValidTokenAsync()
        {
            if (IsAuthenticated)
            {
                // Check if token expires in less than 5 minutes
                if (TokenExpiry > DateTime.UtcNow.AddMinutes(5))
                {
                    return true;
                }
            }

            // Try to refresh
            return await RefreshTokenAsync();
        }

        /// <summary>
        /// Logout and clear all tokens
        /// </summary>
        public void Logout()
        {
            Log("Navigraph: Abmelden...");

            AccessToken = null;
            RefreshToken = null;
            TokenExpiry = DateTime.MinValue;
            Username = null;
            HasFmsDataSubscription = false;

            // Clear settings
            Settings.Default.NavigraphAccessToken = "";
            Settings.Default.NavigraphRefreshToken = "";
            Settings.Default.NavigraphUsername = "";
            Settings.Default.NavigraphTokenExpiry = "";
            Settings.Default.Save();

            // Notify status change
            OnStatusChanged?.Invoke(this, GetStatus());

            Log("Navigraph: Erfolgreich abgemeldet");
        }

        /// <summary>
        /// Get current authentication status
        /// </summary>
        public NavigraphStatus GetStatus()
        {
            return new NavigraphStatus
            {
                IsAuthenticated = IsAuthenticated,
                Username = Username,
                HasFmsDataSubscription = HasFmsDataSubscription
            };
        }

        // Navigation Data API
        private const string NAVDATA_API = "https://api.navigraph.com/v1/navdata/packages";
        private const string NAVDATA_INFO_API = "https://navdata.api.navigraph.com/info";

        /// <summary>
        /// Get current AIRAC cycle info from Navigraph (no package access needed)
        /// </summary>
        public async Task<string> GetCurrentAiracCycleAsync()
        {
            try
            {
                if (!await EnsureValidTokenAsync())
                {
                    return null;
                }

                var request = new HttpRequestMessage(HttpMethod.Get, NAVDATA_INFO_API);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken);

                Log($"Navigraph: Rufe AIRAC Info ab: {NAVDATA_INFO_API}");

                var response = await _httpClient.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();

                Log($"Navigraph: Info API Status: {response.StatusCode}");
                Log($"Navigraph: Info API Response: {responseBody}");

                if (response.IsSuccessStatusCode)
                {
                    var json = JObject.Parse(responseBody);
                    return json["cycle"]?.ToString();
                }

                return null;
            }
            catch (Exception ex)
            {
                Log($"Navigraph: AIRAC Info Fehler: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Download the latest navigation database
        /// </summary>
        public async Task<NavigraphDatabaseInfo> DownloadLatestDatabaseAsync(string targetDirectory)
        {
            try
            {
                if (!await EnsureValidTokenAsync())
                {
                    Log("Navigraph: Kein gültiger Token für Datenbank-Download");
                    return null;
                }

                Log("Navigraph: Lade verfügbare Pakete...");

                // Try different format parameters - Navigraph assigns formats to clients
                // Common formats: dfd (DFD v1), dfd_v2 (DFD v2), or client-specific
                string[] formatsToTry = { "", "dfd", "dfd_v2", "navigraph_dfd" };
                JArray allPackages = null;

                foreach (var format in formatsToTry)
                {
                    var url = string.IsNullOrEmpty(format)
                        ? NAVDATA_API
                        : $"{NAVDATA_API}?format={format}";

                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken);

                    Log($"Navigraph: Versuche Package API mit format='{format}': {url}");

                    var response = await _httpClient.SendAsync(request);
                    var responseBody = await response.Content.ReadAsStringAsync();

                    Log($"Navigraph: Response Status: {response.StatusCode}");
                    Log($"Navigraph: Response Body: {responseBody}");

                    if (response.IsSuccessStatusCode)
                    {
                        // API might return object with packages array or direct array
                        JToken parsed = JToken.Parse(responseBody);
                        JArray packages;

                        if (parsed is JArray arr)
                        {
                            packages = arr;
                        }
                        else if (parsed is JObject obj)
                        {
                            // Try common wrapper properties
                            packages = obj["packages"] as JArray
                                ?? obj["items"] as JArray
                                ?? obj["data"] as JArray;

                            if (packages == null)
                            {
                                Log($"Navigraph: Response ist ein Objekt mit Keys: {string.Join(", ", obj.Properties().Select(p => p.Name))}");
                                continue;
                            }
                        }
                        else
                        {
                            continue;
                        }

                        Log($"Navigraph: {packages.Count} Paket(e) mit format='{format}'");

                        if (packages.Count > 0)
                        {
                            allPackages = packages;
                            break;
                        }
                    }
                }

                if (allPackages == null || allPackages.Count == 0)
                {
                    Log("Navigraph: HINWEIS - Keine Pakete gefunden!");
                    Log("Navigraph: Der Client muss bei Navigraph für Navigation Data API registriert werden.");
                    Log("Navigraph: Bitte kontaktiere dev@navigraph.com um Zugang zu erhalten.");
                    return null;
                }

                Log($"Navigraph: {allPackages.Count} Paket(e) gefunden");

                // Filter for DFD format (SQLite database)
                JToken package = null;
                foreach (var pkg in allPackages)
                {
                    var format = pkg["format"]?.ToString()?.ToLower() ?? "";
                    Log($"Navigraph: Package format: {format}, cycle: {pkg["cycle"]}");

                    if (format.Contains("dfd") || format.Contains("sqlite") || format.Contains("fmsdata"))
                    {
                        package = pkg;
                        break;
                    }
                }

                if (package == null && allPackages.Count > 0)
                {
                    // Fallback: use first package if no DFD found
                    package = allPackages[0];
                    Log($"Navigraph: Kein DFD-Format gefunden, verwende erstes Paket: {package["format"]}");
                }

                if (package == null)
                {
                    Log("Navigraph: Keine aktuellen Pakete gefunden");
                    return null;
                }
                var cycle = package["cycle"]?.ToString();
                var revision = package["revision"]?.ToString();
                var files = package["files"] as JArray;

                Log($"Navigraph: Aktueller AIRAC Cycle: {cycle} (Revision {revision})");

                if (files == null || files.Count == 0)
                {
                    Log("Navigraph: Keine Dateien im Paket");
                    return null;
                }

                // Find the SQLite database file
                JToken dbFile = null;
                foreach (var file in files)
                {
                    var key = file["key"]?.ToString() ?? "";
                    if (key.EndsWith(".s3db") || key.EndsWith(".sqlite") || key.EndsWith(".db"))
                    {
                        dbFile = file;
                        break;
                    }
                }

                if (dbFile == null)
                {
                    // Take first file if no .s3db found
                    dbFile = files[0];
                }

                var signedUrl = dbFile["signed_url"]?.ToString();
                var hash = dbFile["hash"]?.ToString();
                var fileName = dbFile["key"]?.ToString() ?? $"ng_navdata_{cycle}.s3db";

                if (string.IsNullOrEmpty(signedUrl))
                {
                    Log("Navigraph: Keine Download-URL gefunden");
                    return null;
                }

                Log($"Navigraph: Lade Datenbank herunter: {fileName}");

                // Download the file
                var downloadResponse = await _httpClient.GetAsync(signedUrl);
                if (!downloadResponse.IsSuccessStatusCode)
                {
                    Log($"Navigraph: Download fehlgeschlagen: {downloadResponse.StatusCode}");
                    return null;
                }

                // Ensure target directory exists
                if (!System.IO.Directory.Exists(targetDirectory))
                {
                    System.IO.Directory.CreateDirectory(targetDirectory);
                }

                var targetPath = System.IO.Path.Combine(targetDirectory, $"ng_jeppesen_fwdfd_{cycle}.s3db");
                var bytes = await downloadResponse.Content.ReadAsByteArrayAsync();

                // Verify hash if provided
                if (!string.IsNullOrEmpty(hash))
                {
                    using (var sha256 = SHA256.Create())
                    {
                        var computedHash = BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", "").ToLower();
                        if (computedHash != hash.ToLower())
                        {
                            Log($"Navigraph: Hash-Prüfung fehlgeschlagen! Erwartet: {hash}, Berechnet: {computedHash}");
                            return null;
                        }
                        Log("Navigraph: Hash-Prüfung erfolgreich");
                    }
                }

                // Save file
                System.IO.File.WriteAllBytes(targetPath, bytes);
                Log($"Navigraph: Datenbank gespeichert: {targetPath} ({bytes.Length / 1024 / 1024} MB)");

                return new NavigraphDatabaseInfo
                {
                    Cycle = cycle,
                    Revision = revision,
                    FilePath = targetPath,
                    FileSize = bytes.Length
                };
            }
            catch (Exception ex)
            {
                Log($"Navigraph: Datenbank-Download Fehler: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get info about latest available database without downloading
        /// </summary>
        public async Task<NavigraphDatabaseInfo> GetLatestDatabaseInfoAsync()
        {
            try
            {
                if (!await EnsureValidTokenAsync())
                {
                    return null;
                }

                // Try different format parameters
                string[] formatsToTry = { "", "dfd", "dfd_v2", "navigraph_dfd" };
                JArray allPackages = null;

                foreach (var format in formatsToTry)
                {
                    var url = string.IsNullOrEmpty(format)
                        ? NAVDATA_API
                        : $"{NAVDATA_API}?format={format}";

                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken);

                    Log($"Navigraph: Prüfe Pakete mit format='{format}'");

                    var response = await _httpClient.SendAsync(request);
                    var responseBody = await response.Content.ReadAsStringAsync();

                    Log($"Navigraph: GetInfo Response: {responseBody}");

                    if (response.IsSuccessStatusCode)
                    {
                        JToken parsed = JToken.Parse(responseBody);
                        JArray packages;

                        if (parsed is JArray arr)
                        {
                            packages = arr;
                        }
                        else if (parsed is JObject obj)
                        {
                            packages = obj["packages"] as JArray
                                ?? obj["items"] as JArray
                                ?? obj["data"] as JArray;

                            if (packages == null)
                            {
                                Log($"Navigraph: Response Keys: {string.Join(", ", obj.Properties().Select(p => p.Name))}");
                                continue;
                            }
                        }
                        else
                        {
                            continue;
                        }

                        if (packages.Count > 0)
                        {
                            allPackages = packages;
                            Log($"Navigraph: {packages.Count} Paket(e) gefunden mit format='{format}'");
                            break;
                        }
                    }
                }

                if (allPackages == null || allPackages.Count == 0)
                {
                    Log("Navigraph: Keine Pakete gefunden - Client benötigt Navigation Data API Zugang");
                    return null;
                }

                // Filter for DFD format
                JToken package = null;
                foreach (var pkg in allPackages)
                {
                    var format = pkg["format"]?.ToString()?.ToLower() ?? "";
                    if (format.Contains("dfd") || format.Contains("sqlite") || format.Contains("fmsdata"))
                    {
                        package = pkg;
                        break;
                    }
                }

                if (package == null)
                {
                    package = allPackages[0];
                }

                return new NavigraphDatabaseInfo
                {
                    Cycle = package["cycle"]?.ToString(),
                    Revision = package["revision"]?.ToString(),
                    PackageStatus = package["package_status"]?.ToString()
                };
            }
            catch
            {
                return null;
            }
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

    /// <summary>
    /// Information about a Navigraph database
    /// </summary>
    public class NavigraphDatabaseInfo
    {
        public string Cycle { get; set; }
        public string Revision { get; set; }
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public string PackageStatus { get; set; }
    }
}
