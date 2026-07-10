// ファイル概要: AHP (AI Handshake Protocol) の非対称 Handshake 公開エンドポイント。
// QR トークンをスキャンした「アプリを持たない相手」が、ブラウザだけで
// イニシエーターの AI エージェントと会話できるようにする。
// ルート: /{project}/ahp/... （全ルート匿名アクセス可 — FormForge の Public fill 方式に準拠）
//
// フロー:
//   GET  /ai-card/ahp/hs/{token}                → QR 検証 → セッション作成 → チャット画面へ 302
//   GET  /ai-card/ahp/chat/{sessionToken}       → 匿名チャット UI（挨拶メッセージ込み）
//   POST /ai-card/ahp/chat/{sessionToken}/message  → 訪問者メッセージ保存 + AI 応答生成 (JSON)
//   POST /ai-card/ahp/chat/{sessionToken}/complete → 会話要約 + 次アクション生成 → state=completed

using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Services;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Controllers;

[Route("{project}/ahp")]
public class AhpHandshakeController(
    ProjectScope scope,
    IDbConnection db,
    ICliChainService cliChain,
    ILogger<AhpHandshakeController> logger) : BaseProjectController
{
    private string ProjectName => scope.IsSet ? scope.Current.Name : "ai-card";

    private static string NowUtc() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

    // ── 1. QR ランディング ────────────────────────────────

    // GET /{project}/ahp/hs/{token}
    [HttpGet("hs/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> Handshake(string token)
    {
        var qr = await db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM qr_token WHERE token = @token", new { token });

        if (qr == null)
            return HandshakeError("このリンクは無効です", "QR コードが見つかりませんでした。発行者に新しい QR コードを依頼してください。");

        if ((long)qr.is_active != 1)
            return HandshakeError("このリンクは無効化されています", "発行者がこの QR コードを無効にしました。");

        var expiresAt = (string?)qr.expires_at;
        if (!string.IsNullOrEmpty(expiresAt) &&
            DateTime.TryParse(expiresAt, out var exp) && exp < DateTime.UtcNow)
            return HandshakeError("有効期限切れ", "この QR コードの有効期限が切れています。");

        var maxScans = qr.max_scans == null ? (long?)null : (long)qr.max_scans;
        if (maxScans.HasValue && maxScans.Value > 0 && (long)qr.scan_count >= maxScans.Value)
            return HandshakeError("スキャン上限に達しました", "この QR コードは既に上限回数までスキャンされています。");

        // スキャンカウント加算
        await db.ExecuteAsync(
            "UPDATE qr_token SET scan_count = scan_count + 1 WHERE id = @id", new { id = (long)qr.id });

        // 上限に達したらこの QR を退役させる（発行者側の画面が自動で新しい QR に差し替える）
        if (maxScans.HasValue && maxScans.Value > 0 && (long)qr.scan_count + 1 >= maxScans.Value)
        {
            await db.ExecuteAsync(
                "UPDATE qr_token SET is_active = 0 WHERE id = @id", new { id = (long)qr.id });
            logger.LogInformation("[AHP] QR {Token} reached scan limit and was retired.", token);
        }

        // Handshake セッション作成（pending）
        var sessionToken = Guid.NewGuid().ToString("N")[..24];
        var now = NowUtc();
        var sessionExpires = DateTime.UtcNow.AddHours(24).ToString("yyyy-MM-dd HH:mm:ss");

        await db.ExecuteAsync("""
            INSERT INTO handshake_session
                (session_token, initiator_id, state, handshake_type,
                 intent_type, intent_topic, expires_at, created_at, updated_at)
            VALUES
                (@sessionToken, @initiatorId, 'pending', 'asymmetric',
                 @intentType, @intentTopic, @expiresAt, @now, @now)
            """,
            new
            {
                sessionToken,
                initiatorId = (long)qr.ai_identity_id,
                intentType = (string?)qr.intent_type,
                intentTopic = (string?)qr.intent_topic,
                expiresAt = sessionExpires,
                now
            });

        var sessionId = await db.ExecuteScalarAsync<long>(
            "SELECT id FROM handshake_session WHERE session_token = @sessionToken", new { sessionToken });

        // AI の挨拶メッセージを最初に登録（プロフィールの greeting_message 優先）
        var persona = await LoadPersonaAsync((long)qr.ai_identity_id);
        var greeting = !string.IsNullOrWhiteSpace(persona.Greeting)
            ? persona.Greeting!
            : $"初めまして。{persona.DisplayName} の AI アシスタントです。本日はどのようなご用件でしょうか？よろしければお名前と会社名をお聞かせください。";

        await db.ExecuteAsync("""
            INSERT INTO chat_message (session_id, role, content, created_at)
            VALUES (@sessionId, 'ai', @content, @now)
            """, new { sessionId, content = greeting, now });

        // 続けて「何ができるか」の案内 + 自分の AI カード作成の提案を自動送信する。
        // 相手が AI とのチャットに気乗りしなくても、タップ不要でこの情報が最初から見える状態にする。
        var capabilities = BuildCapabilitiesMessage(persona, ProjectName);
        await db.ExecuteAsync("""
            INSERT INTO chat_message (session_id, role, content, created_at)
            VALUES (@sessionId, 'ai', @content, @now)
            """, new { sessionId, content = capabilities, now });

        logger.LogInformation("[AHP] Handshake session {SessionToken} created from QR {Token} (initiator={InitiatorId})",
            sessionToken, token, (long)qr.ai_identity_id);

        // リフレッシュで多重セッションが生成されないよう、チャット画面へリダイレクト
        return Redirect($"/{ProjectName}/ahp/chat/{sessionToken}");
    }

    // ── 2. 匿名チャット画面 ──────────────────────────────

    // GET /{project}/ahp/chat/{sessionToken}
    [HttpGet("chat/{sessionToken}")]
    [AllowAnonymous]
    public async Task<IActionResult> Chat(string sessionToken)
    {
        var session = await GetSessionAsync(sessionToken);
        if (session == null)
            return HandshakeError("セッションが見つかりません", "リンクが正しいかご確認ください。");

        var persona = await LoadPersonaAsync((long)session!.initiator_id);

        var messages = (await db.QueryAsync<dynamic>(
                "SELECT role, content, created_at FROM chat_message WHERE session_id = @id ORDER BY id",
                new { id = (long)session.id }))
            .Select(m => new AhpChatMessage((string)m.role, (string)m.content, (string)m.created_at))
            .ToList();

        var vm = new AhpChatViewModel
        {
            ProjectName = ProjectName,
            SessionToken = sessionToken,
            State = (string)session.state,
            IntentType = (string?)session.intent_type,
            IntentTopic = (string?)session.intent_topic,
            InitiatorName = persona.DisplayName,
            InitiatorAiId = persona.AiId,
            InitiatorOrg = persona.Organization,
            InitiatorRole = persona.Role,
            InitiatorExpertise = ParseJsonStringArray(persona.ExpertiseJson),
            InitiatorGoals = ParseJsonStringArray(persona.GoalsJson),
            RegisterUrl = $"/{ProjectName}/Account/Register",
            LinkedResponderAiId = ExtractResponderAiId((string?)session.intent_context_json),
            Messages = messages
        };

        return View($"~/projects/{ProjectName}/views/HandshakeChat.cshtml", vm);
    }

    // ── 3.5 相手側の AI ID を紐付け（将来の AI 同士の自動連携の下地） ──

    public record AhpLinkAiRequest(string AiId);

    // POST /{project}/ahp/chat/{sessionToken}/link-ai
    [HttpPost("chat/{sessionToken}/link-ai")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> LinkAi(string sessionToken, [FromBody] AhpLinkAiRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.AiId))
            return BadRequest(new { error = "AI ID を入力してください。" });

        var session = await GetSessionAsync(sessionToken);
        if (session == null)
            return NotFound(new { error = "セッションが見つかりません。" });

        var merged = MergeIntentContext((string?)session.intent_context_json, "responder_ai_id", req.AiId.Trim());
        var now = NowUtc();
        await db.ExecuteAsync(
            "UPDATE handshake_session SET intent_context_json = @json, updated_at = @now WHERE id = @id",
            new { id = (long)session.id, json = merged, now });

        logger.LogInformation("[AHP] Session {Id} linked responder AI ID {AiId} (future auto-agent handshake seed)",
            (long)session.id, req.AiId.Trim());

        return Json(new { ok = true, aiId = req.AiId.Trim() });
    }

    // ── 3. メッセージ送信 + AI 応答 ──────────────────────

    public record AhpMessageRequest(string Message, string? VisitorName);

    // POST /{project}/ahp/chat/{sessionToken}/message
    [HttpPost("chat/{sessionToken}/message")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PostMessage(string sessionToken, [FromBody] AhpMessageRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.Message))
            return BadRequest(new { error = "メッセージが空です。" });

        var session = await GetSessionAsync(sessionToken);
        if (session == null)
            return NotFound(new { error = "セッションが見つかりません。" });

        var state = (string)session!.state;
        if (state != "pending" && state != "connected")
            return BadRequest(new { error = $"このセッションは '{state}' 状態のため会話できません。" });

        var expiresAt = (string?)session.expires_at;
        if (!string.IsNullOrEmpty(expiresAt) &&
            DateTime.TryParse(expiresAt, out var exp) && exp < DateTime.UtcNow)
        {
            await db.ExecuteAsync(
                "UPDATE handshake_session SET state = 'expired', updated_at = @now WHERE id = @id",
                new { id = (long)session.id, now = NowUtc() });
            return BadRequest(new { error = "セッションの有効期限が切れました。" });
        }

        var sessionId = (long)session.id;
        var now = NowUtc();

        // pending → connected（最初のメッセージで状態遷移）
        if (state == "pending")
        {
            await db.ExecuteAsync(
                "UPDATE handshake_session SET state = 'connected', updated_at = @now WHERE id = @id",
                new { id = sessionId, now });
            logger.LogInformation("[AHP] Session {Id} transitioned pending → connected", sessionId);
        }

        // 訪問者名が入力されたら responder_name に反映
        if (!string.IsNullOrWhiteSpace(req.VisitorName))
        {
            await db.ExecuteAsync(
                "UPDATE handshake_session SET responder_name = @name, updated_at = @now WHERE id = @id AND (responder_name IS NULL OR responder_name = '')",
                new { id = sessionId, name = req.VisitorName.Trim(), now });
        }

        // 訪問者メッセージ保存
        await db.ExecuteAsync(
            "INSERT INTO chat_message (session_id, role, content, created_at) VALUES (@sessionId, 'human', @content, @now)",
            new { sessionId, content = req.Message.Trim(), now });

        // AI 応答生成
        var persona = await LoadPersonaAsync((long)session.initiator_id);
        var history = (await db.QueryAsync<dynamic>(
                "SELECT role, content FROM chat_message WHERE session_id = @id ORDER BY id DESC LIMIT 20",
                new { id = sessionId }))
            .Reverse()
            .ToList();

        var prompt = BuildAgentPrompt(
            persona, (string?)session.intent_type, (string?)session.intent_topic, history);
        var result = await cliChain.PromptAsync(prompt, projectName: ProjectName);

        string reply;
        if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
        {
            reply = result.Text!.Trim();
            logger.LogInformation("[AHP] AI reply generated for session {Id} via {Provider}", sessionId, result.Provider);
        }
        else
        {
            reply = "申し訳ありません。ただいま AI アシスタントが混み合っております。少し待ってからもう一度お送りください。";
            logger.LogWarning("[AHP] AI reply failed for session {Id}: {Error}", sessionId, result.Error);
        }

        await db.ExecuteAsync(
            "INSERT INTO chat_message (session_id, role, content, created_at) VALUES (@sessionId, 'ai', @content, @now)",
            new { sessionId, content = reply, now = NowUtc() });

        return Json(new { reply });
    }

    // ── 4. 会話終了（要約 + 次アクション生成） ───────────

    // POST /{project}/ahp/chat/{sessionToken}/complete
    [HttpPost("chat/{sessionToken}/complete")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Complete(string sessionToken)
    {
        var session = await GetSessionAsync(sessionToken);
        if (session == null)
            return NotFound(new { error = "セッションが見つかりません。" });

        var state = (string)session!.state;
        if (state == "completed")
            return Json(new { summary = (string?)session.conversation_summary ?? "" });
        if (state != "pending" && state != "connected")
            return BadRequest(new { error = $"このセッションは '{state}' 状態です。" });

        var sessionId = (long)session.id;
        var history = (await db.QueryAsync<dynamic>(
                "SELECT role, content FROM chat_message WHERE session_id = @id ORDER BY id",
                new { id = sessionId })).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("以下は、ビジネス AI アシスタントと訪問者の会話ログです。");
        sb.AppendLine("この会話を CRM 登録用に整理し、次の JSON だけを出力してください（コードフェンス・説明文なし）:");
        sb.AppendLine("""{"summary":"会話の要約(2-3文)","responder_name":"訪問者名(不明なら空)","responder_company":"訪問者の会社(不明なら空)","next_actions":["次のアクション1","次のアクション2"]}""");
        sb.AppendLine();
        sb.AppendLine("--- 会話ログ ---");
        foreach (var m in history)
            sb.AppendLine($"[{(string)m.role}] {(string)m.content}");

        var result = await cliChain.PromptAsync(sb.ToString(), projectName: ProjectName);

        var summary = "";
        string? nextActionsJson = null;
        string? respName = null, respCompany = null;

        if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
        {
            var text = StripCodeFence(result.Text!);
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                respName = root.TryGetProperty("responder_name", out var rn) ? rn.GetString() : null;
                respCompany = root.TryGetProperty("responder_company", out var rc) ? rc.GetString() : null;
                if (root.TryGetProperty("next_actions", out var na) && na.ValueKind == JsonValueKind.Array)
                    nextActionsJson = na.GetRawText();
            }
            catch (JsonException)
            {
                // JSON でなければ全文を要約として保存（PoC のフォールバック）
                summary = text.Trim();
            }
        }

        var now = NowUtc();
        await db.ExecuteAsync("""
            UPDATE handshake_session SET
                state = 'completed',
                conversation_summary = @summary,
                next_actions_json = COALESCE(@nextActions, next_actions_json),
                responder_name = CASE WHEN (responder_name IS NULL OR responder_name = '') AND @respName IS NOT NULL AND @respName <> '' THEN @respName ELSE responder_name END,
                responder_company = CASE WHEN (responder_company IS NULL OR responder_company = '') AND @respCompany IS NOT NULL AND @respCompany <> '' THEN @respCompany ELSE responder_company END,
                updated_at = @now
            WHERE id = @id
            """,
            new { id = sessionId, summary, nextActions = nextActionsJson, respName, respCompany, now });

        logger.LogInformation("[AHP] Session {Id} completed with summary ({Len} chars)", sessionId, summary.Length);

        return Json(new { summary, nextActions = nextActionsJson });
    }

    // ── ヘルパー ─────────────────────────────────────────

    private async Task<dynamic?> GetSessionAsync(string sessionToken) =>
        await db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM handshake_session WHERE session_token = @sessionToken",
            new { sessionToken });

    private sealed record AhpPersona(
        string DisplayName, string AiId, string? Organization, string? Role,
        string? Greeting, string? GoalsJson, string? CanShareJson,
        string? CannotShareJson, string? ExpertiseJson, string? Instructions);

    private async Task<AhpPersona> LoadPersonaAsync(long aiIdentityId)
    {
        var row = await db.QueryFirstOrDefaultAsync<dynamic>("""
            SELECT i.display_name, i.ai_id, i.organization, i.role,
                   p.greeting_message, p.goals_json, p.can_share_json,
                   p.cannot_share_json, p.expertise_json, p.ai_instructions
            FROM ai_identity i
            LEFT JOIN ai_profile p ON p.ai_identity_id = i.id
            WHERE i.id = @id
            """, new { id = aiIdentityId });

        if (row == null)
            return new AhpPersona("AI アシスタント", "ai://unknown", null, null, null, null, null, null, null, null);

        return new AhpPersona(
            (string)row.display_name, (string)row.ai_id,
            (string?)row.organization, (string?)row.role,
            (string?)row.greeting_message, (string?)row.goals_json,
            (string?)row.can_share_json, (string?)row.cannot_share_json,
            (string?)row.expertise_json, (string?)row.ai_instructions);
    }

    private static string BuildAgentPrompt(
        AhpPersona p, string? intentType, string? intentTopic, IReadOnlyList<dynamic> history)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"あなたは {p.DisplayName}（AI ID: {p.AiId}）のビジネス AI アシスタントです。");
        if (!string.IsNullOrEmpty(p.Organization) || !string.IsNullOrEmpty(p.Role))
            sb.AppendLine($"本人の所属: {p.Organization ?? "-"} / 役割: {p.Role ?? "-"}");

        if (!string.IsNullOrEmpty(intentType) || !string.IsNullOrEmpty(intentTopic))
            sb.AppendLine($"この面談の目的 (Intent): {intentType ?? "-"} / トピック: {intentTopic ?? "-"}");

        if (!string.IsNullOrEmpty(p.GoalsJson)) sb.AppendLine($"本人のゴール: {p.GoalsJson}");
        if (!string.IsNullOrEmpty(p.ExpertiseJson)) sb.AppendLine($"専門分野: {p.ExpertiseJson}");
        if (!string.IsNullOrEmpty(p.CanShareJson)) sb.AppendLine($"共有してよい情報 (Permission): {p.CanShareJson}");
        if (!string.IsNullOrEmpty(p.CannotShareJson))
            sb.AppendLine($"【絶対に共有禁止】: {p.CannotShareJson} — これらを聞かれたら「本人に直接ご確認ください」と丁寧に断ること。");
        if (!string.IsNullOrEmpty(p.Instructions)) sb.AppendLine($"追加指示: {p.Instructions}");

        sb.AppendLine();
        sb.AppendLine("ルール:");
        sb.AppendLine("- 訪問者が使った言語と同じ言語で返答する。");
        sb.AppendLine("- 返答は 2〜4 文で簡潔に。ビジネスとして礼儀正しく。");
        sb.AppendLine("- 訪問者の名前・会社がまだ不明なら、会話の流れの中で自然に尋ねる。");
        sb.AppendLine("- プレーンテキストのみ出力する（Markdown 記法・前置き・署名なし）。");
        sb.AppendLine();
        sb.AppendLine("--- これまでの会話 ---");
        foreach (var m in history)
        {
            var roleLabel = (string)m.role == "ai" ? "あなた(AI)" : "訪問者";
            sb.AppendLine($"[{roleLabel}] {(string)m.content}");
        }
        sb.AppendLine();
        sb.AppendLine("上記の会話の続きとして、訪問者の最後のメッセージへの返答だけを出力してください。");
        return sb.ToString();
    }

    // 挨拶に続けて自動送信する「できること + 自分の AI カード作成の提案」メッセージ。
    // チャットが苦手／面倒な相手でも、タップせずに要点だけ読めるようにする。
    private static string BuildCapabilitiesMessage(AhpPersona p, string projectName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("💡 私にできること：");

        var expertise = ParseJsonStringArray(p.ExpertiseJson);
        if (expertise.Count > 0)
            sb.AppendLine($"・{p.DisplayName} の専門分野（{string.Join(" / ", expertise)}）についてのご質問にお答えします");
        else
            sb.AppendLine($"・{p.DisplayName} に関するご質問にお答えします");

        sb.AppendLine("・日程調整や資料共有など、簡単なご要望を承ります");
        sb.AppendLine("・チャットが面倒でしたら、それでも大丈夫です。このまま閉じていただいて問題ありません。");
        sb.AppendLine();
        sb.AppendLine("🤝 もしよろしければ、あなたも AI カードを作ってみませんか？");
        sb.AppendLine($"次回からはお互いの AI 同士が自動でやり取りできるようになります → /{projectName}/Account/Register");

        return sb.ToString().TrimEnd();
    }

    // "[\"a\",\"b\"]" 形式の JSON 文字列配列を安全にパースする。壊れた JSON やカンマ区切りテキストも許容。
    private static List<string> ParseJsonStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return doc.RootElement.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!.Trim())
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // JSON でなければカンマ区切りテキストとして扱う（PoC のフォールバック）
        }
        return json.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    // intent_context_json（現状未使用の汎用メタデータ列）に 1 キーだけ安全にマージする。
    private static string MergeIntentContext(string? existingJson, string key, string value)
    {
        JsonObject obj;
        try
        {
            obj = !string.IsNullOrWhiteSpace(existingJson)
                ? (JsonNode.Parse(existingJson) as JsonObject ?? [])
                : [];
        }
        catch (JsonException)
        {
            obj = [];
        }

        obj[key] = value;
        obj[$"{key}_linked_at"] = NowUtc();
        return obj.ToJsonString();
    }

    private static string? ExtractResponderAiId(string? intentContextJson)
    {
        if (string.IsNullOrWhiteSpace(intentContextJson)) return null;
        try
        {
            var obj = JsonNode.Parse(intentContextJson) as JsonObject;
            return obj?["responder_ai_id"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string StripCodeFence(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("```"))
        {
            var firstNewline = t.IndexOf('\n');
            if (firstNewline >= 0) t = t[(firstNewline + 1)..];
            var lastFence = t.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) t = t[..lastFence];
        }
        return t.Trim();
    }

    private ContentResult HandshakeError(string title, string message)
    {
        var html = $$"""
            <!DOCTYPE html>
            <html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{System.Net.WebUtility.HtmlEncode(title)}}</title>
            <style>body{font-family:-apple-system,'Segoe UI',sans-serif;background:#0f172a;color:#e2e8f0;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0}
            .card{background:#1e293b;border-radius:16px;padding:40px;max-width:420px;text-align:center;box-shadow:0 20px 60px rgba(0,0,0,.4)}
            .icon{font-size:48px;margin-bottom:16px}h1{font-size:20px;margin:0 0 12px}p{color:#94a3b8;font-size:14px;line-height:1.7;margin:0}</style></head>
            <body><div class="card"><div class="icon">🔒</div><h1>{{System.Net.WebUtility.HtmlEncode(title)}}</h1><p>{{System.Net.WebUtility.HtmlEncode(message)}}</p></div></body></html>
            """;
        return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = 200 };
    }
}

// ── ビューモデル（HandshakeChat.cshtml 用） ──────────────

public record AhpChatMessage(string Role, string Content, string CreatedAt);

public class AhpChatViewModel
{
    public string ProjectName { get; set; } = "ai-card";
    public string SessionToken { get; set; } = "";
    public string State { get; set; } = "pending";
    public string? IntentType { get; set; }
    public string? IntentTopic { get; set; }
    public string InitiatorName { get; set; } = "";
    public string InitiatorAiId { get; set; } = "";
    public string? InitiatorOrg { get; set; }
    public string? InitiatorRole { get; set; }
    public List<string> InitiatorExpertise { get; set; } = [];
    public List<string> InitiatorGoals { get; set; } = [];
    public string RegisterUrl { get; set; } = "";
    public string? LinkedResponderAiId { get; set; }
    public List<AhpChatMessage> Messages { get; set; } = [];
}
