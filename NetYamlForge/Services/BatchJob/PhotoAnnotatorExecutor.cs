using System.Data;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// processing_queue からフォトを取り出し、Antigravity CLI/Gemini API/Ollama で画像標注を実行する。
/// job type: photo_annotator
/// </summary>
public class PhotoAnnotatorExecutor : IBatchStepHandler
{
    public string StepType => "photo_annotator";

    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly IGeminiEmbeddingService _embedding;
    private readonly ILogger<PhotoAnnotatorExecutor> _logger;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

    private const string AnnotationPrompt = """
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

    public PhotoAnnotatorExecutor(
        IWebHostEnvironment env,
        IConfiguration configuration,
        IGeminiEmbeddingService embedding,
        ILogger<PhotoAnnotatorExecutor> logger)
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
        var batchSize = job.BatchSize > 0 ? job.BatchSize : 5;
        var provider = job.AiProvider ?? "antigravity";

        var rows = (await db.QueryAsync<QueueRow>(
            @"SELECT q.queue_id, q.photo_id, q.file_path, q.provider, q.retry_count
              FROM processing_queue q
              WHERE q.status = 'queued' AND q.provider = @Provider
              ORDER BY q.priority DESC, q.queued_at ASC
              LIMIT @Batch",
            new { Batch = batchSize, Provider = provider },
            transaction: tx)).ToList();

        if (rows.Count == 0)
        {
            result.Success = true;
            result.RowsAffected = 0;
            return;
        }

        var done = 0;
        var failed = 0;

        foreach (var row in rows)
        {
            if (ct.IsCancellationRequested) break;
            var now = DateTime.UtcNow;

            await db.ExecuteAsync(
                "UPDATE processing_queue SET status='processing', started_at=@Now WHERE queue_id=@Id",
                new { Now = now, Id = row.queue_id }, transaction: tx);

            try
            {
                var absolutePath = ResolveFilePath(row.file_path);
                if (!File.Exists(absolutePath))
                {
                    await FailRow(db, tx, row, $"File not found: {absolutePath}");
                    failed++;
                    continue;
                }

                var usedProvider = string.IsNullOrEmpty(row.provider) ? provider : row.provider;
                var annotation = await AnnotateAsync(absolutePath, usedProvider, projectName, ct);

                if (annotation == null)
                {
                    await FailRow(db, tx, row, "AI returned empty or unparseable response");
                    failed++;
                    continue;
                }

                // 写回 photos 表
                await db.ExecuteAsync(@"
                    UPDATE photos SET
                        caption_short      = @CaptionShort,
                        caption_long       = @CaptionLong,
                        scene_type         = @SceneType,
                        subjects           = @Subjects,
                        activities         = @Activities,
                        person_count       = @PersonCount,
                        confidence_score   = @Confidence,
                        annotation_status  = 'done',
                        annotation_model   = @Model,
                        annotation_at      = @Now,
                        updated_at         = @Now
                    WHERE photo_id = @PhotoId",
                    new
                    {
                        CaptionShort = annotation.CaptionShort,
                        CaptionLong  = annotation.CaptionLong,
                        SceneType    = annotation.SceneType,
                        Subjects     = annotation.Subjects,
                        Activities   = annotation.Activities,
                        PersonCount  = annotation.PersonCount,
                        Confidence   = annotation.ConfidenceScore,
                        Model        = GetModelName(usedProvider),
                        Now          = now,
                        PhotoId      = row.photo_id,
                    }, transaction: tx);

                // 写回 tags（简单实现：INSERT OR IGNORE）
                if (annotation.Tags?.Length > 0)
                {
                    foreach (var tag in annotation.Tags.Take(15).Where(t => !string.IsNullOrWhiteSpace(t)))
                    {
                        var tagId = Guid.NewGuid().ToString("N");
                        await db.ExecuteAsync(@"
                            INSERT OR IGNORE INTO tags (tag_id, name, category, created_at)
                            VALUES (@TagId, @Name, 'auto', @Now)",
                            new { TagId = tagId, Name = tag.Trim().ToLowerInvariant(), Now = now },
                            transaction: tx);

                        var resolvedTagId = await db.QueryFirstOrDefaultAsync<string>(
                            "SELECT tag_id FROM tags WHERE name = @Name",
                            new { Name = tag.Trim().ToLowerInvariant() }, transaction: tx);

                        if (resolvedTagId != null)
                        {
                            await db.ExecuteAsync(@"
                                INSERT OR IGNORE INTO photo_tags (photo_id, tag_id, tag_name, confidence)
                                VALUES (@PhotoId, @TagId, @TagName, @Conf)",
                                new { PhotoId = row.photo_id, TagId = resolvedTagId, TagName = tag.Trim().ToLowerInvariant(), Conf = annotation.ConfidenceScore },
                                transaction: tx);
                        }
                    }
                }

                // 标注完成后立即生成嵌入，无需等待 cron
                try
                {
                    await db.ExecuteAsync("""
                        CREATE TABLE IF NOT EXISTS photo_embeddings (
                            photo_id   TEXT NOT NULL PRIMARY KEY,
                            embedding  TEXT NOT NULL,
                            created_at TEXT NOT NULL
                        )
                        """, transaction: tx);

                    var embedText = BuildEmbedText(annotation, null, null, null);
                    if (!string.IsNullOrWhiteSpace(embedText))
                    {
                        var vecs = await _embedding.EmbedBatchAsync([embedText], ct);
                        var vec = vecs.Count > 0 ? vecs[0] : null;
                        if (vec != null && vec.Length > 0)
                        {
                            await db.ExecuteAsync("""
                                INSERT OR REPLACE INTO photo_embeddings(photo_id, embedding, created_at)
                                VALUES (@PhotoId, @Embedding, @Now)
                                """,
                                new { PhotoId = row.photo_id, Embedding = JsonSerializer.Serialize(vec), Now = DateTime.UtcNow.ToString("o") },
                                transaction: tx);
                            _logger.LogInformation("Embedding generated inline for photo {PhotoId} ({Dims} dims)", row.photo_id, vec.Length);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 嵌入失败不影响标注结果，后续 cron 会补跑
                    _logger.LogWarning(ex, "Inline embedding failed for photo {PhotoId}, will retry via cron", row.photo_id);
                }

                var ms = (long)(DateTime.UtcNow - now).TotalMilliseconds;
                await db.ExecuteAsync(@"
                    UPDATE processing_queue SET
                        status='done', completed_at=@Now, processing_ms=@Ms, provider=@Provider
                    WHERE queue_id=@Id",
                    new { Now = DateTime.UtcNow, Ms = ms, Provider = usedProvider, Id = row.queue_id },
                    transaction: tx);

                done++;
                _logger.LogInformation("Photo annotated: {PhotoId} via {Provider}", row.photo_id, usedProvider);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to annotate photo {PhotoId}", row.photo_id);
                await FailRow(db, tx, row, ex.Message);
                failed++;
            }
        }

        result.Success = failed == 0 || done > 0;
        result.RowsAffected = done;
        result.ErrorMessage = failed > 0 ? $"{failed} photo(s) failed annotation" : null;
    }

    // ──────────────────────────────────────────────────────────
    // Embedding helpers
    // ──────────────────────────────────────────────────────────

    private static string BuildEmbedText(AnnotationResult a, string? sceneType, string? gpsAddress, string? ocrText)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(a.CaptionShort)) parts.Add(a.CaptionShort);
        if (!string.IsNullOrWhiteSpace(a.CaptionLong))  parts.Add(a.CaptionLong);
        if (!string.IsNullOrWhiteSpace(a.SceneType ?? sceneType)) parts.Add($"场景: {a.SceneType ?? sceneType}");
        if (!string.IsNullOrWhiteSpace(a.Subjects))     parts.Add($"主体: {a.Subjects}");
        if (!string.IsNullOrWhiteSpace(a.Activities))   parts.Add($"活动: {a.Activities}");
        if (!string.IsNullOrWhiteSpace(gpsAddress))     parts.Add($"地点: {gpsAddress}");
        if (!string.IsNullOrWhiteSpace(ocrText))        parts.Add($"文字: {ocrText}");
        return string.Join(". ", parts);
    }

    // ──────────────────────────────────────────────────────────
    // Provider dispatch
    // ──────────────────────────────────────────────────────────

    private async Task<AnnotationResult?> AnnotateAsync(
        string absolutePath, string provider, string? projectName, CancellationToken ct)
    {
        return provider.ToLowerInvariant() switch
        {
            "lmstudio"     => await AnnotateWithLmStudioAsync(absolutePath, ct),
            "gemini_cli"   => await AnnotateWithAntigravityCliAsync(absolutePath, ct),
            "gemini"       => await AnnotateWithGeminiAsync(absolutePath, projectName, ct),
            "ollama"       => await AnnotateWithOllamaAsync(absolutePath, projectName, ct),
            "antigravity"  => await AnnotateWithAntigravityCliAsync(absolutePath, ct),
            _ => LogAndReturnNull(provider),
        };
    }

    private AnnotationResult? LogAndReturnNull(string provider)
    {
        _logger.LogWarning("Unknown annotation provider '{Provider}'; skipping annotation", provider);
        return null;
    }

    private async Task<AnnotationResult?> AnnotateWithAntigravityCliAsync(
        string absolutePath, CancellationToken ct)
    {
        // @filepath 语法让 antigravity CLI 真正附加图片内容
        var prompt = $"@{absolutePath}\n{AnnotationPrompt}";
        var escapedPrompt = prompt.Replace("\\", "\\\\").Replace("\"", "\\\"");

        var startInfo = new ProcessStartInfo
        {
            FileName = "antigravity",
            Arguments = $"-p \"{escapedPrompt}\" --dangerously-skip-permissions",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var outputSb = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) outputSb.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(180));
        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            _logger.LogError("antigravity CLI timed out during photo annotation");
            return null;
        }

        if (process.ExitCode != 0)
        {
            _logger.LogError("antigravity CLI exited with code {Code}", process.ExitCode);
            return null;
        }

        var output = outputSb.ToString();
        // Strip ANSI escape codes and control sequences
        var cleaned = Regex.Replace(output, @"\x1B\[[0-9;]*[mGKHFJ]", "");
        cleaned = Regex.Replace(cleaned, @"\x1B\[.*?[a-zA-Z]", "");
        return ParseAnnotationJson(cleaned);
    }

    private async Task<AnnotationResult?> AnnotateWithLmStudioAsync(
        string absolutePath, CancellationToken ct)
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
                        new { type = "text", text = AnnotationPrompt },
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
        var respJson = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("LM Studio API error {Status}: {Body}", resp.StatusCode, respJson);
            return null;
        }

        using var doc = JsonDocument.Parse(respJson);
        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content").GetString() ?? "";

        return ParseAnnotationJson(text);
    }

    private async Task<AnnotationResult?> AnnotateWithAnthropicAsync(
        string absolutePath, string? projectName, CancellationToken ct)
    {
        var apiKey = GetEnvValue("ANTHROPIC_API_KEY", projectName);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("ANTHROPIC_API_KEY not set; skipping Anthropic annotation");
            return null;
        }

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
                        new { type = "image", source = new { type = "base64", media_type = mediaType, data = base64 } },
                        new { type = "text",  text = AnnotationPrompt }
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
        var respJson = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Anthropic API error {Status}: {Body}", resp.StatusCode, respJson);
            return null;
        }

        using var doc = JsonDocument.Parse(respJson);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text").GetString() ?? "";

        return ParseAnnotationJson(text);
    }

    private async Task<AnnotationResult?> AnnotateWithGeminiAsync(
        string absolutePath, string? projectName, CancellationToken ct)
    {
        var apiKey = GetEnvValue("GEMINI_API_KEY", projectName);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("GEMINI_API_KEY not set; skipping Gemini annotation");
            return null;
        }

        var (base64, mimeType) = EncodeImage(absolutePath);
        var model = "gemini-2.0-flash";
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

        var body = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = AnnotationPrompt },
                        new { inline_data = new { mime_type = mimeType, data = base64 } }
                    }
                }
            },
            generationConfig = new { maxOutputTokens = 1024, temperature = 0.2 }
        };

        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

        var resp = await _http.SendAsync(req, ct);
        var respJson = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Gemini API error {Status}: {Body}", resp.StatusCode, respJson);
            return null;
        }

        using var doc = JsonDocument.Parse(respJson);
        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text").GetString() ?? "";

        return ParseAnnotationJson(text);
    }

    private async Task<AnnotationResult?> AnnotateWithOllamaAsync(
        string absolutePath, string? projectName, CancellationToken ct)
    {
        var baseUrl = GetEnvValue("OLLAMA_BASE_URL", projectName) ?? "http://localhost:11434";
        var model   = GetEnvValue("OLLAMA_VISION_MODEL", projectName) ?? "llava:13b";
        var (base64, _) = EncodeImage(absolutePath);

        var body = new
        {
            model,
            prompt = AnnotationPrompt,
            images = new[] { base64 },
            stream = false
        };

        var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/generate")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

        var resp = await _http.SendAsync(req, ct);
        var respJson = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Ollama API error {Status}: {Body}", resp.StatusCode, respJson);
            return null;
        }

        using var doc = JsonDocument.Parse(respJson);
        var text = doc.RootElement.GetProperty("response").GetString() ?? "";
        return ParseAnnotationJson(text);
    }

    // ──────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────

    private string ResolveFilePath(string storedPath)
    {
        if (File.Exists(storedPath)) return storedPath;
        // URL path → wwwroot relative
        var relative = storedPath.TrimStart('/');
        return Path.Combine(_env.WebRootPath, relative);
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
            var json = cleaned[start..(end + 1)];
            return JsonSerializer.Deserialize<AnnotationResult>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
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
                    if (t[..eq].Trim() == key)
                        return t[(eq + 1)..].Trim().Trim('"');
                }
            }
        }
        return _configuration[key];
    }

    private static string GetModelName(string provider) => provider switch
    {
        "lmstudio"     => "google/gemma-4-e4b",
        "gemini"       => "gemini-2.0-flash",
        "gemini_cli"   => "antigravity-cli",
        "antigravity"  => "antigravity-cli",
        "ollama"       => "llava:13b",
        _              => "claude-haiku-4-5-20251001"
    };

    /// <summary>
    /// 直接标注单张照片，不经过 processing_queue，适合「立即触发」场景。
    /// </summary>
    public async Task<(bool Success, string? Error)> AnnotateSingleAsync(
        string photoId, string projectName, IDbConnection db, IDbTransaction? tx, CancellationToken ct)
    {
        var row = await db.QueryFirstOrDefaultAsync<QueueRow>(
            "SELECT photo_id, file_path FROM photos WHERE photo_id = @Id",
            new { Id = photoId }, transaction: tx);

        if (row == null) return (false, "照片不存在");

        var provider = "antigravity";
        try
        {
            provider = await db.QueryFirstOrDefaultAsync<string>(
                "SELECT COALESCE(value, 'antigravity') FROM project_settings WHERE setting_key = 'annotation_provider' ORDER BY id DESC LIMIT 1",
                transaction: tx) ?? "antigravity";
        }
        catch { }

        var absolutePath = ResolveFilePath(row.file_path);
        if (!File.Exists(absolutePath))
            return (false, $"文件不存在: {absolutePath}");

        var now = DateTime.UtcNow;

        await db.ExecuteAsync(
            "UPDATE photos SET annotation_status = 'processing', updated_at = @Now WHERE photo_id = @Id",
            new { Now = now, Id = photoId }, transaction: tx);

        AnnotationResult? annotation;
        try
        {
            annotation = await AnnotateAsync(absolutePath, provider, projectName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AnnotateSingle failed for photo {PhotoId}", photoId);
            await db.ExecuteAsync(
                "UPDATE photos SET annotation_status = 'failed', updated_at = @Now WHERE photo_id = @Id",
                new { Now = now, Id = photoId }, transaction: tx);
            return (false, ex.Message);
        }

        if (annotation == null)
        {
            await db.ExecuteAsync(
                "UPDATE photos SET annotation_status = 'failed', updated_at = @Now WHERE photo_id = @Id",
                new { Now = now, Id = photoId }, transaction: tx);
            return (false, "AI 返回空结果");
        }

        await db.ExecuteAsync(@"
            UPDATE photos SET
                caption_short      = @CaptionShort,
                caption_long       = @CaptionLong,
                scene_type         = @SceneType,
                subjects           = @Subjects,
                activities         = @Activities,
                person_count       = @PersonCount,
                confidence_score   = @Confidence,
                annotation_status  = 'done',
                annotation_model   = @Model,
                annotation_at      = @Now,
                updated_at         = @Now
            WHERE photo_id = @PhotoId",
            new
            {
                CaptionShort = annotation.CaptionShort,
                CaptionLong  = annotation.CaptionLong,
                SceneType    = annotation.SceneType,
                Subjects     = annotation.Subjects,
                Activities   = annotation.Activities,
                PersonCount  = annotation.PersonCount,
                Confidence   = annotation.ConfidenceScore,
                Model        = GetModelName(provider),
                Now          = now,
                PhotoId      = photoId
            }, transaction: tx);

        if (annotation.Tags?.Length > 0)
        {
            foreach (var tag in annotation.Tags.Take(15).Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                var tagId = Guid.NewGuid().ToString("N");
                await db.ExecuteAsync(@"
                    INSERT OR IGNORE INTO tags (tag_id, name, category, created_at)
                    VALUES (@TagId, @Name, 'auto', @Now)",
                    new { TagId = tagId, Name = tag.Trim().ToLowerInvariant(), Now = now }, transaction: tx);

                var resolvedTagId = await db.QueryFirstOrDefaultAsync<string>(
                    "SELECT tag_id FROM tags WHERE name = @Name",
                    new { Name = tag.Trim().ToLowerInvariant() }, transaction: tx);

                if (resolvedTagId != null)
                {
                    await db.ExecuteAsync(@"
                        INSERT OR IGNORE INTO photo_tags (photo_id, tag_id, tag_name, confidence)
                        VALUES (@PhotoId, @TagId, @TagName, @Conf)",
                        new { PhotoId = photoId, TagId = resolvedTagId, TagName = tag.Trim().ToLowerInvariant(), Conf = annotation.ConfidenceScore },
                        transaction: tx);
                }
            }
        }

        try
        {
            await db.ExecuteAsync("""
                CREATE TABLE IF NOT EXISTS photo_embeddings (
                    photo_id   TEXT NOT NULL PRIMARY KEY,
                    embedding  TEXT NOT NULL,
                    created_at TEXT NOT NULL
                )
                """, transaction: tx);

            var embedText = BuildEmbedText(annotation, null, null, null);
            if (!string.IsNullOrWhiteSpace(embedText))
            {
                var vecs = await _embedding.EmbedBatchAsync([embedText], ct);
                var vec = vecs.Count > 0 ? vecs[0] : null;
                if (vec != null && vec.Length > 0)
                {
                    await db.ExecuteAsync("""
                        INSERT OR REPLACE INTO photo_embeddings(photo_id, embedding, created_at)
                        VALUES (@PhotoId, @Embedding, @Now)
                        """,
                        new { PhotoId = photoId, Embedding = JsonSerializer.Serialize(vec), Now = DateTime.UtcNow.ToString("o") },
                        transaction: tx);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inline embedding failed for photo {PhotoId} during AnnotateSingle", photoId);
        }

        _logger.LogInformation("Photo annotated (immediate): {PhotoId} via {Provider}", photoId, provider);
        return (true, null);
    }

    private static async Task FailRow(IDbConnection db, IDbTransaction tx, QueueRow row, string error)
    {
        await db.ExecuteAsync(@"
            UPDATE processing_queue SET
                status = CASE WHEN retry_count >= 3 THEN 'failed' ELSE 'queued' END,
                retry_count  = retry_count + 1,
                error_message = @Err,
                completed_at  = @Now
            WHERE queue_id = @Id",
            new { Err = error[..Math.Min(error.Length, 500)], Now = DateTime.UtcNow, Id = row.queue_id },
            transaction: tx);
    }

    private class QueueRow
    {
        public int queue_id { get; set; }
        public string photo_id  { get; set; } = "";
        public string file_path { get; set; } = "";
        public string provider  { get; set; } = "";
        public int retry_count  { get; set; }
    }

    private class AnnotationResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("caption_short")]
        public string? CaptionShort    { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("caption_long")]
        public string? CaptionLong     { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("scene_type")]
        public string? SceneType       { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("subjects")]
        public string? Subjects        { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("activities")]
        public string? Activities      { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("tags")]
        public string[]? Tags          { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("person_count")]
        public int PersonCount         { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("confidence_score")]
        public double ConfidenceScore  { get; set; }
    }
}
