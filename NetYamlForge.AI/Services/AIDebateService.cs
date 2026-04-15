// DCS003 抑制理由: AIDebateService は全プロジェクト共通の system.db を使うため
// ProjectScope とは独立した接続を直接生成します。
#pragma warning disable DCS003

using Dapper;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Hubs;
using NetYamlForge.AI.Models;

namespace NetYamlForge.AI.Services;

/// <summary>
/// 2つの AI CLI が指定テーマについて対話するディベートサービス。
/// セッション・メッセージを system.db に保存し、SignalR でリアルタイム配信します。
/// </summary>
public class AIDebateService
{
    private readonly string _connectionString;
    private readonly CLIServiceFactory _cliFactory;
    private readonly IHubContext<AIDebateHub> _hubContext;
    private readonly ILogger<AIDebateService> _logger;
    private const int MaxMessagesPerSide = 11;

    public AIDebateService(
        IConfiguration config,
        CLIServiceFactory cliFactory,
        IHubContext<AIDebateHub> hubContext,
        ILogger<AIDebateService> logger)
    {
        var dbPath = config["ChatHistory:DbPath"] ?? "system.db";
        _connectionString = $"Data Source={dbPath}";
        _cliFactory = cliFactory;
        _hubContext = hubContext;
        _logger = logger;
        InitializeSchema();
    }

    // ──────────────────────────────────────────────
    // スキーマ初期化
    // ──────────────────────────────────────────────

    private void InitializeSchema()
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            conn.Execute(@"
CREATE TABLE IF NOT EXISTS AIDebateSessions (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    Topic         TEXT NOT NULL,
    LeftProvider  TEXT NOT NULL DEFAULT 'claude',
    RightProvider TEXT NOT NULL DEFAULT 'qwen',
    LeftRole      TEXT NOT NULL DEFAULT 'Advocate',
    RightRole     TEXT NOT NULL DEFAULT 'Critic',
    Status        TEXT NOT NULL DEFAULT 'pending',
    CreatedBy     TEXT,
    CreatedAt     TEXT NOT NULL,
    UpdatedAt     TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_debate_sessions_status ON AIDebateSessions(Status);

CREATE TABLE IF NOT EXISTS AIDebateMessages (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId     INTEGER NOT NULL,
    Side          TEXT NOT NULL,
    Provider      TEXT NOT NULL,
    Role          TEXT NOT NULL DEFAULT '',
    Content       TEXT NOT NULL,
    MessageNumber INTEGER NOT NULL,
    CreatedAt     TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_debate_messages_session ON AIDebateMessages(SessionId, Id);");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AIDebate スキーマ初期化に失敗しました");
        }
    }

    // ──────────────────────────────────────────────
    // セッション CRUD
    // ──────────────────────────────────────────────

