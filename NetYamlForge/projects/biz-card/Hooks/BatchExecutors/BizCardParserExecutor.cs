// DCS001 抑制理由: テーブル名・カラム名は設定値のみを使用する動的SQL生成ユーティリティです。
#pragma warning disable DCS001
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.BatchJob;

namespace NetYamlForge.Projects.BizCard.Hooks;

public class ImportJobRow
{
    public int    id        { get; set; }
    public string file_name { get; set; } = "";
    public string file_path { get; set; } = "";
    public string file_type { get; set; } = "";
}

/// <summary>
/// Biz-card business card OCR/parse executor.
/// </summary>
public class BizCardParserExecutor : AiQueueStepHandlerBase<ImportJobRow>
{
    public override string StepType => "biz_card_parser";

    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly IEmbeddingService _embedding;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

    private const string BizCardPrompt = """
        あなたは名刺OCR専門AIです。添付の名刺画像を解析し、以下のJSON形式のみで出力してください（説明・Markdownコードブロック不要）。
        読み取れない項目はnullを設定し、メールアドレス・電話番号は正確に抽出してください。
        {
          "last_name": "姓",
          "first_name": "名",
          "last_name_kana": "セイ（ひらがな/カタカナ）またはnull",
          "first_name_kana": "メイ（ひらがな/カタカナ）またはnull",
          "company_name": "会社名",
          "company_name_kana": "カイシャメイカナまたはnull",
          "title": "役職名またはnull",
          "department": "部署名またはnull",
          "email": "メールアドレス（1件目）またはnull",
          "email2": "メールアドレス（2件目）またはnull",
          "phone": "電話番号（固定）またはnull",
          "mobile": "携帯電話番号またはnull",
          "fax": "FAX番号またはnull",
          "postal_code": "郵便番号（ハイフン付き）またはnull",
          "address": "住所またはnull",
          "website": "WebサイトURLまたはnull",
          "industry": "業界カテゴリ（IT/製造/金融/医療/不動産/小売/サービス/教育/その他）またはnull",
          "tags": ["タグ1", "タグ2"],
          "notes": null
        }
        """;

    public BizCardParserExecutor(
        IWebHostEnvironment env,
        IConfiguration configuration,
        IEmbeddingService embedding,
        ICliChainService cliChain,
        ILogger<BizCardParserExecutor> logger) : base(cliChain, logger)
    {
        _env = env;
        _configuration = configuration;
        _embedding = embedding;
        Logger = logger;
    }

    protected override async Task<IReadOnlyList<ImportJobRow>> FetchPendingAsync(
        BatchJobDefinition job, string? projectName, IDbConnection db, IDbTransaction tx,
        int batchSize, CancellationToken ct)
    {
        var cfg = job.BizCardParseConfig ?? new BizCardParseConfig();
        var rows = (await db.QueryAsync<ImportJobRow>(
            $"""
            SELECT id, file_name, file_path, file_type
            FROM {cfg.ImportTable}
            WHERE status = 'pending'
            ORDER BY id ASC
            LIMIT @Batch
            """,
            new { Batch = batchSize },
            transaction: tx)).ToList();
        return rows;
    }

    protected override async Task MarkProcessingAsync(ImportJobRow row, BatchJobDefinition job, IDbConnection db, IDbTransaction tx)
    {
        var cfg = job.BizCardParseConfig ?? new BizCardParseConfig();
        await db.ExecuteAsync(
            $"UPDATE {cfg.ImportTable} SET status='processing' WHERE id=@Id",
            new { Id = row.id }, transaction: tx);
    }

    protected override async Task<RowOutcome> ProcessRowAsync(
        ImportJobRow row, BatchJobDefinition job, string? projectName, IDbConnection db, IDbTransaction tx,
        CancellationToken ct)
    {
        var cfg = job.BizCardParseConfig ?? new BizCardParseConfig();
        var provider = job.AiProvider ?? "antigravity";
        var absolutePath = ResolveFilePath(row.file_path);

        if (!File.Exists(absolutePath))
        {
            return RowOutcome.Fail($"File not found: {absolutePath}");
        }

        var parsed = await ParseCardAsync(absolutePath, provider, projectName, ct);
        if (parsed == null)
        {
            return RowOutcome.Fail("AI returned empty or unparseable response");
        }

        var now = DateTime.UtcNow;
        var cardNo = $"BC-{now:yyyyMMddHHmmss}-{row.id}";
        var cardId = await InsertBusinessCard(db, tx, row, cfg, parsed, cardNo, now);

        if (cfg.AutoEmbed)
        {
            await TryInlineEmbed(db, tx, cardId.ToString(), cfg, parsed, ct);
        }

        return RowOutcome.Ok(cardId);
    }

