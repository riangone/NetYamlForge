// DCS001 抑制理由: テーブル名・カラム名はすべて IsValidIdentifier() で検証済みの設定値のみを使用する動的SQL生成ユーティリティです。
#pragma warning disable DCS001
using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// Generic AI annotator — works with any entity table, not just photos.
/// Configure via jobs.yml:
///   type: ai_annotator
///   aiProvider: lmstudio | antigravity | gemini | ollama | anthropic
///   batchSize: 5
///   annotationConfig:
///     sourceTable: documents
///     primaryKey: doc_id
///     filePathField: file_path
///     statusField: ai_status
///     queueTable: annotation_queue
///     embeddingTable: doc_embeddings
///     annotationPrompt: "..."   # optional — overrides built-in photo prompt
///     resultFields:             # JSON key → DB column (null = skip)
///       caption_short: summary
///       tags: null
///     autoEmbed: true
/// </summary>
public class AiAnnotatorExecutor : IBatchStepHandler
{
    public string StepType => "ai_annotator";

    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly IEmbeddingService _embedding;
    private readonly ILogger<AiAnnotatorExecutor> _logger;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

    private const string DefaultAnnotationPrompt = """
        你是专业的图片分析AI。请仔细分析图片，仅输出合法JSON（不加任何说明、不使用markdown代码块），字段如下：
        {
          "caption_short": "25字以内的简洁中文描述",
          "caption_long": "100字以内的详细中文描述，包含画面内容、色调、氛围",
          "scene_type": "场景类别，必须是以下之一：indoor/outdoor/portrait/landscape/food/architecture/street/nature/event/abstract/vehicle/document/other",
          "subjects": "主体对象，中文逗号分隔，如：人物,建筑,动物",
          "activities": "画面中可见的活动，中文逗号分隔，无则填null",
          "tags": ["中文标签1", "中文标签2", "最多15个"],
          "person_count": 0,
          "confidence_score": 0.90
        }
        """;

    // Default photo-schema result field mapping (annotation JSON key → DB column)
    private static readonly Dictionary<string, string?> DefaultResultFields = new()
    {
        ["caption_short"]    = "caption_short",
        ["caption_long"]     = "caption_long",
        ["scene_type"]       = "scene_type",
        ["subjects"]         = "subjects",
        ["activities"]       = "activities",
        ["person_count"]     = "person_count",
        ["confidence_score"] = "confidence_score",
        ["annotation_model"] = "annotation_model",
    };

    // Default text fields for building embedding input
    private static readonly List<EmbeddingTextField> DefaultEmbedTextFields =
    [
        new() { Field = "caption_short" },
        new() { Field = "caption_long" },
        new() { Field = "scene_type",   Prefix = "场景" },
        new() { Field = "subjects",     Prefix = "主体" },
        new() { Field = "activities",   Prefix = "活动" },
    ];

