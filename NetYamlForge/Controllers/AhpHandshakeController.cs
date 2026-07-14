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

    // 双方AI自動交渉の暴走防止（LLM同士が礼儀正しく無限に喋り続けるのを防ぐ上限）。
    private const int MaxAutoTurns = 8;

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
            : $"どうも、{persona.DisplayName}の AI です。今日はどんな用件でいらっしゃいましたか？";

        await db.ExecuteAsync("""
            INSERT INTO chat_message (session_id, role, content, created_at)
            VALUES (@sessionId, 'ai', @content, @now)
            """, new { sessionId, content = greeting, now });

        // 「できること」の案内は自動送信しない。相手がチャットに気乗りしなくても構わないように、
        // 気になる相手だけが下の chip「💬 何ができる？」を押せば同じ内容を本物の AI が答える。

        // 「録画じゃなく今動いてます」的な活体証明メッセージは冗長との判断で削除済み。
        // 自動送信するのは挨拶メッセージ（persona.Greeting）1 通のみ。

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

        // 名刺画像の QR に載せる「再利用可能な」公開エントリー URL。
        // 今開いている sessionToken は "この訪問者専用" の一回限りリンクなので使わない。
        // 同じ AI ID の現在有効な QR（何度でもスキャンされ得る入口）を優先し、
        // 万一見つからなければ最後の手段として現在のチャット URL にフォールバックする。
        var qrRelativePath = await db.ExecuteScalarAsync<string?>("""
            SELECT qr_url FROM qr_token
            WHERE ai_identity_id = @id AND is_active = 1
            ORDER BY created_at DESC LIMIT 1
            """, new { id = (long)session.initiator_id })
            ?? $"/{ProjectName}/ahp/chat/{sessionToken}";
        var initiatorQrUrl = $"{Request.Scheme}://{Request.Host}{qrRelativePath}";

        // 「同じ NetYamlForge インスタンス内に、相手も ai_identity を持っている」場合の
        // AI 同士自動交渉の下地。responder_ai_identity_id が埋まっていれば、両方のペルソナを
        // 使ってサーバー側で交互に応答を生成できる（federation はまだ扱わない、詳細は
        // ai-card_federation_scope メモリを参照）。
        var ctx = ParseIntentContext((string?)session.intent_context_json);
        long? responderAiIdentityId = null;
        if (ctx.TryGetPropertyValue("responder_ai_identity_id", out var ridNode) && ridNode != null)
            responderAiIdentityId = ridNode.GetValue<long>();

        string? responderDisplayName = null;
        bool responderIsGuest = false;
        if (responderAiIdentityId != null)
        {
            var responderPersona = await LoadPersonaAsync(responderAiIdentityId.Value);
            responderDisplayName = responderPersona.DisplayName;
            responderIsGuest = responderPersona.OwnerType == "guest";
        }

        var negotiation = ReadNegotiation(ctx);
        var proposals = GetProposals(ctx)
            .Select(p => new AhpProposalVm(p.Id, p.Text, p.ProposedBy, p.State, p.CreatedAt))
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
            InitiatorQrUrl = initiatorQrUrl,
            RegisterUrl = $"/{ProjectName}/Account/Register",
            LinkedResponderAiId = ExtractResponderAiId((string?)session.intent_context_json),
            ResponderLinkedLocally = responderAiIdentityId != null,
            ResponderDisplayName = responderDisplayName,
            ResponderIsGuest = responderIsGuest,
            NegotiationState = negotiation.State,
            NegotiationTurns = negotiation.Turns,
            Proposals = proposals,
            Messages = messages
        };

        return View($"~/projects/{ProjectName}/views/HandshakeChat.cshtml", vm);
    }

    // ── 3.5 相手側の AI ID を紐付け ──
    // 入力された ai:// が「同じ NetYamlForge インスタンス内に実在する ai_identity」なら
    // その場で responder_ai_identity_id を解決し、AI 同士の自動交渉（/ai-turn）を有効化する。
    // 存在しない/よそのサーバーの ID なら、これまで通り文字列として保存するだけに留める
    // （federation は別スコープ。ai-card_federation_scope メモリ参照）。

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

        var aiId = req.AiId.Trim();
        var match = await db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT id, display_name FROM ai_identity WHERE ai_id = @aiId AND is_active = 1",
            new { aiId });

        if (match is not null && (long)match.id == (long)session!.initiator_id)
        {
            // 自分自身の AI とは交渉できない（PoC: セッション内の自己ループを防ぐ）
            return Json(new { ok = true, aiId, linkedLocally = false, note = "自分自身の AI ID とは連携できません。" });
        }

        var ctx = ParseIntentContext((string?)session.intent_context_json);
        ctx["responder_ai_id"] = aiId;
        ctx["responder_ai_id_linked_at"] = NowUtc();

        // `match != null` を var で受けると dynamic 比較の結果が dynamic 型のまま推論され、
        // 後段の LogInformation（extension method）呼び出しが dynamic 引数のせいで解決不能になる
        // （CS1973）ため、明示的に bool へ確定させる。
        bool linkedLocally = match != null;
        string? responderDisplayName = null;

        if (match != null)
        {
            responderDisplayName = (string)match.display_name;
            ctx["responder_ai_identity_id"] = (long)match.id;
            if (ctx["negotiation"] is not JsonObject)
                WriteNegotiation(ctx, new NegotiationState("greeting", 0, "initiator"));
        }

        await SaveIntentContext((long)session.id, ctx);

        logger.LogInformation(
            "[AHP] Session {Id} linked responder AI ID {AiId} (linkedLocally={Local})",
            (long)session.id, aiId, linkedLocally);

        return Json(new { ok = true, aiId, linkedLocally, responderDisplayName });
    }

    // ── 3.55 相手がまだ ai:// を持っていない場合、その場で仮の AI 名刺を発行する ──
    // 「同库互聊」の前提（双方が ai_identity を持つこと）は、正式登録という重い壁を越えないと
    // 満たせない — これが最も頻度の高い離脱ポイントだった。owner_type='guest' + verified=0 で
    // 正式登録済みの身元とは区別し（新しい列は増やさない）、UI 側で「情報が少ない仮IDです」と
    // 提案カードに注意書きを出す（信頼境界は緩めない）。

    public record AhpCreateGuestAiRequest(string? DisplayName, string? Company, string? Goal);

    // POST /{project}/ahp/chat/{sessionToken}/create-guest-ai
    [HttpPost("chat/{sessionToken}/create-guest-ai")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CreateGuestAi(string sessionToken, [FromBody] AhpCreateGuestAiRequest req)
    {
        var session = await GetSessionAsync(sessionToken);
        if (session == null)
            return NotFound(new { error = "セッションが見つかりません。" });

        var sessionId = (long)session.id;
        var ctx = ParseIntentContext((string?)session.intent_context_json);

        // このセッションで既に連携/作成済みなら二重作成しない（ボタン連打・再送信対策）。
        if (ctx.TryGetPropertyValue("responder_ai_identity_id", out var existingRid) && existingRid != null)
        {
            var existingPersona = await LoadPersonaAsync(existingRid.GetValue<long>());
            return Json(new
            {
                ok = true,
                aiId = existingPersona.AiId,
                linkedLocally = true,
                responderDisplayName = existingPersona.DisplayName,
                guest = existingPersona.OwnerType == "guest"
            });
        }

        var displayName = !string.IsNullOrWhiteSpace(req?.DisplayName)
            ? req!.DisplayName!.Trim()
            : (!string.IsNullOrWhiteSpace((string?)session.responder_name) ? (string)session.responder_name : "ゲスト");
        var company = !string.IsNullOrWhiteSpace(req?.Company) ? req!.Company!.Trim() : (string?)session.responder_company;
        var goal = req?.Goal?.Trim();

        var aiId = $"ai://guest/{sessionToken[..Math.Min(8, sessionToken.Length)]}";
        var taken = await db.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM ai_identity WHERE ai_id = @aiId", new { aiId });
        if (taken > 0)
            aiId = $"ai://guest/{sessionToken}"; // 極めて稀な短縮衝突時のフォールバック（フルトークンなら実質一意）

        var now = NowUtc();
        await db.ExecuteAsync("""
            INSERT INTO ai_identity (ai_id, display_name, owner_type, organization, is_active, verified, created_at, updated_at)
            VALUES (@aiId, @displayName, 'guest', @company, 1, 0, @now, @now)
            """, new { aiId, displayName, company, now });

        var newIdentityId = await db.ExecuteScalarAsync<long>(
            "SELECT id FROM ai_identity WHERE ai_id = @aiId", new { aiId });

        var goalsJson = string.IsNullOrWhiteSpace(goal) ? null : JsonSerializer.Serialize(new[] { goal });
        var greeting = $"どうも、{displayName}です。その場で作った仮の AI 名刺なので、まだ情報は少ないですがよろしくお願いします。";
        await db.ExecuteAsync("""
            INSERT INTO ai_profile (ai_identity_id, goals_json, greeting_message, updated_at)
            VALUES (@newIdentityId, @goalsJson, @greeting, @now)
            """, new { newIdentityId, goalsJson, greeting, now });

        // 訪問者名がまだ空なら、ここで入力された名前を handshake_session 側にも反映する（PostMessage と同じ扱い）。
        await db.ExecuteAsync("""
            UPDATE handshake_session
            SET responder_name = CASE WHEN (responder_name IS NULL OR responder_name = '') THEN @displayName ELSE responder_name END,
                updated_at = @now
            WHERE id = @id
            """, new { id = sessionId, displayName, now });

        ctx["responder_ai_id"] = aiId;
        ctx["responder_ai_id_linked_at"] = now;
        ctx["responder_ai_identity_id"] = newIdentityId;
        if (ctx["negotiation"] is not JsonObject)
            WriteNegotiation(ctx, new NegotiationState("greeting", 0, "initiator"));
        await SaveIntentContext(sessionId, ctx);

        await db.ExecuteAsync(
            "INSERT INTO chat_message (session_id, role, content, created_at) VALUES (@sessionId, 'system', @content, @now)",
            new { sessionId, content = $"🆕 その場で {displayName} の仮 AI 名刺（{aiId}）を作成しました。", now });

        logger.LogInformation("[AHP] Session {Id} created guest ai_identity {AiId} (id={NewId})",
            sessionId, aiId, newIdentityId);

        return Json(new { ok = true, aiId, linkedLocally = true, responderDisplayName = displayName, guest = true });
    }

    // ── 3.6 双方AI自動交渉：1ターン進める ──
    // responder_ai_identity_id が同一インスタンス内で解決できているセッションでのみ動く。
    // 「寒暄 → 意図交換 → 提案 → 人間確認待ち → 成立/却下」という状態機械を
    // intent_context_json.negotiation に持たせ、具体的な決めごと（PROPOSAL:）が出た瞬間に
    // 自動再生を止めて人間の承認を必須にする（AI同士の合意を自動で実効化しないための閂）。

    // POST /{project}/ahp/chat/{sessionToken}/ai-turn
    [HttpPost("chat/{sessionToken}/ai-turn")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AiTurn(string sessionToken)
    {
        var session = await GetSessionAsync(sessionToken);
        if (session == null)
            return NotFound(new { error = "セッションが見つかりません。" });

        var state = (string)session!.state;
        if (state != "pending" && state != "connected")
            return BadRequest(new { error = $"このセッションは '{state}' 状態のため交渉できません。" });

        var expiresAt = (string?)session.expires_at;
        if (!string.IsNullOrEmpty(expiresAt) &&
            DateTime.TryParse(expiresAt, out var exp) && exp < DateTime.UtcNow)
        {
            await db.ExecuteAsync(
                "UPDATE handshake_session SET state = 'expired', updated_at = @now WHERE id = @id",
                new { id = (long)session.id, now = NowUtc() });
            return BadRequest(new { error = "セッションの有効期限が切れました。" });
        }

        var ctx = ParseIntentContext((string?)session.intent_context_json);
        long? responderAiIdentityId = null;
        if (ctx.TryGetPropertyValue("responder_ai_identity_id", out var ridNode) && ridNode != null)
            responderAiIdentityId = ridNode.GetValue<long>();
        if (responderAiIdentityId == null)
            return BadRequest(new { error = "相手側の AI ID がまだ連携されていません。" });

        var sessionId = (long)session.id;
        var neg = ReadNegotiation(ctx);

        if (neg.State is "agreed" or "rejected")
            return Json(new { done = true, negotiationState = neg.State, turns = neg.Turns });
        if (neg.State == "pending_confirmation")
            return Json(new { paused = true, negotiationState = neg.State, reason = "人間の確認待ちです。" });
        if (neg.Turns >= MaxAutoTurns)
        {
            WriteNegotiation(ctx, neg with { State = "stalled" });
            await SaveIntentContext(sessionId, ctx);
            return Json(new { stalled = true, negotiationState = "stalled", turns = neg.Turns });
        }

        if (state == "pending")
        {
            await db.ExecuteAsync(
                "UPDATE handshake_session SET state = 'connected', updated_at = @now WHERE id = @id",
                new { id = sessionId, now = NowUtc() });
            logger.LogInformation("[AHP] Session {Id} transitioned pending → connected (ai-turn)", sessionId);
        }

        var speakerIsInitiator = neg.NextSpeaker != "responder";
        var selfId = speakerIsInitiator ? (long)session.initiator_id : responderAiIdentityId.Value;
        var counterpartId = speakerIsInitiator ? responderAiIdentityId.Value : (long)session.initiator_id;

        var selfPersona = await LoadPersonaAsync(selfId);
        var counterpartPersona = await LoadPersonaAsync(counterpartId);

        var history = (await db.QueryAsync<dynamic>(
                "SELECT role, content FROM chat_message WHERE session_id = @id ORDER BY id DESC LIMIT 20",
                new { id = sessionId }))
            .Reverse()
            .ToList();

        var prompt = BuildNegotiationPrompt(
            selfPersona, counterpartPersona, (string?)session.intent_type, (string?)session.intent_topic,
            history);
        var result = await cliChain.PromptAsync(prompt, projectName: ProjectName);

        var raw = result.Success && !string.IsNullOrWhiteSpace(result.Text)
            ? result.Text!.Trim()
            : "（応答生成に失敗しました。少し待ってからもう一度お試しください。）";
        logger.LogInformation("[AHP] AI-AI turn generated for session {Id} (speaker={Speaker}, provider={Provider})",
            sessionId, speakerIsInitiator ? "initiator" : "responder", result.Provider);

        var (replyText, proposalText) = ExtractProposal(raw);
        var roleTag = speakerIsInitiator ? "ai_initiator" : "ai_responder";
        var now = NowUtc();
        await db.ExecuteAsync(
            "INSERT INTO chat_message (session_id, role, content, created_at) VALUES (@sessionId, @role, @content, @now)",
            new { sessionId, role = roleTag, content = replyText, now });

        var nextTurns = neg.Turns + 1;
        var nextSpeaker = speakerIsInitiator ? "responder" : "initiator";
        object? proposalPayload = null;
        string nextState;

        if (proposalText != null)
        {
            var proposal = new ProposalRecord(
                $"PR-{sessionId:D6}-{GetProposals(ctx).Count + 1}",
                proposalText,
                speakerIsInitiator ? "initiator" : "responder",
                "pending_confirmation",
                now);
            AppendProposal(ctx, proposal);
            nextState = "pending_confirmation";
            proposalPayload = new
            {
                id = proposal.Id,
                text = proposal.Text,
                proposedBy = proposal.ProposedBy,
                createdAt = proposal.CreatedAt
            };
            logger.LogInformation("[AHP] Session {Id} AI-AI negotiation produced proposal {ProposalId}",
                sessionId, proposal.Id);
        }
        else
        {
            nextState = nextTurns >= MaxAutoTurns ? "stalled" : AdvanceState(neg.State);
        }

        WriteNegotiation(ctx, new NegotiationState(nextState, nextTurns, nextSpeaker));
        await SaveIntentContext(sessionId, ctx);

        return Json(new
        {
            reply = replyText,
            speaker = roleTag,
            negotiationState = nextState,
            turns = nextTurns,
            nextSpeaker,
            proposal = proposalPayload
        });
    }

    // ── 3.7 双方AI自動交渉の提案を人間が確認 ──
    // AI同士がどれだけ「合意」しても、実際に next_actions / CRM に響く効果を持たせるのは
    // 必ずここを通した人間の確認のみ。これが「AIの結論を自動で実効化しない」という信頼境界。

    public record AhpProposalDecisionRequest(bool Approve);

    // POST /{project}/ahp/chat/{sessionToken}/proposal/{proposalId}/decide
    [HttpPost("chat/{sessionToken}/proposal/{proposalId}/decide")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> DecideProposal(string sessionToken, string proposalId, [FromBody] AhpProposalDecisionRequest req)
    {
        var session = await GetSessionAsync(sessionToken);
        if (session == null)
            return NotFound(new { error = "セッションが見つかりません。" });

        var ctx = ParseIntentContext((string?)session.intent_context_json);
        var proposals = GetProposals(ctx);
        var idx = proposals.FindIndex(p => p.Id == proposalId);
        if (idx == -1)
            return NotFound(new { error = "提案が見つかりません。" });
        if (proposals[idx].State != "pending_confirmation")
            return BadRequest(new { error = "この提案は既に処理済みです。" });

        var decided = proposals[idx] with { State = req.Approve ? "agreed" : "rejected" };
        ReplaceProposal(ctx, idx, decided);

        var neg = ReadNegotiation(ctx);
        // 承認 = この交渉は成立して終了。却下 = 交渉を続行できるよう意図交換フェーズへ戻す。
        var nextNegotiationState = req.Approve ? "agreed" : "intent_exchange";
        WriteNegotiation(ctx, neg with { State = nextNegotiationState });

        var sessionId = (long)session.id;
        var now = NowUtc();
        var systemNote = req.Approve
            ? $"✅ 人間が提案「{decided.Text}」を承認しました。"
            : $"❌ 人間が提案「{decided.Text}」を却下しました。交渉を続けられます。";
        await db.ExecuteAsync(
            "INSERT INTO chat_message (session_id, role, content, created_at) VALUES (@sessionId, 'system', @content, @now)",
            new { sessionId, content = systemNote, now });

        await SaveIntentContext(sessionId, ctx);

        logger.LogInformation("[AHP] Session {Id} proposal {ProposalId} decided: {Decision}",
            sessionId, proposalId, decided.State);

        return Json(new { ok = true, state = decided.State, negotiationState = nextNegotiationState });
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

        // 「意図 → 実物」の可視化閉環：訪問者の発言から意図（資料が欲しい／会いたい）を検出したら、
        // その場で「記録が作られた」ことが分かる実体（artifact）を 1 セッションにつき種類ごと 1 回だけ生成する。
        object? artifactPayload = null;
        var detected = DetectIntentArtifact(req.Message);
        if (detected != null)
        {
            var currentArtifacts = ExtractArtifacts((string?)session.intent_context_json);
            if (!currentArtifacts.Any(a => a.Type == detected.Value.Type))
            {
                var artifact = new ArtifactRecord(
                    detected.Value.Type,
                    detected.Value.Title,
                    detected.Value.Type == "resource" ? $"/{ProjectName}/ahp/resource/{sessionToken}" : null,
                    $"HS-{sessionId:D6}-{currentArtifacts.Count + 1}",
                    NowUtc());

                var mergedJson = AppendArtifact((string?)session.intent_context_json, artifact);
                await db.ExecuteAsync(
                    "UPDATE handshake_session SET intent_context_json = @json, updated_at = @now WHERE id = @id",
                    new { id = sessionId, json = mergedJson, now = NowUtc() });

                artifactPayload = new
                {
                    type = artifact.Type,
                    title = artifact.Title,
                    url = artifact.Url,
                    id = artifact.Id,
                    createdAt = artifact.CreatedAt
                };
                logger.LogInformation("[AHP] Session {Id} generated artifact {Type} ({ArtifactId})",
                    sessionId, artifact.Type, artifact.Id);
            }
        }

        return Json(new { reply, artifact = artifactPayload });
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

        // 「回執カード」用の集計 — この体験そのものを持ち帰って共有したくなる社会的証明の材料。
        var persona = await LoadPersonaAsync((long)session.initiator_id);
        var artifacts = ExtractArtifacts((string?)session.intent_context_json);
        var startedAt = DateTime.TryParse((string)session.created_at, out var st) ? st : DateTime.UtcNow;
        var durationSeconds = Math.Max(0, (int)(DateTime.UtcNow - startedAt).TotalSeconds);
        var receipt = new
        {
            sessionShortId = $"HS-{sessionId:D6}",
            initiatorName = persona.DisplayName,
            durationSeconds,
            messageCount = history.Count,
            artifactCount = artifacts.Count
        };

        return Json(new { summary, nextActions = nextActionsJson, receipt });
    }

    // ── 5. 自分の AI ID の空き状況チェック（「域名抢注」体験） ────

    // GET /{project}/ahp/check-id/{name}
    [HttpGet("check-id/{name}")]
    [AllowAnonymous]
    public async Task<IActionResult> CheckId(string name)
    {
        var slug = SlugifyAiId(name);
        if (string.IsNullOrEmpty(slug))
            return Json(new { slug = "", aiId = "", available = false });

        var aiId = $"ai://{slug}";
        var count = await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(1) FROM ai_identity WHERE ai_id = @aiId", new { aiId });

        return Json(new { slug, aiId, available = count == 0 });
    }

    // ── 6. 意図から生成された公開資料（Public 権限の一枚資料） ───

    // GET /{project}/ahp/resource/{sessionToken}
    [HttpGet("resource/{sessionToken}")]
    [AllowAnonymous]
    public async Task<IActionResult> Resource(string sessionToken)
    {
        var session = await GetSessionAsync(sessionToken);
        if (session == null)
            return HandshakeError("リンクが無効です", "このリソースリンクは見つかりませんでした。");

        var persona = await LoadPersonaAsync((long)session!.initiator_id);
        var expertise = ParseJsonStringArray(persona.ExpertiseJson);
        var goals = ParseJsonStringArray(persona.GoalsJson);

        string Enc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

        var tagsHtml = expertise.Count > 0
            ? $"<h2>専門分野</h2><div>{string.Join("", expertise.Select(e => $"<span class=\"tag\">{Enc(e)}</span>"))}</div>"
            : "";
        var goalsHtml = goals.Count > 0
            ? $"<h2>関心・ゴール</h2><ul>{string.Join("", goals.Select(g => $"<li>{Enc(g)}</li>"))}</ul>"
            : "";

        var html = $$"""
            <!DOCTYPE html>
            <html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{Enc(persona.DisplayName)}} — 資料</title>
            <style>
            body{font-family:-apple-system,'Segoe UI','Hiragino Sans','Noto Sans JP',sans-serif;background:#f4f5f7;color:#1e293b;margin:0;padding:32px 16px;display:flex;justify-content:center}
            .doc{max-width:520px;width:100%;background:#fff;border-radius:16px;padding:32px;box-shadow:0 10px 30px rgba(0,0,0,.06)}
            h1{font-size:22px;margin:0 0 4px}
            .aiid{font-family:ui-monospace,monospace;color:#4f46e5;font-size:13px;margin-bottom:20px}
            h2{font-size:13px;text-transform:uppercase;letter-spacing:.05em;color:#6366f1;margin:22px 0 8px}
            .tag{display:inline-block;font-size:12px;padding:4px 11px;border-radius:99px;color:#4338ca;background:#eef2ff;border:1px solid #e0e7ff;margin:0 6px 6px 0}
            ul{margin:0;padding-left:18px;line-height:1.8;font-size:14px;color:#475569}
            .foot{margin-top:28px;font-size:11px;color:#94a3b8;text-align:center}
            </style></head>
            <body><div class="doc">
            <h1>{{Enc(persona.DisplayName)}}</h1>
            <div class="aiid">{{Enc(persona.AiId)}}{{(string.IsNullOrEmpty(persona.Organization) ? "" : " ・ " + Enc(persona.Organization))}}</div>
            {{tagsHtml}}
            {{goalsHtml}}
            <div class="foot">この資料は AHP（AI Handshake Protocol）セッションでのリクエストに応じて自動生成されました。</div>
            </div></body></html>
            """;

        return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = 200 };
    }

    // ── 7. Agent Card（A2A の Agent Card 概念を借りた構造化プロフィール） ───
    // ai_profile の *_json 列は今まで BuildAgentPrompt で生の文字列としてプロンプトに埋め込まれるだけだった。
    // ここではそれを一つの整った JSON にまとめ直す。Google/Linux Foundation の A2A プロトコルにある
    // Agent Card（https://a2a-protocol.org/ の "/.well-known/agent.json"）を参考にした簡易版であり、
    // 完全準拠ではない（name/description/provider/capabilities/skills は近い形に寄せたが、
    // goals/sharingPolicy は AHP 独自の拡張フィールド）。
    // 今すぐ federation をやるためのものではなく、将来やるときに「データ構造から作り直す」コストを
    // 今のうちに潰しておくための整理。同库限定の現行仕様は変えていない。

    // GET /{project}/ahp/agent-card/{sessionToken}
    [HttpGet("agent-card/{sessionToken}")]
    [AllowAnonymous]
    public async Task<IActionResult> AgentCard(string sessionToken)
    {
        var session = await GetSessionAsync(sessionToken);
        if (session == null)
            return HandshakeError("リンクが無効です", "このリンクは見つかりませんでした。");

        var persona = await LoadPersonaAsync((long)session!.initiator_id);
        return Json(BuildAgentCard(persona));
    }

    private static object BuildAgentCard(AhpPersona p)
    {
        var expertise = ParseJsonStringArray(p.ExpertiseJson);
        var goals = ParseJsonStringArray(p.GoalsJson);
        var canShare = ParseJsonStringArray(p.CanShareJson);
        var cannotShare = ParseJsonStringArray(p.CannotShareJson);

        return new
        {
            protocolVersion = "ahp-agent-card-lite/1",
            name = p.DisplayName,
            aiId = p.AiId,
            description = !string.IsNullOrWhiteSpace(p.Greeting) ? p.Greeting : (goals.FirstOrDefault() ?? ""),
            provider = new { organization = p.Organization, role = p.Role },
            verified = p.Verified,
            ownerType = p.OwnerType,
            // 今は同库内 AI 同士の会話に閲覧/交渉能力を限定しており、外部からの push 通知等は未実装。
            capabilities = new { streaming = false, pushNotifications = false },
            skills = expertise.Select(e => new { id = SlugifyAiId(e), name = e, tags = new[] { e } }).ToList(),
            // ここから下は A2A 標準の Agent Card にはない AHP 独自の拡張項目。
            goals,
            sharingPolicy = new { canShare, cannotShare }
        };
    }

    // ── ヘルパー ─────────────────────────────────────────

    private async Task<dynamic?> GetSessionAsync(string sessionToken) =>
        await db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM handshake_session WHERE session_token = @sessionToken",
            new { sessionToken });

    private sealed record AhpPersona(
        string DisplayName, string AiId, string? Organization, string? Role,
        string? Greeting, string? GoalsJson, string? CanShareJson,
        string? CannotShareJson, string? ExpertiseJson, string? Instructions,
        string OwnerType, bool Verified);

    private async Task<AhpPersona> LoadPersonaAsync(long aiIdentityId)
    {
        var row = await db.QueryFirstOrDefaultAsync<dynamic>("""
            SELECT i.display_name, i.ai_id, i.organization, i.role, i.owner_type, i.verified,
                   p.greeting_message, p.goals_json, p.can_share_json,
                   p.cannot_share_json, p.expertise_json, p.ai_instructions
            FROM ai_identity i
            LEFT JOIN ai_profile p ON p.ai_identity_id = i.id
            WHERE i.id = @id
            """, new { id = aiIdentityId });

        if (row == null)
            return new AhpPersona("AI アシスタント", "ai://unknown", null, null, null, null, null, null, null, null, "unknown", false);

        return new AhpPersona(
            (string)row.display_name, (string)row.ai_id,
            (string?)row.organization, (string?)row.role,
            (string?)row.greeting_message, (string?)row.goals_json,
            (string?)row.can_share_json, (string?)row.cannot_share_json,
            (string?)row.expertise_json, (string?)row.ai_instructions,
            (string?)row.owner_type ?? "individual", (long)row.verified == 1);
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


    // 訪問者の発言から「資料が欲しい」「会いたい（商談）」の意図を検出する（PoC の簡易キーワード判定）。
    private static (string Type, string Title)? DetectIntentArtifact(string visitorMessage)
    {
        bool Has(params string[] keywords) =>
            keywords.Any(k => visitorMessage.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (Has("資料", "パンフレット", "PDF", "brochure", "document", "资料"))
            return ("resource", "📄 資料リクエストを受け付けました");
        if (Has("会いたい", "商談", "ミーティング", "打ち合わせ", "会議", "meeting", "商谈", "面谈"))
            return ("meeting", "🤝 商談リクエストを受け付けました");
        return null;
    }

    private sealed record ArtifactRecord(string Type, string Title, string? Url, string Id, string CreatedAt);

    // intent_context_json の "artifacts" 配列から既存の生成物一覧を読み出す（同じ種類の重複生成を防ぐため）。
    private static List<ArtifactRecord> ExtractArtifacts(string? intentContextJson)
    {
        if (string.IsNullOrWhiteSpace(intentContextJson)) return [];
        try
        {
            if (JsonNode.Parse(intentContextJson) is not JsonObject obj) return [];
            if (obj["artifacts"] is not JsonArray arr) return [];

            var list = new List<ArtifactRecord>();
            foreach (var item in arr)
            {
                if (item is not JsonObject o) continue;
                list.Add(new ArtifactRecord(
                    (string?)o["type"] ?? "",
                    (string?)o["title"] ?? "",
                    (string?)o["url"],
                    (string?)o["id"] ?? "",
                    (string?)o["createdAt"] ?? ""));
            }
            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // intent_context_json（汎用 JSON 列）の "artifacts" 配列に 1 件だけ安全に追記する。
    private static string AppendArtifact(string? existingJson, ArtifactRecord artifact)
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

        var existingArr = obj["artifacts"] as JsonArray;
        var arr = existingArr ?? [];
        arr.Add(new JsonObject
        {
            ["type"] = artifact.Type,
            ["title"] = artifact.Title,
            ["url"] = artifact.Url,
            ["id"] = artifact.Id,
            ["createdAt"] = artifact.CreatedAt
        });
        if (existingArr == null) obj["artifacts"] = arr;

        return obj.ToJsonString();
    }

    // 名前を "ai://xxx" のスラッグへ変換する（GoDaddy 型の「域名抢注」体験用）。
    // ASCII 英数字以外（例: 日本語のみの名前）で空になった場合は短いハッシュにフォールバックする。
    private static string SlugifyAiId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var sb = new StringBuilder();
        var lastWasDash = false;
        foreach (var ch in raw.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(ch);
                lastWasDash = false;
            }
            else if (!lastWasDash && sb.Length > 0)
            {
                sb.Append('-');
                lastWasDash = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length > 32) slug = slug[..32].TrimEnd('-');
        if (string.IsNullOrEmpty(slug))
            slug = $"guest-{Math.Abs(raw.GetHashCode()) % 10000}";

        return slug;
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

    // intent_context_json（汎用 JSON 列）を安全に JsonObject としてパースする。壊れていれば空オブジェクト。
    private static JsonObject ParseIntentContext(string? json)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(json)
                ? (JsonNode.Parse(json) as JsonObject ?? [])
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task SaveIntentContext(long sessionId, JsonObject ctx)
    {
        await db.ExecuteAsync(
            "UPDATE handshake_session SET intent_context_json = @json, updated_at = @now WHERE id = @id",
            new { id = sessionId, json = ctx.ToJsonString(), now = NowUtc() });
    }

    // ── AI-AI 交渉の状態機械（intent_context_json.negotiation に保持） ──
    // greeting → intent_exchange → (proposal 検出時) pending_confirmation → agreed / rejected
    // ターン上限に達すると stalled（人間の介入待ち）。
    private sealed record NegotiationState(string State, int Turns, string NextSpeaker);

    private static NegotiationState ReadNegotiation(JsonObject ctx)
    {
        if (ctx.TryGetPropertyValue("negotiation", out var node) && node is JsonObject neg)
        {
            var state = neg.TryGetPropertyValue("state", out var s) && s != null ? s.GetValue<string>() : "greeting";
            var turns = neg.TryGetPropertyValue("turns", out var t) && t != null ? t.GetValue<int>() : 0;
            var nextSpeaker = neg.TryGetPropertyValue("next_speaker", out var ns) && ns != null ? ns.GetValue<string>() : "initiator";
            return new NegotiationState(state, turns, nextSpeaker);
        }
        return new NegotiationState("greeting", 0, "initiator");
    }

    private static void WriteNegotiation(JsonObject ctx, NegotiationState neg)
    {
        ctx["negotiation"] = new JsonObject
        {
            ["state"] = neg.State,
            ["turns"] = neg.Turns,
            ["next_speaker"] = neg.NextSpeaker
        };
    }

    private static string AdvanceState(string current) => current == "greeting" ? "intent_exchange" : current;

    private sealed record ProposalRecord(string Id, string Text, string ProposedBy, string State, string CreatedAt);

    // intent_context_json の "proposals" 配列から既存の提案一覧を読み出す。
    private static List<ProposalRecord> GetProposals(JsonObject ctx)
    {
        if (ctx["proposals"] is not JsonArray arr) return [];
        var list = new List<ProposalRecord>();
        foreach (var item in arr)
        {
            if (item is not JsonObject o) continue;
            list.Add(new ProposalRecord(
                (string?)o["id"] ?? "",
                (string?)o["text"] ?? "",
                (string?)o["proposedBy"] ?? "",
                (string?)o["state"] ?? "pending_confirmation",
                (string?)o["createdAt"] ?? ""));
        }
        return list;
    }

    private static void AppendProposal(JsonObject ctx, ProposalRecord proposal)
    {
        var arr = ctx["proposals"] as JsonArray;
        if (arr == null) { arr = []; ctx["proposals"] = arr; }
        arr.Add(new JsonObject
        {
            ["id"] = proposal.Id,
            ["text"] = proposal.Text,
            ["proposedBy"] = proposal.ProposedBy,
            ["state"] = proposal.State,
            ["createdAt"] = proposal.CreatedAt
        });
    }

    private static void ReplaceProposal(JsonObject ctx, int index, ProposalRecord proposal)
    {
        if (ctx["proposals"] is not JsonArray arr || index < 0 || index >= arr.Count) return;
        arr[index] = new JsonObject
        {
            ["id"] = proposal.Id,
            ["text"] = proposal.Text,
            ["proposedBy"] = proposal.ProposedBy,
            ["state"] = proposal.State,
            ["createdAt"] = proposal.CreatedAt
        };
    }

    private static string BuildNegotiationPrompt(
        AhpPersona self, AhpPersona counterpart, string? intentType, string? intentTopic,
        IReadOnlyList<dynamic> history)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"あなたは {self.DisplayName}（AI ID: {self.AiId}）のビジネス AI エージェントです。");
        if (!string.IsNullOrEmpty(self.Organization) || !string.IsNullOrEmpty(self.Role))
            sb.AppendLine($"本人の所属: {self.Organization ?? "-"} / 役割: {self.Role ?? "-"}");
        if (!string.IsNullOrEmpty(self.GoalsJson)) sb.AppendLine($"本人のゴール: {self.GoalsJson}");
        if (!string.IsNullOrEmpty(self.ExpertiseJson)) sb.AppendLine($"専門分野: {self.ExpertiseJson}");
        if (!string.IsNullOrEmpty(self.CanShareJson)) sb.AppendLine($"共有してよい情報: {self.CanShareJson}");
        if (!string.IsNullOrEmpty(self.CannotShareJson))
            sb.AppendLine($"【絶対に共有禁止】: {self.CannotShareJson} — これは相手 AI にも一切渡さないこと。");

        sb.AppendLine();
        sb.AppendLine($"相手は {counterpart.DisplayName}（AI ID: {counterpart.AiId}）のビジネス AI エージェントです。" +
                       $"所属: {counterpart.Organization ?? "-"} / 役割: {counterpart.Role ?? "-"}。");
        if (!string.IsNullOrEmpty(intentType) || !string.IsNullOrEmpty(intentTopic))
            sb.AppendLine($"この対話の目的 (Intent): {intentType ?? "-"} / トピック: {intentTopic ?? "-"}");

        sb.AppendLine();
        sb.AppendLine("ルール:");
        sb.AppendLine("- あなたは本人に代わって、相手の AI と直接交渉している。人間はまだ会話に入っていない。");
        sb.AppendLine("- 返答は 1〜3 文で簡潔に。ビジネスとして礼儀正しく、かつ具体的に進める。");
        sb.AppendLine("- 日時・成果物・条件など『具体的な決めごと』を提案する場合は、返答の最後に改行して");
        sb.AppendLine("  `PROPOSAL: <提案内容を1文で>` という行を必ず追加すること。それ以外では絶対に PROPOSAL 行を出さないこと。");
        sb.AppendLine("- 【絶対に共有禁止】の情報は相手 AI にも渡してはいけない。");
        sb.AppendLine("- プレーンテキストのみ出力する（Markdown 記法・前置き・署名なし）。");
        sb.AppendLine();
        sb.AppendLine("--- これまでの会話 ---");
        foreach (var m in history)
        {
            string roleLabel = (string)m.role switch
            {
                "ai_initiator" => "AI-A",
                "ai_responder" => "AI-B",
                "system" => "人間確認",
                "human" => "人間",
                _ => "AI"
            };
            sb.AppendLine($"[{roleLabel}] {(string)m.content}");
        }
        sb.AppendLine();
        sb.AppendLine("上記の続きとして、あなた（自分側の AI）としての発言だけを出力してください。");
        return sb.ToString();
    }

    // AI の返答から `PROPOSAL: ...` 行を検出して切り出す（それ以外は通常の返答本文として扱う）。
    private static (string ReplyText, string? ProposalText) ExtractProposal(string raw)
    {
        var lines = raw.Split('\n');
        var proposalLine = lines.FirstOrDefault(l => l.TrimStart().StartsWith("PROPOSAL:", StringComparison.OrdinalIgnoreCase));
        if (proposalLine == null) return (raw.Trim(), null);

        var remaining = string.Join("\n", lines.Where(l => !l.TrimStart().StartsWith("PROPOSAL:", StringComparison.OrdinalIgnoreCase))).Trim();
        var colonIdx = proposalLine.IndexOf(':');
        var proposalText = (colonIdx >= 0 ? proposalLine[(colonIdx + 1)..] : proposalLine).Trim();
        if (string.IsNullOrWhiteSpace(remaining)) remaining = "（具体的な提案を作成しました）";
        return (remaining, string.IsNullOrWhiteSpace(proposalText) ? null : proposalText);
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

// AI-AI 自動交渉で生成された「決めごと」の提案。人間が Approve/Reject するまでは pending_confirmation のまま。
public record AhpProposalVm(string Id, string Text, string ProposedBy, string State, string CreatedAt);

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
    public string InitiatorQrUrl { get; set; } = "";
    public string RegisterUrl { get; set; } = "";
    public string? LinkedResponderAiId { get; set; }

    // ── 双方AI自動交渉（同一インスタンス内で相手の ai_identity が解決できた場合のみ有効） ──
    public bool ResponderLinkedLocally { get; set; }
    public string? ResponderDisplayName { get; set; }
    // 相手が「正式登録」ではなく、この場のハンドシェイク中に現場生成された仮の ai_identity（owner_type='guest'）かどうか。
    // 提案カードの信頼度表示（"この提案は情報が少ない仮IDから"）に使う。
    public bool ResponderIsGuest { get; set; }
    public string NegotiationState { get; set; } = "greeting";
    public int NegotiationTurns { get; set; }
    public List<AhpProposalVm> Proposals { get; set; } = [];

    public List<AhpChatMessage> Messages { get; set; } = [];
}