    protected override async Task WriteOutcomeAsync(ImportJobRow row, RowOutcome outcome, BatchJobDefinition job, IDbConnection db, IDbTransaction tx)
    {
        var cfg = job.BizCardParseConfig ?? new BizCardParseConfig();
        var now = DateTime.UtcNow;

        if (outcome.Status == RowStatus.Ok)
        {
            var cardId = (long)outcome.Payload!;
            await db.ExecuteAsync(
                $"UPDATE {cfg.ImportTable} SET status='done', result_card_id=@CardId, completed_at=@Now WHERE id=@Id",
                new { CardId = cardId, Now = now.ToString("o"), Id = row.id }, transaction: tx);
            
            Logger.LogInformation("BizCard parsed: import_job/{Id} → {CardsTable}/{CardId}",
                row.id, cfg.CardsTable, cardId);
        }
        else
        {
            var error = outcome.Reason ?? "Unknown error";
            await db.ExecuteAsync(
                $"UPDATE {cfg.ImportTable} SET status='failed', error_message=@Err WHERE id=@Id",
                new { Err = error[..Math.Min(error.Length, 500)], Id = row.id },
                transaction: tx);
        }
    }

    private static async Task<long> InsertBusinessCard(
        IDbConnection db, IDbTransaction tx,
        ImportJobRow importRow, BizCardParseConfig cfg,
        BizCardResult parsed, string cardNo, DateTime now)
    {
        await db.ExecuteAsync(
            $"""
            INSERT INTO {cfg.CardsTable} (
                card_no, last_name, first_name, last_name_kana, first_name_kana,
                title, department, company_name, company_name_kana, industry,
                email, email2, phone, mobile, fax,
                postal_code, address, website,
                file_path, source_type, parse_status, embedding_status,
                notes, tags, received_at, created_at, updated_at
            ) VALUES (
                @CardNo, @LastName, @FirstName, @LastNameKana, @FirstNameKana,
                @Title, @Department, @CompanyName, @CompanyNameKana, @Industry,
                @Email, @Email2, @Phone, @Mobile, @Fax,
                @PostalCode, @Address, @Website,
                @FilePath, 'import', 'done', 'pending',
                @Notes, @Tags, @Now, @Now, @Now
            )
            """,
            new
            {
                CardNo          = cardNo,
                LastName        = parsed.LastName,
                FirstName       = parsed.FirstName,
                LastNameKana    = parsed.LastNameKana,
                FirstNameKana   = parsed.FirstNameKana,
                Title           = parsed.Title,
                Department      = parsed.Department,
                CompanyName     = parsed.CompanyName,
                CompanyNameKana = parsed.CompanyNameKana,
                Industry        = parsed.Industry,
                Email           = parsed.Email,
                Email2          = parsed.Email2,
                Phone           = parsed.Phone,
                Mobile          = parsed.Mobile,
                Fax             = parsed.Fax,
                PostalCode      = parsed.PostalCode,
                Address         = parsed.Address,
                Website         = parsed.Website,
                FilePath        = importRow.file_path,
                Notes           = parsed.Notes,
                Tags            = parsed.Tags != null ? string.Join(",", parsed.Tags) : null,
                Now             = now.ToString("o"),
            },
            transaction: tx);

        return await db.QueryFirstAsync<long>("SELECT last_insert_rowid()", transaction: tx);
    }