    public AiAnnotatorExecutor(
        IWebHostEnvironment env,
        IConfiguration configuration,
        IEmbeddingService embedding,
        ILogger<AiAnnotatorExecutor> logger)
    {
        _env = env;
        _configuration = configuration;
        _embedding = embedding;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx,
        BatchJobResult result, CancellationToken ct)
    {
        var cfg      = job.AnnotationConfig ?? new AnnotationJobConfig();
        var provider = job.AiProvider ?? "antigravity";
        var batch    = job.BatchSize > 0 ? job.BatchSize : 5;
        var prompt   = cfg.AnnotationPrompt ?? DefaultAnnotationPrompt;

        var rows = (await db.QueryAsync<QueueRow>(
            $"""
            SELECT q.queue_id, q.{cfg.PrimaryKey} AS pk_value, q.{cfg.FilePathField} AS file_path,
                   q.provider, q.retry_count
            FROM {cfg.QueueTable} q
            WHERE q.status = 'queued' AND q.provider = @Provider
            ORDER BY q.priority DESC, q.queued_at ASC
            LIMIT @Batch
            """,
            new { Batch = batch, Provider = provider },
            transaction: tx)).ToList();

        if (rows.Count == 0)
        {
            result.Success = true;
            result.RowsAffected = 0;
            return;
        }

        var done = 0; var failed = 0;

        foreach (var row in rows)
        {
            if (ct.IsCancellationRequested) break;
            var now = DateTime.UtcNow;

            await db.ExecuteAsync(
                $"UPDATE {cfg.QueueTable} SET status='processing', started_at=@Now WHERE queue_id=@Id",
                new { Now = now, Id = row.queue_id }, transaction: tx);

            try
            {
                var absolutePath = ResolveFilePath(row.file_path);
                if (!File.Exists(absolutePath))
                {
                    await FailRow(db, tx, row, cfg, $"File not found: {absolutePath}");
                    failed++;
                    continue;
                }

                var usedProvider = string.IsNullOrEmpty(row.provider) ? provider : row.provider;
                var annotation = await AnnotateAsync(absolutePath, usedProvider, prompt, projectName, ct);

                if (annotation == null)
                {
                    await FailRow(db, tx, row, cfg, "AI returned empty or unparseable response");
                    failed++;
                    continue;
                }

                await WriteAnnotationResult(db, tx, row.pk_value, cfg, annotation, usedProvider, now);

                if (cfg.AutoEmbed)
                    await TryInlineEmbed(db, tx, row.pk_value, cfg, annotation, ct);

                var ms = (long)(DateTime.UtcNow - now).TotalMilliseconds;
                await db.ExecuteAsync(
                    $"UPDATE {cfg.QueueTable} SET status='done', completed_at=@Now, processing_ms=@Ms, provider=@Provider WHERE queue_id=@Id",
                    new { Now = DateTime.UtcNow, Ms = ms, Provider = usedProvider, Id = row.queue_id },
                    transaction: tx);

                done++;
                _logger.LogInformation("Annotated {Table}/{Id} via {Provider}", cfg.SourceTable, row.pk_value, usedProvider);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to annotate {Table}/{Id}", cfg.SourceTable, row.pk_value);
                await FailRow(db, tx, row, cfg, ex.Message);
                failed++;
            }
        }

        result.Success = failed == 0 || done > 0;
        result.RowsAffected = done;
        result.ErrorMessage = failed > 0 ? $"{failed} item(s) failed annotation" : null;
    }

    // ──────────────────────────────────────────────────────────
    // Write annotation result columns back to source table
    // ──────────────────────────────────────────────────────────

    private async Task WriteAnnotationResult(
        IDbConnection db, IDbTransaction tx,
        string pkValue, AnnotationJobConfig cfg,
        AnnotationResult annotation, string provider, DateTime now)
    {
        var fields = cfg.ResultFields ?? DefaultResultFields;
        var setClauses = new List<string>();
        var parameters = new Dictionary<string, object?> { ["PkValue"] = pkValue, ["Now"] = now };

        // Build SET clause from mapping
        foreach (var (jsonKey, dbCol) in fields)
        {
            if (string.IsNullOrEmpty(dbCol)) continue;

            object? val = jsonKey switch
            {
                "caption_short"    => annotation.CaptionShort,
                "caption_long"     => annotation.CaptionLong,
                "scene_type"       => annotation.SceneType,
                "subjects"         => annotation.Subjects,
                "activities"       => annotation.Activities,
                "person_count"     => (object)annotation.PersonCount,
                "confidence_score" => annotation.ConfidenceScore,
                "annotation_model" => GetModelName(provider),
                "tags"             => annotation.Tags != null ? string.Join(",", annotation.Tags) : null,
                _                  => null
            };

            setClauses.Add($"{dbCol} = @p_{jsonKey}");
            parameters[$"p_{jsonKey}"] = val;
        }

        // Always write status, model, and timestamps
        setClauses.Add($"{cfg.StatusField} = 'done'");
        if (!fields.ContainsKey("annotation_model"))
        {
            setClauses.Add("annotation_model = @ModelName");
            parameters["ModelName"] = GetModelName(provider);
        }
        setClauses.Add("annotation_at = @Now");
        setClauses.Add("updated_at = @Now");

        var sql = $"UPDATE {cfg.SourceTable} SET {string.Join(", ", setClauses)} WHERE {cfg.PrimaryKey} = @PkValue";
        await db.ExecuteAsync(sql, parameters, transaction: tx);
    }

