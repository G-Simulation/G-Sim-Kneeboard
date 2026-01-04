using System;
using System.Collections.Generic;
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
        private const string SUBSCRIPTIONS_ENDPOINT = "https://subscriptions.api.navigraph.com/v1/subscriptions";
        private const string USERINFO_ENDPOINT = AUTH_BASE + "/connect/userinfo";

        // OAuth Client Configuration
        // These should be replaced with actual Navigraph developer credentials
        private const string DEFAULT_CLIENT_ID = "kneeboard-server";
        private const string SCOPE = "openid offline_access fmsdata";

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

        public string ClientId => !string.IsNullOrEmpty(Settings.Default.NavigraphClientId)
            ? Settings.Default.NavigraphClientId
            : DEFAULT_CLIENT_ID;

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
                    { "scope", SCOPE },
                    { "code_challenge", _codeChallenge },
                    { "code_challenge_method", "S256" }
                });

                var response = await _httpClient.PostAsync(DEVICE_AUTH_ENDPOINT, content);
                var responseBody = await response.Content.ReadAsStringAsync();

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
                        { "grant_type", "urn:ietf:params:oauth:grant-type:device_code" },
                        { "device_code", deviceCode },
                        { "code_verifier", _codeVerifier }
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

                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(responseBody);

                    // Check for fmsdata subscription
                    var subs = json["subscriptions"] as JArray;
                    if (subs != null)
                    {
                        foreach (var sub in subs)
                        {
                            var type = sub["type"]?.ToString();
                            if (type == "fmsdata" || type == "ultimate")
                            {
                                HasFmsDataSubscription = true;
                                Log($"Navigraph: FMS Data Subscription gefunden ({type})");
                                return true;
                            }
                        }
                    }
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