    private async Task TryInlineEmbed(
        IDbConnection db, IDbTransaction tx,
        string cardId, BizCardParseConfig cfg,
        BizCardResult parsed, CancellationToken ct)
    {
        try
        {
            await db.ExecuteAsync(
                $"""
                CREATE TABLE IF NOT EXISTS {cfg.EmbeddingTable} (
                    id         TEXT NOT NULL PRIMARY KEY,
                    embedding  TEXT NOT NULL,
                    created_at TEXT NOT NULL
                )
                """, transaction: tx);

            var text = BuildEmbedText(parsed);
            if (string.IsNullOrWhiteSpace(text)) return;

            var vecs = await _embedding.EmbedBatchAsync([text], ct);
            var vec = vecs.Count > 0 ? vecs[0] : null;
            if (vec == null || vec.Length == 0) return;

            await db.ExecuteAsync(
                $"""
                INSERT OR REPLACE INTO {cfg.EmbeddingTable}(id, embedding, created_at)
                VALUES (@Id, @Embedding, @Now)
                """,
                new { Id = cardId, Embedding = JsonSerializer.Serialize(vec), Now = DateTime.UtcNow.ToString("o") },
                transaction: tx);

            await db.ExecuteAsync(
                $"UPDATE {cfg.CardsTable} SET embedding_status='done', updated_at=@Now WHERE id=@Id",
                new { Now = DateTime.UtcNow.ToString("o"), Id = cardId }, transaction: tx);

            Logger.LogInformation("Inline embedding generated for card/{Id} ({Dims} dims)", cardId, vec.Length);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Inline embedding failed for card/{Id}, will retry via cron", cardId);
        }
    }

    private static string BuildEmbedText(BizCardResult p)
    {
        var parts = new List<string>();
        var fullName = string.Join(" ", new[] { p.LastName, p.FirstName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(fullName))      parts.Add(fullName);
        if (!string.IsNullOrWhiteSpace(p.CompanyName)) parts.Add($"会社: {p.CompanyName}");
        if (!string.IsNullOrWhiteSpace(p.Title))       parts.Add($"役職: {p.Title}");
        if (!string.IsNullOrWhiteSpace(p.Department))  parts.Add($"部署: {p.Department}");
        if (!string.IsNullOrWhiteSpace(p.Email))       parts.Add($"メール: {p.Email}");
        if (!string.IsNullOrWhiteSpace(p.Industry))    parts.Add($"業界: {p.Industry}");
        if (!string.IsNullOrWhiteSpace(p.Address))     parts.Add($"住所: {p.Address}");
        return string.Join(". ", parts);
    }

    private async Task<BizCardResult?> ParseCardAsync(
        string absolutePath, string provider, string? projectName, CancellationToken ct)
        => provider.ToLowerInvariant() switch
        {
            "lmstudio"    => await ParseWithLmStudioAsync(absolutePath, ct),
            "gemini_cli"  => await ParseWithAntigravityCliAsync(absolutePath, projectName, ct),
            "gemini"      => await ParseWithGeminiAsync(absolutePath, projectName, ct),
            "ollama"      => await ParseWithOllamaAsync(absolutePath, projectName, ct),
            "antigravity" => await ParseWithAntigravityCliAsync(absolutePath, projectName, ct),
            "anthropic"   => await ParseWithAnthropicAsync(absolutePath, projectName, ct),
            _ => LogAndNull(provider)
        };

    private BizCardResult? LogAndNull(string provider)
    {
        Logger.LogWarning("Unknown biz-card parse provider '{Provider}'", provider);
        return null;
    }

    private async Task<BizCardResult?> ParseWithAntigravityCliAsync(string absolutePath, string? projectName, CancellationToken ct)
    {
        var result = await Cli.PromptAsync(BizCardPrompt, imagePath: absolutePath, projectName: projectName, cancellationToken: ct);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Text))
        {
            return null;
        }
        return ParseJson(result.Text);
    }

    private async Task<BizCardResult?> ParseWithLmStudioAsync(string absolutePath, CancellationToken ct)
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
                        new { type = "text",      text = BizCardPrompt },
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{base64}" } }
                    }
                }
            },
            max_tokens = 1024,
            temperature = 0.1
        };

        var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        var resp = await _http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { Logger.LogError("LM Studio {Status}: {Body}", resp.StatusCode, json); return null; }

        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        return ParseJson(text);
    }

    private async Task<BizCardResult?> ParseWithGeminiAsync(string absolutePath, string? projectName, CancellationToken ct)
    {
        var apiKey = GetEnvValue("GEMINI_API_KEY", projectName);
        if (string.IsNullOrWhiteSpace(apiKey)) { Logger.LogWarning("GEMINI_API_KEY not set"); return null; }

        var (base64, mimeType) = EncodeImage(absolutePath);
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}";

        var body = new
        {
            contents = new[]
            {
                new { parts = new object[] { new { text = BizCardPrompt }, new { inline_data = new { mime_type = mimeType, data = base64 } } } }
            },
            generationConfig = new { maxOutputTokens = 1024, temperature = 0.1 }
        };

        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        var resp = await _http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { Logger.LogError("Gemini {Status}: {Body}", resp.StatusCode, json); return null; }

        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
        return ParseJson(text);
    }

    private async Task<BizCardResult?> ParseWithOllamaAsync(string absolutePath, string? projectName, CancellationToken ct)
    {
        var baseUrl = GetEnvValue("OLLAMA_BASE_URL", projectName) ?? "http://localhost:11434";
        var model   = GetEnvValue("OLLAMA_VISION_MODEL", projectName) ?? "llava:13b";
        var (base64, _) = EncodeImage(absolutePath);

        var body = new { model, prompt = BizCardPrompt, images = new[] { base64 }, stream = false };
        var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/generate")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        var resp = await _http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { Logger.LogError("Ollama {Status}: {Body}", resp.StatusCode, json); return null; }

        using var doc = JsonDocument.Parse(json);
        return ParseJson(doc.RootElement.GetProperty("response").GetString() ?? "");
    }

    private async Task<BizCardResult?> ParseWithAnthropicAsync(string absolutePath, string? projectName, CancellationToken ct)
    {
        var apiKey = GetEnvValue("ANTHROPIC_API_KEY", projectName);
        if (string.IsNullOrWhiteSpace(apiKey)) { Logger.LogWarning("ANTHROPIC_API_KEY not set"); return null; }

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
                        new { type = "text",   text = BizCardPrompt }
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
        if (!resp.IsSuccessStatusCode) { Logger.LogError("Anthropic {Status}: {Body}", resp.StatusCode, json); return null; }

        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
        return ParseJson(text);
    }

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

    private static BizCardResult? ParseJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var cleaned = Regex.Replace(text, @"```(?:json)?\s*", "", RegexOptions.IgnoreCase).Trim();
            var start = cleaned.IndexOf('{');
            var end   = cleaned.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            return JsonSerializer.Deserialize<BizCardResult>(cleaned[start..(end + 1)],
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
}