    // ──────────────────────────────────────────────────────────
    // Inline embedding after annotation
    // ──────────────────────────────────────────────────────────

    private async Task TryInlineEmbed(
        IDbConnection db, IDbTransaction tx,
        string pkValue, AnnotationJobConfig cfg,
        AnnotationResult annotation, CancellationToken ct)
    {
        try
        {
            await db.ExecuteAsync($"""
                CREATE TABLE IF NOT EXISTS {cfg.EmbeddingTable} (
                    {cfg.PrimaryKey} TEXT NOT NULL PRIMARY KEY,
                    embedding         TEXT NOT NULL,
                    created_at        TEXT NOT NULL
                )
                """, transaction: tx);

            var textFields = cfg.EmbedTextFields ?? DefaultEmbedTextFields;
            var text = BuildEmbedText(annotation, textFields);
            if (string.IsNullOrWhiteSpace(text)) return;

            var vecs = await _embedding.EmbedBatchAsync([text], ct);
            var vec = vecs.Count > 0 ? vecs[0] : null;
            if (vec == null || vec.Length == 0) return;

            await db.ExecuteAsync($"""
                INSERT OR REPLACE INTO {cfg.EmbeddingTable}({cfg.PrimaryKey}, embedding, created_at)
                VALUES (@PkValue, @Embedding, @Now)
                """,
                new { PkValue = pkValue, Embedding = JsonSerializer.Serialize(vec), Now = DateTime.UtcNow.ToString("o") },
                transaction: tx);

            _logger.LogInformation("Inline embedding generated for {Table}/{Id} ({Dims} dims)", cfg.SourceTable, pkValue, vec.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inline embedding failed for {Table}/{Id}, will retry via cron", cfg.SourceTable, pkValue);
        }
    }

    private static string BuildEmbedText(AnnotationResult a, IList<EmbeddingTextField> fields)
    {
        var parts = new List<string>();
        foreach (var f in fields)
        {
            var val = f.Field switch
            {
                "caption_short" => a.CaptionShort,
                "caption_long"  => a.CaptionLong,
                "scene_type"    => a.SceneType,
                "subjects"      => a.Subjects,
                "activities"    => a.Activities,
                _               => null
            };
            if (string.IsNullOrWhiteSpace(val)) continue;
            parts.Add(string.IsNullOrEmpty(f.Prefix) ? val : $"{f.Prefix}: {val}");
        }
        return string.Join(". ", parts);
    }

    // ──────────────────────────────────────────────────────────
    // Provider dispatch
    // ──────────────────────────────────────────────────────────

    private async Task<AnnotationResult?> AnnotateAsync(
        string absolutePath, string provider, string prompt, string? projectName, CancellationToken ct)
        => provider.ToLowerInvariant() switch
        {
            "lmstudio"    => await AnnotateWithLmStudioAsync(absolutePath, prompt, ct),
            "gemini_cli"  => await AnnotateWithAntigravityCliAsync(absolutePath, prompt, ct),
            "gemini"      => await AnnotateWithGeminiAsync(absolutePath, prompt, projectName, ct),
            "ollama"      => await AnnotateWithOllamaAsync(absolutePath, prompt, projectName, ct),
            "antigravity" => await AnnotateWithAntigravityCliAsync(absolutePath, prompt, ct),
            "anthropic"   => await AnnotateWithAnthropicAsync(absolutePath, prompt, projectName, ct),
            _ => LogAndNull(provider)
        };

    private AnnotationResult? LogAndNull(string provider)
    {
        _logger.LogWarning("Unknown annotation provider '{Provider}'", provider);
        return null;
    }

    private async Task<AnnotationResult?> AnnotateWithAntigravityCliAsync(
        string absolutePath, string prompt, CancellationToken ct)
    {
        var fullPrompt = $"@{absolutePath}\n{prompt}";
        var escaped = fullPrompt.Replace("\\", "\\\\").Replace("\"", "\\\"");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "antigravity",
                Arguments = $"-p \"{escaped}\" --dangerously-skip-permissions",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var sb = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        process.ErrorDataReceived  += (_, _) => { };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(180));
        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            _logger.LogError("antigravity CLI timed out");
            return null;
        }
        if (process.ExitCode != 0)
        {
            _logger.LogError("antigravity CLI exited {Code}", process.ExitCode);
            return null;
        }

