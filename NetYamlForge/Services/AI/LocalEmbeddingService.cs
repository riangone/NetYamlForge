using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NetYamlForge.Services.AI;

/// <summary>
/// Local embedding service using Python sentence-transformers (paraphrase-multilingual-MiniLM-L12-v2).
/// Starts local_embed_server.py on port 5789 on first use.
/// No external API key required — fully offline, supports 50+ languages.
/// </summary>
public class LocalEmbeddingService : IGeminiEmbeddingService
{
    private const int Port = 5789;
    private const string BaseUrl = "http://127.0.0.1:5789";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly SemaphoreSlim _startLock = new(1, 1);
    private static Process? _serverProcess;
    private static bool _serverReady;

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<LocalEmbeddingService> _logger;

    public LocalEmbeddingService(IWebHostEnvironment env, ILogger<LocalEmbeddingService> logger)
    {
        _env = env;
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

        await EnsureServerAsync(ct);
        if (!_serverReady)
        {
            _logger.LogWarning("Local embedding server is not running");
            return Enumerable.Repeat<float[]?>(null, texts.Count).ToList();
        }

        var body = JsonSerializer.Serialize(new { texts });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            var resp = await _http.PostAsync($"{BaseUrl}/embed_batch", content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogError("Embedding server {Status}: {Error}", (int)resp.StatusCode, err);
                return Enumerable.Repeat<float[]?>(null, texts.Count).ToList();
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("embeddings")
                .EnumerateArray()
                .Select(e => e.EnumerateArray().Select(v => v.GetSingle()).ToArray() as float[])
                .Cast<float[]?>()
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch embedding request failed");
            return Enumerable.Repeat<float[]?>(null, texts.Count).ToList();
        }
    }

    private async Task EnsureServerAsync(CancellationToken ct)
    {
        if (_serverReady && await IsHealthyAsync(ct)) return;

        await _startLock.WaitAsync(ct);
        try
        {
            if (_serverReady && await IsHealthyAsync(ct)) return;
            _serverReady = false;
            await StartServerAsync(ct);
        }
        finally
        {
            _startLock.Release();
        }
    }

    private async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            var resp = await _http.GetAsync($"{BaseUrl}/health", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task StartServerAsync(CancellationToken ct)
    {
        // If already running externally (e.g. from a previous process), use it
        if (await IsHealthyAsync(ct))
        {
            _logger.LogInformation("Local embedding server already running on port {Port}", Port);
            _serverReady = true;
            return;
        }

        // Kill stale process
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            try { _serverProcess.Kill(); } catch { }
        }
        _serverProcess = null;

        var scriptPath = Path.Combine(_env.ContentRootPath, "local_embed_server.py");
        if (!File.Exists(scriptPath))
        {
            _logger.LogError("Embedding script not found at {Path}", scriptPath);
            return;
        }

        _logger.LogInformation("Starting local embedding server: python3 {Script}", scriptPath);
        _serverProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "python3",
            Arguments = $"\"{scriptPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _env.ContentRootPath
        });

        if (_serverProcess == null)
        {
            _logger.LogError("Failed to spawn Python embedding server process");
            return;
        }

        // Wait up to 90s for model to load (first run may download the model ~100MB)
        _logger.LogInformation("Waiting for embedding model to load (may take ~30s on first run)...");
        for (var i = 0; i < 90; i++)
        {
            if (ct.IsCancellationRequested) return;
            if (_serverProcess.HasExited)
            {
                _logger.LogError("Embedding server process exited unexpectedly (exit code {Code})", _serverProcess.ExitCode);
                return;
            }
            if (await IsHealthyAsync(ct))
            {
                _serverReady = true;
                _logger.LogInformation("Local embedding server ready (port {Port})", Port);
                return;
            }
            await Task.Delay(1000, ct);
        }

        _logger.LogError("Local embedding server did not become healthy within 90 seconds");
    }
}
