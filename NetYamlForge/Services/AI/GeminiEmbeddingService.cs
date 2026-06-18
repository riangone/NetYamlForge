using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NetYamlForge.Services.AI;

public interface IGeminiEmbeddingService
{
    Task<float[]?> EmbedAsync(string text, CancellationToken ct = default);
    Task<List<float[]?>> EmbedBatchAsync(IList<string> texts, CancellationToken ct = default);
}

public class GeminiEmbeddingService : IGeminiEmbeddingService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private const string Model = "text-embedding-004";

    // Gemini CLI OAuth app credentials — read dynamically from the CLI bundle at runtime
    private static string? _oauthClientId;
    private static string? _oauthClientSecret;

    private static readonly string OAuthCredsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "oauth_creds.json");

    private static string? _cachedToken;
    private static long _tokenExpiryMs;
    private static readonly SemaphoreSlim _refreshLock = new(1, 1);

    private readonly IConfiguration _config;
    private readonly ILogger<GeminiEmbeddingService> _logger;

    public GeminiEmbeddingService(IConfiguration config, ILogger<GeminiEmbeddingService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<List<float[]?>> EmbedBatchAsync(IList<string> texts, CancellationToken ct = default)
    {
        var results = new List<float[]?>();
        foreach (var t in texts)
            results.Add(await EmbedAsync(t, ct));
        return results;
    }

    public async Task<float[]?> EmbedAsync(string text, CancellationToken ct = default)
    {
        var apiKey = _config["GEMINI_API_KEY"]
                     ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        string? authToken = null;
        string? requestUrl;

        if (!string.IsNullOrEmpty(apiKey))
        {
            requestUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:embedContent?key={apiKey}";
        }
        else
        {
            authToken = await GetValidOAuthTokenAsync(ct);
            if (authToken == null)
            {
                _logger.LogWarning("No GEMINI_API_KEY and no valid OAuth token — embedding skipped");
                return null;
            }
            requestUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:embedContent";
        }

        var body = JsonSerializer.Serialize(new
        {
            model = $"models/{Model}",
            content = new { parts = new[] { new { text } } }
        });

        var req = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (authToken != null)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        try
        {
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogError("Gemini embedding API {Status}: {Error}", (int)resp.StatusCode, err);
                return null;
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("embedding")
                .GetProperty("values")
                .EnumerateArray()
                .Select(v => v.GetSingle())
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini embedding API call failed");
            return null;
        }
    }

    private async Task<string?> GetValidOAuthTokenAsync(CancellationToken ct)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Return cached token if still valid (with 60-second buffer)
        if (_cachedToken != null && _tokenExpiryMs - 60_000 > nowMs)
            return _cachedToken;

        await _refreshLock.WaitAsync(ct);
        try
        {
            // Double-check after lock
            nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_cachedToken != null && _tokenExpiryMs - 60_000 > nowMs)
                return _cachedToken;

            return await LoadOrRefreshTokenAsync(nowMs, ct);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<string?> LoadOrRefreshTokenAsync(long nowMs, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(OAuthCredsPath)) return null;

            var json = await File.ReadAllTextAsync(OAuthCredsPath, ct);
            var node = JsonNode.Parse(json);
            if (node == null) return null;

            var accessToken = node["access_token"]?.GetValue<string>();
            var expiryDate = node["expiry_date"]?.GetValue<long>() ?? 0L;
            var refreshToken = node["refresh_token"]?.GetValue<string>();

            // Token still valid
            if (!string.IsNullOrEmpty(accessToken) && expiryDate - 60_000 > nowMs)
            {
                _cachedToken = accessToken;
                _tokenExpiryMs = expiryDate;
                return accessToken;
            }

            // Need refresh
            if (string.IsNullOrEmpty(refreshToken))
            {
                _logger.LogWarning("Gemini OAuth token expired and no refresh_token available");
                return null;
            }

            var newToken = await RefreshAccessTokenAsync(refreshToken, ct);
            if (newToken == null) return null;

            // Persist refreshed token back to creds file
            node["access_token"] = newToken.Value.AccessToken;
            node["expiry_date"] = newToken.Value.ExpiryMs;
            await File.WriteAllTextAsync(OAuthCredsPath, node.ToJsonString(), ct);

            _cachedToken = newToken.Value.AccessToken;
            _tokenExpiryMs = newToken.Value.ExpiryMs;
            return newToken.Value.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load/refresh Gemini OAuth token from {Path}", OAuthCredsPath);
            return null;
        }
    }

    private static (string? ClientId, string? ClientSecret) ReadCliOAuthCredentials()
    {
        // Read OAuth client credentials from the installed Gemini CLI bundle at runtime
        // This avoids hardcoding secrets in source code
        try
        {
            var geminiCli = "/usr/local/lib/node_modules/@google/gemini-cli/bundle/chunk-QH43L44B.js";
            if (!File.Exists(geminiCli)) return (null, null);

            var content = File.ReadAllText(geminiCli);

            var idMatch = System.Text.RegularExpressions.Regex.Match(
                content, @"OAUTH_CLIENT_ID\s*=\s*""([^""]+)""");
            var secretMatch = System.Text.RegularExpressions.Regex.Match(
                content, @"OAUTH_CLIENT_SECRET\s*=\s*""([^""]+)""");

            return idMatch.Success && secretMatch.Success
                ? (idMatch.Groups[1].Value, secretMatch.Groups[1].Value)
                : (null, null);
        }
        catch { return (null, null); }
    }

    private async Task<(string AccessToken, long ExpiryMs)?> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct)
    {
        if (_oauthClientId == null || _oauthClientSecret == null)
        {
            (_oauthClientId, _oauthClientSecret) = ReadCliOAuthCredentials();
        }

        if (string.IsNullOrEmpty(_oauthClientId) || string.IsNullOrEmpty(_oauthClientSecret))
        {
            _logger.LogWarning("Could not locate Gemini CLI OAuth credentials for token refresh");
            return null;
        }

        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"]     = _oauthClientId,
            ["client_secret"] = _oauthClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"]    = "refresh_token"
        });

        try
        {
            var resp = await _http.PostAsync("https://oauth2.googleapis.com/token", body, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogError("OAuth token refresh failed {Status}: {Error}", (int)resp.StatusCode, err);
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var accessToken = root.GetProperty("access_token").GetString()!;
            var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt64() : 3600L;
            var expiryMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + expiresIn * 1000;

            _logger.LogInformation("Gemini OAuth token refreshed, expires in {Sec}s", expiresIn);
            return (accessToken, expiryMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth token refresh request failed");
            return null;
        }
    }
}