        var cleaned = Regex.Replace(sb.ToString(), @"\x1B\[[0-9;]*[mGKHFJ]", "");
        cleaned = Regex.Replace(cleaned, @"\x1B\[.*?[a-zA-Z]", "");
        return ParseAnnotationJson(cleaned);
    }

    private async Task<AnnotationResult?> AnnotateWithLmStudioAsync(
        string absolutePath, string prompt, CancellationToken ct)
    {
        var baseUrl = Environment.GetEnvironmentVariable("LMSTUDIO_BASE_URL") ?? "http://localhost:1234/v1";
        var model   = Environment.GetEnvironmentVariable("LMSTUDIO_MODEL") ?? "google/gemma-4-e4b";
        var (base64, mimeType) = EncodeImage(absolutePath);

        var body = new
        {
            model,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text",      text = prompt },
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{base64}" } }
                    }
                }
            },
            max_tokens = 1024,
            temperature = 0.2
        };

        var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        var resp = await _http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { _logger.LogError("LM Studio {Status}: {Body}", resp.StatusCode, json); return null; }

        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        return ParseAnnotationJson(text);
    }

    private async Task<AnnotationResult?> AnnotateWithGeminiAsync(
        string absolutePath, string prompt, string? projectName, CancellationToken ct)
    {
        var apiKey = GetEnvValue("GEMINI_API_KEY", projectName);
        if (string.IsNullOrWhiteSpace(apiKey)) { _logger.LogWarning("GEMINI_API_KEY not set"); return null; }

        var (base64, mimeType) = EncodeImage(absolutePath);
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}";

        var body = new
        {
            contents = new[]
            {
                new { parts = new object[] { new { text = prompt }, new { inline_data = new { mime_type = mimeType, data = base64 } } } }
            },
            generationConfig = new { maxOutputTokens = 1024, temperature = 0.2 }
        };

        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        var resp = await _http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { _logger.LogError("Gemini {Status}: {Body}", resp.StatusCode, json); return null; }

        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
        return ParseAnnotationJson(text);
    }

    private async Task<AnnotationResult?> AnnotateWithOllamaAsync(
        string absolutePath, string prompt, string? projectName, CancellationToken ct)
    {
        var baseUrl = GetEnvValue("OLLAMA_BASE_URL", projectName) ?? "http://localhost:11434";
        var model   = GetEnvValue("OLLAMA_VISION_MODEL", projectName) ?? "llava:13b";
        var (base64, _) = EncodeImage(absolutePath);

        var body = new { model, prompt, images = new[] { base64 }, stream = false };
        var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/generate")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        var resp = await _http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { _logger.LogError("Ollama {Status}: {Body}", resp.StatusCode, json); return null; }

        using var doc = JsonDocument.Parse(json);
        return ParseAnnotationJson(doc.RootElement.GetProperty("response").GetString() ?? "");
    }

    private async Task<AnnotationResult?> AnnotateWithAnthropicAsync(
        string absolutePath, string prompt, string? projectName, CancellationToken ct)
    {
        var apiKey = GetEnvValue("ANTHROPIC_API_KEY", projectName);
        if (string.IsNullOrWhiteSpace(apiKey)) { _logger.LogWarning("ANTHROPIC_API_KEY not set"); return null; }

        var (base64, mediaType) = EncodeImage(absolutePath);
        var model = GetEnvValue("ANTHROPIC_MODEL", projectName) ?? "claude-haiku-4-5-20251001";

        var body = new
        {
            model,
            max_tokens = 1024,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image",  source = new { type = "base64", media_type = mediaType, data = base64 } },
                        new { type = "text",   text = prompt }
                    }
                }
            }
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        var resp = await _http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { _logger.LogError("Anthropic {Status}: {Body}", resp.StatusCode, json); return null; }

        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
        return ParseAnnotationJson(text);
    }

    // ──────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────

    private string ResolveFilePath(string storedPath)
    {
        if (File.Exists(storedPath)) return storedPath;
        return Path.Combine(_env.WebRootPath, storedPath.TrimStart('/'));
    }

    private static (string base64, string mediaType) EncodeImage(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var mime = ext switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png"           => "image/png",
            "gif"           => "image/gif",
            "webp"          => "image/webp",
            "heic" or "heif"=> "image/heic",
            _               => "image/jpeg"
        };
        return (Convert.ToBase64String(bytes), mime);
    }

    private static AnnotationResult? ParseAnnotationJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var cleaned = Regex.Replace(text, @"```(?:json)?\s*", "", RegexOptions.IgnoreCase).Trim();
            var start = cleaned.IndexOf('{');
            var end   = cleaned.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            return JsonSerializer.Deserialize<AnnotationResult>(cleaned[start..(end + 1)],
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private string? GetEnvValue(string key, string? projectName)
    {
        var val = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrEmpty(val)) return val;

        if (!string.IsNullOrEmpty(projectName))
        {
            var envFile = Path.Combine(Directory.GetCurrentDirectory(), "projects", projectName, ".env");
            if (File.Exists(envFile))
            {
                foreach (var line in File.ReadAllLines(envFile))
                {
                    var t = line.Trim();
                    if (t.StartsWith('#') || !t.Contains('=')) continue;
                    var eq = t.IndexOf('=');
                    if (t[..eq].Trim() == key) return t[(eq + 1)..].Trim().Trim('"');
                }
            }
        }
        return _configuration[key];
    }

    private static string GetModelName(string provider) => provider switch
    {
        "lmstudio"    => "google/gemma-4-e4b",
        "gemini"      => "gemini-2.0-flash",
        "gemini_cli"  => "antigravity-cli",
        "antigravity" => "antigravity-cli",
        "ollama"      => "llava:13b",
        "anthropic"   => "claude-haiku-4-5-20251001",
        _             => provider
    };

    private static async Task FailRow(
        IDbConnection db, IDbTransaction tx, QueueRow row, AnnotationJobConfig cfg, string error)
    {
        await db.ExecuteAsync($"""
            UPDATE {cfg.QueueTable} SET
                status = CASE WHEN retry_count >= 3 THEN 'failed' ELSE 'queued' END,
                retry_count   = retry_count + 1,
                error_message = @Err,
                completed_at  = @Now
            WHERE queue_id = @Id
            """,
            new { Err = error[..Math.Min(error.Length, 500)], Now = DateTime.UtcNow, Id = row.queue_id },
            transaction: tx);
    }

    private class QueueRow
    {
        public int    queue_id  { get; set; }
        public string pk_value  { get; set; } = "";
        public string file_path { get; set; } = "";
        public string provider  { get; set; } = "";
        public int    retry_count { get; set; }
    }

    internal class AnnotationResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("caption_short")]
        public string? CaptionShort   { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("caption_long")]
        public string? CaptionLong    { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("scene_type")]
        public string? SceneType      { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("subjects")]
        public string? Subjects       { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("activities")]
        public string? Activities     { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("tags")]
        public string[]? Tags         { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("person_count")]
        public int PersonCount        { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("confidence_score")]
        public double ConfidenceScore { get; set; }
    }
}