internal class BizCardResult
{
    [JsonPropertyName("last_name")]         public string? LastName        { get; set; }
    [JsonPropertyName("first_name")]        public string? FirstName       { get; set; }
    [JsonPropertyName("last_name_kana")]    public string? LastNameKana    { get; set; }
    [JsonPropertyName("first_name_kana")]   public string? FirstNameKana   { get; set; }
    [JsonPropertyName("company_name")]      public string? CompanyName     { get; set; }
    [JsonPropertyName("company_name_kana")] public string? CompanyNameKana { get; set; }
    [JsonPropertyName("title")]             public string? Title           { get; set; }
    [JsonPropertyName("department")]        public string? Department      { get; set; }
    [JsonPropertyName("email")]             public string? Email           { get; set; }
    [JsonPropertyName("email2")]            public string? Email2          { get; set; }
    [JsonPropertyName("phone")]             public string? Phone           { get; set; }
    [JsonPropertyName("mobile")]            public string? Mobile          { get; set; }
    [JsonPropertyName("fax")]               public string? Fax             { get; set; }
    [JsonPropertyName("postal_code")]       public string? PostalCode      { get; set; }
    [JsonPropertyName("address")]           public string? Address         { get; set; }
    [JsonPropertyName("website")]           public string? Website         { get; set; }
    [JsonPropertyName("industry")]          public string? Industry        { get; set; }
    [JsonPropertyName("tags")]              public string[]? Tags          { get; set; }
    [JsonPropertyName("notes")]             public string? Notes           { get; set; }
}