    public async Task<DebateSession> CreateSessionAsync(CreateDebateRequest request, string? userId)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await using var conn = new SqliteConnection(_connectionString);
        var id = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO AIDebateSessions (Topic, LeftProvider, RightProvider, LeftRole, RightRole, Status, CreatedBy, CreatedAt, UpdatedAt)
VALUES (@Topic, @LeftProvider, @RightProvider, @LeftRole, @RightRole, 'pending', @CreatedBy, @CreatedAt, @UpdatedAt);
SELECT last_insert_rowid();",
            new
            {
                request.Topic,
                request.LeftProvider,
                request.RightProvider,
                request.LeftRole,
                request.RightRole,
                CreatedBy = userId,
                CreatedAt = now,
                UpdatedAt = now
            });

        return (await GetSessionAsync(id))!;
    }

    public async Task<DebateSession?> GetSessionAsync(long id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        return await conn.QueryFirstOrDefaultAsync<DebateSession>(
            "SELECT * FROM AIDebateSessions WHERE Id = @Id", new { Id = id });
    }

    public async Task<IEnumerable<DebateSession>> GetRecentSessionsAsync(int limit = 30)
    {
        await using var conn = new SqliteConnection(_connectionString);
        return await conn.QueryAsync<DebateSession>(
            "SELECT * FROM AIDebateSessions ORDER BY Id DESC LIMIT @Limit", new { Limit = limit });
    }

    public async Task<IEnumerable<DebateMessage>> GetMessagesAsync(long sessionId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        return await conn.QueryAsync<DebateMessage>(
            "SELECT * FROM AIDebateMessages WHERE SessionId = @SessionId ORDER BY Id", new { SessionId = sessionId });
    }

    // ──────────────────────────────────────────────
    // ディベート実行
    // ──────────────────────────────────────────────

    /// <summary>バックグラウンドでディベートを開始します。</summary>
    public Task StartDebateAsync(long sessionId, CancellationToken ct = default)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var session = await GetSessionAsync(sessionId);
                if (session == null || session.Status != "pending")
                {
                    _logger.LogWarning("ディベートセッション {Id} が見つからないか pending 状態ではありません", sessionId);
                    return;
                }

                await SetStatusAsync(sessionId, "active");
                await _hubContext.Clients.Group($"debate-{sessionId}")
                    .SendAsync("DebateStatusChanged", new DebateStatusEvent
                    {
                        SessionId = sessionId,
                        Status = "active",
                        Message = "ディベートを開始します"
                    }, ct);

                await RunDebateExchangeAsync(sessionId, session, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("ディベートセッション {Id} がキャンセルされました", sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ディベートセッション {Id} でエラーが発生しました", sessionId);
                await SetStatusAsync(sessionId, "error");
                await _hubContext.Clients.Group($"debate-{sessionId}")
                    .SendAsync("DebateStatusChanged", new DebateStatusEvent
                    {
                        SessionId = sessionId,
                        Status = "error",
                        Message = $"エラーが発生しました: {ex.Message}"
                    });
            }
        }, ct);

        return Task.CompletedTask;
    }

    private async Task RunDebateExchangeAsync(long sessionId, DebateSession session, CancellationToken ct)
    {
        var leftMessages = new List<(string side, string role, string content)>();
        var rightMessages = new List<(string side, string role, string content)>();

        string? lastRightContent = null;

        for (int round = 1; round <= MaxMessagesPerSide; round++)
        {
            ct.ThrowIfCancellationRequested();

            bool isFinalMessage = round == MaxMessagesPerSide;

            // ── 左 AI のメッセージ ──
            string leftPrompt = BuildPrompt(
                session.Topic,
                session.LeftRole,
                session.RightRole,
                "left",
                round,
                lastRightContent,
                leftMessages,
                rightMessages,
                isFinalMessage);

            string leftSystemPrompt = BuildSystemPrompt(session.LeftRole, session.RightRole, session.Topic);

            var leftMsg = await SendToAIAsync(
                sessionId, "left", session.LeftProvider, session.LeftRole,
                leftSystemPrompt, leftPrompt, round, isFinalMessage, ct);

            leftMessages.Add(("left", session.LeftRole, leftMsg.Content));

            // ── 右 AI のメッセージ ──
            ct.ThrowIfCancellationRequested();

            string rightPrompt = BuildPrompt(
                session.Topic,
                session.RightRole,
                session.LeftRole,
                "right",
                round,
                leftMsg.Content,
                rightMessages,
                leftMessages,
                isFinalMessage);

            string rightSystemPrompt = BuildSystemPrompt(session.RightRole, session.LeftRole, session.Topic);

            var rightMsg = await SendToAIAsync(
                sessionId, "right", session.RightProvider, session.RightRole,
                rightSystemPrompt, rightPrompt, round, isFinalMessage, ct);

            rightMessages.Add(("right", session.RightRole, rightMsg.Content));
            lastRightContent = rightMsg.Content;
        }

        await SetStatusAsync(sessionId, "completed");
        await _hubContext.Clients.Group($"debate-{sessionId}")
            .SendAsync("DebateStatusChanged", new DebateStatusEvent
            {
                SessionId = sessionId,
                Status = "completed",
                Message = "ディベートが完了しました"
            });
    }

    private static string BuildSystemPrompt(string myRole, string opponentRole, string topic)
    {
        return $@"あなたは構造化 AI ディベートの参加者です。
あなたの役割: {myRole}
相手の役割: {opponentRole}
討議テーマ: {topic}

【重要なルール】
- あなたは「{myRole}」として一貫した立場を保ちながら発言してください。
- 相手の主張を踏まえた上で、論理的かつ簡潔に反論・意見を述べてください。
- ツールは一切使用しないでください。これは純粋な対話ディベートです。
- 出力は日本語で、1〜3段落以内の簡潔な文章にしてください。";
    }

    private static string BuildPrompt(
        string topic,
        string myRole,
        string opponentRole,
        string side,
        int round,
        string? lastOpponentContent,
        List<(string side, string role, string content)> myHistory,
        List<(string side, string role, string content)> opponentHistory,
        bool isFinal)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"【討議テーマ】{topic}");
        sb.AppendLine($"【あなたの役割】{myRole}");
        sb.AppendLine($"【相手の役割】{opponentRole}");
        sb.AppendLine();

        // 過去の会話を渡す（直近10件まで、左右交互に）
        int totalPrev = myHistory.Count + opponentHistory.Count;
        if (totalPrev > 0)
        {
            sb.AppendLine("【これまでの対話（直近の部分）】");
            // 左右交互に並べた全履歴リスト（タプル要素に名前付きアクセス）
            var combined = new List<(string role, string content)>();
            int maxLen = Math.Max(myHistory.Count, opponentHistory.Count);
            for (int idx = 0; idx < maxLen; idx++)
            {
                if (idx < opponentHistory.Count)
                {
                    var (_, oRole, oContent) = opponentHistory[idx];
                    combined.Add((oRole, oContent));
                }
                if (idx < myHistory.Count)
                {
                    var (_, mRole, mContent) = myHistory[idx];
                    combined.Add((mRole, mContent));
                }
            }
            int skip = Math.Max(0, combined.Count - 10);
            foreach (var h in combined.Skip(skip))
                sb.AppendLine($"[{h.role}]: {Truncate(h.content, 300)}");
            sb.AppendLine();
        }

        if (lastOpponentContent != null)
        {
            sb.AppendLine($"【相手({opponentRole})の最新発言】");
            sb.AppendLine(lastOpponentContent);
            sb.AppendLine();
        }

        if (round == 1 && lastOpponentContent == null)
        {
            sb.AppendLine("ディベートを開始します。まず「{myRole}」としてこのテーマに対する立場と最初の主張を述べてください。"
                .Replace("{myRole}", myRole));
        }
        else if (isFinal)
        {
            sb.AppendLine($"これはあなたの最後の発言（第{round}回）です。");
            sb.AppendLine("「{myRole}」としての立場を総括し、ディベートで明らかになった合意点・対立点をまとめてください。"
                .Replace("{myRole}", myRole));
        }
        else
        {
            sb.AppendLine($"第{round}回の発言です。相手の意見に対して「{myRole}」として応答してください。");
        }

        return sb.ToString();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private async Task<DebateMessage> SendToAIAsync(
        long sessionId,
        string side,
        string provider,
        string role,
        string systemPrompt,
        string userMessage,
        int messageNumber,
        bool isFinal,
        CancellationToken ct)
    {
        string content;
        try
        {
            var svc = _cliFactory.TryGetService(provider);
            if (svc == null)
            {
                content = $"[エラー: プロバイダー '{provider}' が見つかりません]";
            }
            else
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(120));

                content = await svc.ExecuteAsync(
                    message: userMessage,
                    workingDirectory: null,
                    sessionId: null,
                    allowedTools: new List<string>(),
                    systemPromptOverride: systemPrompt,
                    ct: cts.Token);

                if (string.IsNullOrWhiteSpace(content))
                    content = "[応答なし]";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI {Provider} への送信に失敗しました (session={SessionId}, round={Round})",
                provider, sessionId, messageNumber);
            content = $"[エラー: {ex.Message}]";
        }

        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await using var conn = new SqliteConnection(_connectionString);
        var id = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO AIDebateMessages (SessionId, Side, Provider, Role, Content, MessageNumber, CreatedAt)
VALUES (@SessionId, @Side, @Provider, @Role, @Content, @MessageNumber, @CreatedAt);
SELECT last_insert_rowid();",
            new { SessionId = sessionId, Side = side, Provider = provider, Role = role,
                  Content = content, MessageNumber = messageNumber, CreatedAt = now });

        var msg = new DebateMessage
        {
            Id = id, SessionId = sessionId, Side = side, Provider = provider,
            Role = role, Content = content, MessageNumber = messageNumber, CreatedAt = now
        };

        // SignalR でリアルタイム配信
        await _hubContext.Clients.Group($"debate-{sessionId}")
            .SendAsync("DebateMessageReceived", new DebateMessageEvent
            {
                SessionId = sessionId,
                MessageId = id,
                Side = side,
                Provider = provider,
                Role = role,
                Content = content,
                MessageNumber = messageNumber,
                CreatedAt = now,
                IsFinal = isFinal
            });

        return msg;
    }

    private async Task SetStatusAsync(long sessionId, string status)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            "UPDATE AIDebateSessions SET Status = @Status, UpdatedAt = @Now WHERE Id = @Id",
            new { Status = status, Now = now, Id = sessionId });
    }
}
