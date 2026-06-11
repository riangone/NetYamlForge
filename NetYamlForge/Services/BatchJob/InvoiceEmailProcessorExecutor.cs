using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MimeKit;
using NetYamlForge.Services;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// メールから請求情報を抽出し、DB保存・PDF生成・返信送信を行うジョブ実行器。
/// </summary>
public class InvoiceEmailProcessorExecutor : IBatchStepHandler
{
    public string StepType => "invoice_email_processor";
    private readonly IDocumentPdfService _docPdf;
    private readonly IAntigravityCliService _antigravityCli;
    private readonly ILogger<InvoiceEmailProcessorExecutor> _logger;

    public InvoiceEmailProcessorExecutor(IDocumentPdfService docPdf, IAntigravityCliService antigravityCli, ILogger<InvoiceEmailProcessorExecutor> logger)
    {
        _docPdf = docPdf;
        _antigravityCli = antigravityCli;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx,
        BatchJobResult result, CancellationToken ct)
    {
        var r = await ExecuteAsync(job, projectName ?? "", db, ct);
        result.Success = r.Success;
        result.RowsAffected = r.RowsAffected;
        result.ErrorMessage = r.ErrorMessage;
        result.ErrorDetail = r.ErrorDetail;
    }

    public async Task<BatchJobResult> ExecuteAsync(
        BatchJobDefinition job,
        string projectName,
        IDbConnection db,
        CancellationToken cancellationToken = default)
    {
        var result = new BatchJobResult { JobId = job.Id, StartedAt = DateTime.UtcNow };

        try
        {
            _logger.LogInformation("InvoiceEmailProcessor 開始: {JobId} (Project: {Project})", job.Id, projectName);

            var projectDir = Path.Combine(Directory.GetCurrentDirectory(), "projects", projectName);
            var env = LoadEnv(Path.Combine(projectDir, ".env"));

            var imapServer  = env.GetValueOrDefault("IMAP_SERVER");
            var imapPort    = int.Parse(env.GetValueOrDefault("IMAP_PORT", "993"));
            var imapSsl     = bool.Parse(env.GetValueOrDefault("IMAP_SSL", "true"));
            var imapUser    = env.GetValueOrDefault("IMAP_USER");
            var imapPass    = env.GetValueOrDefault("IMAP_PASSWORD");

            var smtpServer  = env.GetValueOrDefault("SMTP_SERVER");
            var smtpPort    = int.Parse(env.GetValueOrDefault("SMTP_PORT", "587"));
            var smtpSsl     = bool.Parse(env.GetValueOrDefault("SMTP_SSL", "false"));
            var smtpUser    = env.GetValueOrDefault("SMTP_USER");
            var smtpPass    = env.GetValueOrDefault("SMTP_PASSWORD");
            var fromName    = env.GetValueOrDefault("SMTP_FROM_NAME", "請求処理システム");
            var fromAddress = env.GetValueOrDefault("SMTP_FROM_ADDRESS") ?? smtpUser;
            var aiModel     = env.GetValueOrDefault("AI_MODEL", "");
            var targetSender = env.GetValueOrDefault("TARGET_SENDER_EMAIL");
            var invoiceKeywordsRaw = env.GetValueOrDefault("INVOICE_SUBJECT_KEYWORDS", "");
            var invoiceKeywords = invoiceKeywordsRaw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();

            using var imapClient = new ImapClient();
            await imapClient.ConnectAsync(imapServer, imapPort, imapSsl, cancellationToken);
            await imapClient.AuthenticateAsync(imapUser, imapPass, cancellationToken);

            var inbox = imapClient.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var uids = await inbox.SearchAsync(SearchQuery.NotSeen, cancellationToken);
            var processed = 0;

            foreach (var uid in uids.OrderBy(x => x.Id).Take(10))
            {
                var message = await inbox.GetMessageAsync(uid, cancellationToken);
                var senderEmail = message.From.Mailboxes.FirstOrDefault()?.Address ?? "";

                if (!string.IsNullOrEmpty(targetSender))
                {
                    var allowed = targetSender.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (!allowed.Any(s => s.Equals(senderEmail, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogInformation("スキップ（送信者不一致）: {Sender}", senderEmail);
                        continue;
                    }
                }

                // INVOICE_SUBJECT_KEYWORDS が設定されている場合、件名が一致するメールのみ処理
                if (invoiceKeywords.Length > 0 && !invoiceKeywords.Any(kw =>
                    message.Subject?.Contains(kw, StringComparison.OrdinalIgnoreCase) == true))
                {
                    _logger.LogInformation("スキップ（請求キーワード不一致）: {Subject}", message.Subject);
                    continue;
                }

                var body = message.TextBody ?? message.HtmlBody ?? "";
                _logger.LogInformation("請求メール処理中: {Subject} from {Sender}", message.Subject, senderEmail);

                // AI で請求データをJSON抽出
                var extracted = await ExtractInvoicesFromEmailAsync(body, aiModel, projectName, cancellationToken);
                if (extracted == null || extracted.Invoices == null || extracted.Invoices.Count == 0)
                {
                    _logger.LogWarning("請求情報を抽出できませんでした: {Subject}", message.Subject);
                    continue;
                }

                // DB保存・PDF生成
                var attachments = new List<(string Filename, byte[] Bytes)>();

                foreach (var inv in extracted.Invoices)
                {
                    try
                    {
                        var dbResult = await SaveInvoiceToDbAsync(db, inv);
                        var invoiceId = dbResult.InvoiceId;
                        var invoiceNo = dbResult.InvoiceNo;
                        _logger.LogInformation("請求書保存完了: {InvoiceNo} (Id={Id})", invoiceNo, invoiceId);

                        var pdfBytes = await GenerateInvoicePdfAsync(db, invoiceId, projectDir);
                        if (pdfBytes != null)
                        {
                            attachments.Add(($"請求書_{invoiceNo}.pdf", pdfBytes));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "請求书処理エラー: {Client}", inv.ClientName);
                    }
                }

                // 返信メール送信（PDFを添付）— 送信元アドレスにのみ返信
                if (attachments.Count > 0 && !string.IsNullOrEmpty(senderEmail))
                {
                    var recipients = new List<string> { senderEmail };

                    await SendReplyWithAttachmentsAsync(
                        smtpServer!, smtpPort, smtpSsl, smtpUser!, smtpPass!,
                        fromName, fromAddress!, message, recipients, attachments, extracted.Invoices, cancellationToken);
                }

                await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, cancellationToken);
                processed++;
            }

            await imapClient.DisconnectAsync(true, cancellationToken);

            result.Success = true;
            result.RowsAffected = processed;
            result.EndedAt = DateTime.UtcNow;

            _logger.LogInformation("InvoiceEmailProcessor 完了: {JobId}, 処理数: {Count}", job.Id, processed);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InvoiceEmailProcessor エラー: {JobId}", job.Id);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.EndedAt = DateTime.UtcNow;
            return result;
        }
    }

    // ─── AI 抽出 ────────────────────────────────────────────────────────────────

    private async Task<ExtractedInvoiceContainer?> ExtractInvoicesFromEmailAsync(
        string emailBody, string aiModel, string projectName, CancellationToken cancellationToken)
    {
        var systemPrompt = """
            あなたは日本語メールから請求情報を抽出するAIです。
            メール本文を解析し、請求書データをJSON形式で返してください。
            必ずJSONのみ返してください（マークダウン・説明文不要）。

            返却するJSONの構造:
            {
              "invoices": [
                {
                  "client_name": "宛先・請求先の名前（個人名・会社名）",
                  "items": [
                    {
                      "item_name": "品名・サービス名",
                      "order_no": "受注番号（なければ空文字）",
                      "description": "内容・説明",
                      "quantity": 数量（整数）,
                      "unit_price": 単価（数値）,
                      "amount": 金額（quantity × unit_price）,
                      "notes": "備考（なければ空文字）"
                    }
                  ],
                  "subtotal": 小計（合計前の税抜き金額）,
                  "tax_rate": 10,
                  "tax_amount": 消費税額,
                  "total": 合計金額（税込）,
                  "due_date": "支払期限 YYYY-MM-DD（不明ならnull）",
                  "remarks": "備考・特記事項"
                }
              ],
              "extraction_notes": "抽出できなかった情報のメモ"
            }

            ルール:
            - 金額・小計・合計の計算が正しいか、必ずセルフチェックしてください
            - 金額(amount) = 数量(quantity) × 単価(unit_price) です
            - 小計(subtotal) = すべての明細の金額(amount)の合計 です
            - 消費税(tax_amount) = 小計 × 0.10 です（小数点以下四捨五入）
            - 合計(total) = 小計 + 消費税 です
            - 金額が明示されていない場合は上記ルールで計算してください
            - 複数の請求先が記載されていれば複数のinvoiceを返す
            - 情報が不足している請求先もできる限り含める
            """;

        var prompt = $"{systemPrompt}\n\nメール本文:\n{emailBody}";

        var extracted = await _antigravityCli.PromptJsonAsync<ExtractedInvoiceContainer>(prompt, aiModel, projectName, cancellationToken);
        
        if (extracted?.Invoices != null)
        {
            foreach (var inv in extracted.Invoices)
            {
                // 合計・小計の検証と補正
                foreach (var item in inv.Items)
                {
                    var calculatedAmount = item.Quantity * item.UnitPrice;
                    if (calculatedAmount != 0 && item.Amount != calculatedAmount)
                    {
                        inv.VerificationNotes.Add($"[検証] 明細 '{item.ItemName}' の金額を補正しました: {item.Amount:N0} -> {calculatedAmount:N0}");
                        item.Amount = calculatedAmount;
                    }
                }

                var calcSubtotal = inv.Items.Sum(i => i.Amount);
                if (inv.Subtotal != calcSubtotal)
                {
                    inv.VerificationNotes.Add($"[検証] 小計を補正しました: {inv.Subtotal:N0} -> {calcSubtotal:N0}");
                    inv.Subtotal = calcSubtotal;
                }

                var calcTax = Math.Round(inv.Subtotal * 0.10m, 0);
                if (inv.TaxAmount != calcTax)
                {
                    inv.VerificationNotes.Add($"[検証] 消費税(10%)を補正しました: {inv.TaxAmount:N0} -> {calcTax:N0}");
                    inv.TaxAmount = calcTax;
                }

                var calcTotal = inv.Subtotal + inv.TaxAmount;
                if (inv.Total != calcTotal)
                {
                    inv.VerificationNotes.Add($"[検証] 合計金額を補正しました: {inv.Total:N0} -> {calcTotal:N0}");
                    inv.Total = calcTotal;
                }
            }
        }

        return extracted;
    }

    // ─── DB 保存 ─────────────────────────────────────────────────────────────

    private async Task<(int InvoiceId, string InvoiceNo)> SaveInvoiceToDbAsync(
        IDbConnection db, ExtractedInvoice inv)
    {
        // 取引先を名前で検索 or 新規作成
        var clientId = await db.QueryFirstOrDefaultAsync<int?>(
            "SELECT Id FROM GoseiClient WHERE Name = @Name LIMIT 1",
            new { Name = inv.ClientName });

        if (clientId == null)
        {
            clientId = await db.QuerySingleAsync<int>(
                "INSERT INTO GoseiClient (Name, CreatedAt, UpdatedAt) VALUES (@Name, @Now, @Now); SELECT last_insert_rowid()",
                new { Name = inv.ClientName, Now = DateTime.UtcNow });
            _logger.LogInformation("取引先新規作成: {Name} (Id={Id})", inv.ClientName, clientId);
        }

        // 請求書番号を自動採番（AI-YYYYMM-XXXX 形式）
        var prefix = $"AI-{DateTime.UtcNow:yyyyMM}-";
        var lastNo = await db.QueryFirstOrDefaultAsync<string>(
            "SELECT InvoiceNo FROM GoseiInvoice WHERE InvoiceNo LIKE @Prefix ORDER BY InvoiceNo DESC LIMIT 1",
            new { Prefix = prefix + "%" });

        var seq = 1;
        if (lastNo != null && int.TryParse(lastNo[prefix.Length..], out var lastSeq))
            seq = lastSeq + 1;

        var invoiceNo = $"{prefix}{seq:D4}";
        var issueDate = DateTime.UtcNow.Date;
        var dueDate = inv.DueDate != null ? DateTime.Parse(inv.DueDate) : (DateTime?)null;

        var invoiceId = await db.QuerySingleAsync<int>("""
            INSERT INTO GoseiInvoice
                (InvoiceNo, ClientId, IssueDate, DueDate, Subtotal, TaxAmount10, TaxAmount8, Total, Remarks, Status, CreatedAt, UpdatedAt)
            VALUES
                (@InvoiceNo, @ClientId, @IssueDate, @DueDate, @Subtotal, @TaxAmount10, 0, @Total, @Remarks, 'issued', @Now, @Now);
            SELECT last_insert_rowid()
            """,
            new
            {
                InvoiceNo = invoiceNo,
                ClientId  = clientId,
                IssueDate = issueDate,
                DueDate   = dueDate,
                Subtotal  = inv.Subtotal,
                TaxAmount10 = inv.TaxAmount,
                Total     = inv.Total,
                Remarks   = inv.Remarks,
                Now       = DateTime.UtcNow
            });

        // 明細行を挿入
        for (int i = 0; i < inv.Items.Count; i++)
        {
            var item = inv.Items[i];
            await db.ExecuteAsync("""
                INSERT INTO GoseiInvoiceItem
                    (InvoiceId, LineNo, ItemName, OrderNo, Description, Quantity, UnitPrice, Amount, Notes, CreatedAt, UpdatedAt)
                VALUES
                    (@InvoiceId, @LineNo, @ItemName, @OrderNo, @Description, @Quantity, @UnitPrice, @Amount, @Notes, @Now, @Now)
                """,
                new
                {
                    InvoiceId   = invoiceId,
                    LineNo      = i + 1,
                    item.ItemName,
                    item.OrderNo,
                    item.Description,
                    item.Quantity,
                    item.UnitPrice,
                    item.Amount,
                    item.Notes,
                    Now = DateTime.UtcNow
                });
        }

        return (invoiceId, invoiceNo);
    }

    // ─── PDF 生成 ────────────────────────────────────────────────────────────

    private async Task<byte[]?> GenerateInvoicePdfAsync(IDbConnection db, int invoiceId, string projectDir)
    {
        try
        {
            // ヘッダーデータ取得（クライクライアントのテンプレート設定も含む）
            var invoiceRow = await db.QueryFirstOrDefaultAsync(
                "SELECT gi.*, gc.Name AS ClientName, gc.InvoiceTemplate, gc.TemplateVars FROM GoseiInvoice gi LEFT JOIN GoseiClient gc ON gi.ClientId = gc.Id WHERE gi.Id = @Id",
                new { Id = invoiceId });

            if (invoiceRow == null) return null;

            var header = ((IDictionary<string, object>)invoiceRow)
                .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

            // クライアント別テンプレート名（未設定時はデフォルト）
            var templateName = header.TryGetValue("InvoiceTemplate", out var tmplVal) && tmplVal is string s && !string.IsNullOrWhiteSpace(s)
                ? s
                : "gosei-invoice";

            var template = _docPdf.LoadTemplate(projectDir, templateName);
            if (template == null && templateName != "gosei-invoice")
            {
                _logger.LogWarning("PDFテンプレート '{Template}' が見つかりません。デフォルトにフォールバックします", templateName);
                template = _docPdf.LoadTemplate(projectDir, "gosei-invoice");
            }

            if (template == null)
            {
                _logger.LogWarning("PDFテンプレート 'gosei-invoice' が見つかりません");
                return null;
            }

            // TemplateVars（JSON）をヘッダーにマージ
            if (header.TryGetValue("TemplateVars", out var tvRaw) && tvRaw is string tvJson && !string.IsNullOrWhiteSpace(tvJson))
            {
                try
                {
                    using var tvDoc = JsonDocument.Parse(tvJson);
                    foreach (var prop in tvDoc.RootElement.EnumerateObject())
                        header[prop.Name] = prop.Value.GetString();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "TemplateVars のJSONパース失敗: {Json}", tvJson);
                }
            }

            // dataSources 構築（テンプレートのクエリを実行）
            var dataSources = new Dictionary<string, IList<IDictionary<string, object?>>>();

            foreach (var ds in template.DataSources)
            {
                var rows = (await db.QueryAsync(ds.Value.Query, header.ToDictionary(kv => kv.Key, kv => kv.Value)))
                    .Select(r => (IDictionary<string, object?>)((IDictionary<string, object>)r)
                        .ToDictionary(kv => kv.Key, kv => (object?)kv.Value))
                    .ToList<IDictionary<string, object?>>();
                dataSources[ds.Key] = rows;
            }

            return _docPdf.Generate(template, header, dataSources, projectDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF生成エラー: InvoiceId={Id}", invoiceId);
            return null;
        }
    }

    // ─── 返信メール送信 ──────────────────────────────────────────────────────

    private async Task SendReplyWithAttachmentsAsync(
        string smtpServer, int smtpPort, bool smtpSsl,
        string smtpUser, string smtpPass,
        string fromName, string fromAddress,
        MimeMessage original,
        List<string> recipients,
        List<(string Filename, byte[] Bytes)> attachments,
        List<ExtractedInvoice> invoices,
        CancellationToken cancellationToken)
    {
        var reply = new MimeMessage();
        reply.From.Add(new MailboxAddress(fromName, fromAddress));
        foreach (var addr in recipients)
            reply.To.Add(new MailboxAddress(addr, addr));
        var originalSubject = original.Subject ?? string.Empty;
        reply.Subject = originalSubject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
            ? originalSubject : $"Re: {originalSubject}";

        if (!string.IsNullOrEmpty(original.MessageId))
        {
            reply.InReplyTo = original.MessageId;
            reply.References.Add(original.MessageId);
        }

        var sb = new StringBuilder();
        sb.AppendLine("請求書を作成しました。添付のPDFファイルをご確認ください。");
        sb.AppendLine();

        foreach (var inv in invoices)
        {
            sb.AppendLine($"【{inv.ClientName}】");
            foreach (var item in inv.Items)
                sb.AppendLine($"  ・{item.ItemName}　{item.Amount:N0}円");
            sb.AppendLine($"  合計: {inv.Total:N0}円（消費税含む）");

            if (inv.VerificationNotes.Count > 0)
            {
                sb.AppendLine("  ※ 計算内容を確認し、以下の通り補正を行いました：");
                foreach (var note in inv.VerificationNotes)
                    sb.AppendLine($"    {note}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("本メールはシステムによって自動送信されました。");

        var builder = new BodyBuilder { TextBody = sb.ToString() };

        foreach (var (filename, bytes) in attachments)
            builder.Attachments.Add(filename, bytes, new ContentType("application", "pdf"));

        reply.Body = builder.ToMessageBody();

        using var smtp = new MailKit.Net.Smtp.SmtpClient();
        await smtp.ConnectAsync(smtpServer, smtpPort, smtpSsl, cancellationToken);
        await smtp.AuthenticateAsync(smtpUser, smtpPass, cancellationToken);
        await smtp.SendAsync(reply, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);

        _logger.LogInformation("返信メール送信完了: {To}, 添付: {Count}件", string.Join(", ", recipients), attachments.Count);
    }

    // ─── ユーティリティ ──────────────────────────────────────────────────────

    private static Dictionary<string, string> LoadEnv(string filePath)
    {
        var env = new Dictionary<string, string>();
        if (!File.Exists(filePath)) return env;

        foreach (var line in File.ReadAllLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0) continue;

            var key   = trimmed[..eqIndex].Trim();
            var value = trimmed[(eqIndex + 1)..].Trim();

            var commentIdx = value.IndexOf('#');
            if (commentIdx >= 0) value = value[..commentIdx].Trim();

            if (value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2)
                value = value[1..^1];

            env[key] = value;
        }
        return env;
    }
}

// ─── 抽出データモデル ────────────────────────────────────────────────────────

internal class ExtractedInvoiceContainer
{
    [System.Text.Json.Serialization.JsonPropertyName("invoices")]
    public List<ExtractedInvoice> Invoices { get; set; } = new();
    
    [System.Text.Json.Serialization.JsonPropertyName("extraction_notes")]
    public string ExtractionNotes { get; set; } = "";
}

internal class ExtractedInvoice
{
    [System.Text.Json.Serialization.JsonPropertyName("client_name")]
    public string ClientName { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("items")]
    public List<ExtractedInvoiceItem> Items { get; set; } = new();
    
    [System.Text.Json.Serialization.JsonPropertyName("subtotal")]
    public decimal Subtotal { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("tax_amount")]
    public decimal TaxAmount { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("total")]
    public decimal Total { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("due_date")]
    public string? DueDate { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("remarks")]
    public string Remarks { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonIgnore]
    public List<string> VerificationNotes { get; set; } = new();
}

internal class ExtractedInvoiceItem
{
    [System.Text.Json.Serialization.JsonPropertyName("item_name")]
    public string ItemName { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("order_no")]
    public string OrderNo { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string Description { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("quantity")]
    public int Quantity { get; set; } = 1;
    
    [System.Text.Json.Serialization.JsonPropertyName("unit_price")]
    public decimal UnitPrice { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("amount")]
    public decimal Amount { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("notes")]
    public string Notes { get; set; } = "";
}
