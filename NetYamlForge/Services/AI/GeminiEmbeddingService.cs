using System.Text;
using System.Text.Json;

namespace NetYamlForge.Services.AI;

public interface IGeminiEmbeddingService
{
    Task<float[]?> EmbedAsync(string text, CancellationToken ct = default);
}

public class GeminiEmbeddingService : IGeminiEmbeddingService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private const string Model = "text-embedding-004";

    private readonly IConfiguration _config;
    private readonly ILogger<GeminiEmbeddingService> _logger;

    public GeminiEmbeddingService(IConfiguration config, ILogger<GeminiEmbeddingService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<float[]?> EmbedAsync(string text, CancellationToken ct = default)
    {
        var apiKey = _config["GEMINI_API_KEY"]
                     ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("GEMINI_API_KEY not configured — embedding skipped");
            return null;
        }

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:embedContent?key={apiKey}";
        var body = JsonSerializer.Serialize(new
        {
            model = $"models/{Model}",
            content = new { parts = new[] { new { text } } }
        });

        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        try
        {
            var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
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
}
