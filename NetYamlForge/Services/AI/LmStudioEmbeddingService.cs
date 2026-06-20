using System.Text;
using System.Text.Json;

namespace NetYamlForge.Services.AI;

public class LmStudioEmbeddingService : IEmbeddingService, IGeminiEmbeddingService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private readonly IConfiguration _config;
    private readonly ILogger<LmStudioEmbeddingService> _logger;

    public LmStudioEmbeddingService(IConfiguration config, ILogger<LmStudioEmbeddingService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<float[]?> EmbedAsync(string text, CancellationToken ct = default)
    {
        var results = await EmbedBatchAsync([text], ct);
        return results.Count > 0 ? results[0] : null;
    }

    public async Task<List<float[]?>> EmbedBatchAsync(IList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];

        var baseUrl = _config["EMBEDDING_LMSTUDIO_BASE_URL"]
                      ?? Environment.GetEnvironmentVariable("EMBEDDING_LMSTUDIO_BASE_URL")
                      ?? "http://localhost:1234/v1";

        var model = _config["EMBEDDING_LMSTUDIO_MODEL"]
                    ?? Environment.GetEnvironmentVariable("EMBEDDING_LMSTUDIO_MODEL")
                    ?? "text-embedding-nomic-embed-text-v1.5";

        var body = JsonSerializer.Serialize(new
        {
            model,
            input = texts
        });

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            var resp = await _http.PostAsync($"{baseUrl}/embeddings", content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogError("LM Studio embedding API {Status}: {Error}", (int)resp.StatusCode, err);
                return Enumerable.Repeat<float[]?>(null, texts.Count).ToList();
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var dataArray = doc.RootElement.GetProperty("data");
            var results = new List<float[]?>();

            foreach (var item in dataArray.EnumerateArray())
            {
                var vec = item.GetProperty("embedding")
                    .EnumerateArray()
                    .Select(v => v.GetSingle())
                    .ToArray();
                results.Add(vec);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LM Studio batch embedding request failed");
            return Enumerable.Repeat<float[]?>(null, texts.Count).ToList();
        }
    }
}
