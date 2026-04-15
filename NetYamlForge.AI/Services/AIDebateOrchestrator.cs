using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Hubs;
using NetYamlForge.AI.Models.Debate;

namespace NetYamlForge.AI.Services;

/// <summary>
/// AI ディベートを制御するオーケストレーターサービス
/// </summary>
public class AIDebateOrchestrator
{
    private const int MaxMessagesPerSide = 11;
    private readonly CLIServiceFactory _cliFactory;
    private readonly AIDebateDbService _db;
    private readonly IHubContext<AIDebateHub> _hub;
    private readonly ILogger<AIDebateOrchestrator> _logger;
    private readonly Dictionary<int, CancellationTokenSource> _activeTasks = new();
    private readonly object _lock = new();

    public AIDebateOrchestrator(
        CLIServiceFactory cliFactory,
        AIDebateDbService db,
        IHubContext<AIDebateHub> hub,
        ILogger<AIDebateOrchestrator> logger)
    {
        _cliFactory = cliFactory;
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    public bool IsRunning(int topicId)
    {
        lock (_lock)
        {
            return _activeTasks.ContainsKey(topicId);
        }
    }

    public async Task<bool> StartDebateAsync(int topicId)
    {
        var topic = await _db.GetTopicAsync(topicId);
        if (topic == null) return false;
        if (topic.Status == "running") return false;

        lock (_lock)
        {
            if (_activeTasks.ContainsKey(topicId)) return false;
            var cts = new CancellationTokenSource();
            _activeTasks[topicId] = cts;
        }

        await _db.UpdateTopicStatusAsync(topicId, "running", 0, 0);
        await BroadcastStatus(topicId, new DebateStatusUpdate { Status = "running", LeftCount = 0, RightCount = 0 });

        _ = Task.Run(async () => await RunDebateLoopAsync(topicId));
        return true;
    }

    public async Task CancelDebateAsync(int topicId)
    {
        CancellationTokenSource? cts = null;
        lock (_lock)
        {
            _activeTasks.TryGetValue(topicId, out cts);
        }
        cts?.Cancel();
        await _db.UpdateTopicStatusAsync(topicId, "cancelled", 0, 0);
        await BroadcastStatus(topicId, new DebateStatusUpdate { Status = "cancelled" });
    }

    private async Task RunDebateLoopAsync(int topicId)
    {
        CancellationToken ct;
        lock (_lock)
        {
            ct = _activeTasks.TryGetValue(topicId, out var cts) ? cts.Token : CancellationToken.None;
        }

        try
        {
            var topic = await _db.GetTopicAsync(topicId);
            if (topic == null) return;

            var leftService = _cliFactory.TryGetService(topic.LeftAi);
            var rightService = _cliFactory.TryGetService(topic.RightAi);

            if (leftService == null || rightService == null)
            {
                _logger.LogError("AI service not found: left={Left}, right={Right}", topic.LeftAi, topic.RightAi);
                await _db.UpdateTopicStatusAsync(topicId, "error", 0, 0);
                await BroadcastStatus(topicId, new DebateStatusUpdate { Status = "error", Error = "AI サービスが見つかりません" });
                return;
            }

            var messages = new List<DebateMessage>();
            int leftCount = 0;
            int rightCount = 0;

            // 最大 MaxMessagesPerSide ラウンド (左右交互)
            for (int round = 1; round <= MaxMessagesPerSide && !ct.IsCancellationRequested; round++)
            {
                bool isFinal = (round == MaxMessagesPerSide);

                // --- 左AIのターン ---
                var leftPrompt = BuildPrompt(topic, "left", messages, round, isFinal);
                var leftSystemPrompt = BuildSystemPrompt(topic, "left");

                _logger.LogInformation("TopicId={TopicId} Left AI turn {Round}/{Max}", topicId, round, MaxMessagesPerSide);
                await BroadcastStatus(topicId, new DebateStatusUpdate { Status = "running", LeftCount = leftCount, RightCount = rightCount });

                string leftResponse;
                try
                {
                    leftResponse = await leftService.ExecuteAsync(
                        leftPrompt,
                        workingDirectory: null,
                        sessionId: null,
                        allowedTools: [],
                        systemPromptOverride: leftSystemPrompt,
                        ct: ct);
                    leftResponse = CleanResponse(leftResponse);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Left AI error in round {Round}", round);
                    leftResponse = $"[エラー: {ex.Message}]";
                }

                leftCount++;
                var leftMsg = await _db.SaveMessageAsync(topicId, "left", topic.LeftAi, leftResponse, leftCount);
                messages.Add(leftMsg);
                await _db.UpdateTopicStatusAsync(topicId, "running", leftCount, rightCount);
                await BroadcastMessage(topicId, leftMsg);
                await BroadcastStatus(topicId, new DebateStatusUpdate { Status = "running", LeftCount = leftCount, RightCount = rightCount });

                if (isFinal || ct.IsCancellationRequested) break;

                // 少し間隔を空ける (API レート制限対策)
                await Task.Delay(500, ct);

                // --- 右AIのターン ---
                var rightPrompt = BuildPrompt(topic, "right", messages, round, isFinal);
                var rightSystemPrompt = BuildSystemPrompt(topic, "right");

                _logger.LogInformation("TopicId={TopicId} Right AI turn {Round}/{Max}", topicId, round, MaxMessagesPerSide);

                string rightResponse;
                try
                {
                    rightResponse = await rightService.ExecuteAsync(
                        rightPrompt,
                        workingDirectory: null,
                        sessionId: null,
                        allowedTools: [],
                        systemPromptOverride: rightSystemPrompt,
                        ct: ct);
                    rightResponse = CleanResponse(rightResponse);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Right AI error in round {Round}", round);
                    rightResponse = $"[エラー: {ex.Message}]";
                }

                rightCount++;
                var rightMsg = await _db.SaveMessageAsync(topicId, "right", topic.RightAi, rightResponse, rightCount);
                messages.Add(rightMsg);
                await _db.UpdateTopicStatusAsync(topicId, "running", leftCount, rightCount);
                await BroadcastMessage(topicId, rightMsg);
                await BroadcastStatus(topicId, new DebateStatusUpdate { Status = "running", LeftCount = leftCount, RightCount = rightCount });

                await Task.Delay(500, ct);
            }

            if (!ct.IsCancellationRequested)
            {
                await _db.UpdateTopicStatusAsync(topicId, "completed", leftCount, rightCount);
                await BroadcastStatus(topicId, new DebateStatusUpdate { Status = "completed", LeftCount = leftCount, RightCount = rightCount });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Debate loop failed for topicId={TopicId}", topicId);
            await _db.UpdateTopicStatusAsync(topicId, "error", 0, 0);
            await BroadcastStatus(topicId, new DebateStatusUpdate { Status = "error", Error = ex.Message });
        }
        finally
        {
            lock (_lock)
            {
                if (_activeTasks.TryGetValue(topicId, out var cts))
                {
                    cts.Dispose();
                    _activeTasks.Remove(topicId);
                }
            }
        }
    }

    private static string BuildSystemPrompt(DebateTopic topic, string side)
    {
        var role = side == "left" ? topic.LeftRole : topic.RightRole;
        var aiName = side == "left" ? topic.LeftAi : topic.RightAi;
        var roleText = string.IsNullOrWhiteSpace(role)
            ? $"あなたは {aiName} AI として、論理的かつ建設的な議論を行います。"
            : role;

        return $@"あなたはAIディベートの参加者です。
役割: {roleText}
議論のルール:
- 論理的かつ建設的な意見を述べてください
- 相手の意見を尊重しながら、自分の立場を明確にしてください
- 回答は200〜400字程度にまとめてください
- ツールは使用せず、テキストのみで回答してください";
    }

    private static string BuildPrompt(DebateTopic topic, string side, List<DebateMessage> history, int round, bool isFinal)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"【議論テーマ】{topic.Title}");
        if (!string.IsNullOrWhiteSpace(topic.Description))
            sb.AppendLine($"【詳細】{topic.Description}");
        sb.AppendLine();

        if (history.Count > 0)
        {
            sb.AppendLine("【これまでの議論】");
            foreach (var m in history)
            {
                var sideLabel = m.AiSide == "left" ? $"左({m.AiProvider})" : $"右({m.AiProvider})";
                sb.AppendLine($"[{sideLabel} 第{m.MessageIndex}発言]");
                sb.AppendLine(m.Content);
                sb.AppendLine();
            }
        }

        if (isFinal)
        {
            sb.AppendLine($"【指示】これはあなたの最終発言（{round}/{MaxMessagesPerSide}）です。");
            sb.AppendLine("これまでの議論を総括し、相互の合意点・相違点を整理した上で、あなたの最終的な見解をまとめてください。");
        }
        else if (round == 1 && side == "left")
        {
            sb.AppendLine("【指示】このテーマについてあなたの意見・見解を述べてください。");
        }
        else
        {
            var lastMsg = history.LastOrDefault();
            if (lastMsg != null)
            {
                var opponentSide = lastMsg.AiSide == "left" ? "左" : "右";
                sb.AppendLine($"【指示】上記の{opponentSide}AIの発言に対して、あなたの意見を述べてください（第{round}ラウンド）。");
            }
        }

        return sb.ToString();
    }

    private static string CleanResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "(応答なし)";

        var trimmed = raw.Trim();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
        {
            // JSON でない場合はそのまま返す（長すぎる場合は切り詰め）
            return trimmed.Length > 2000 ? trimmed[..2000] + "..." : trimmed;
        }

        // NDJSON（stream-json）形式の検出：Claude/Qwen CLI が複数行の JSON イベントを出力する
        var lines = trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 1)
        {
            string? resultText = null;
            var textParts = new List<string>();

            foreach (var line in lines)
            {
                var trimLine = line.Trim();
                if (!trimLine.StartsWith('{')) continue;
                try
                {
                    using var lineDoc = JsonDocument.Parse(trimLine);
                    var lineRoot = lineDoc.RootElement;

                    if (!lineRoot.TryGetProperty("type", out var typeEl)) continue;
                    var eventType = typeEl.GetString();

                    // { "type": "result", "result": "..." } — 最終結果イベント（最優先）
                    if (eventType == "result" &&
                        lineRoot.TryGetProperty("result", out var resultEl) &&
                        resultEl.ValueKind == JsonValueKind.String)
                    {
                        var t = resultEl.GetString();
                        if (!string.IsNullOrWhiteSpace(t)) resultText = t;
                    }

                    // { "type": "assistant", "message": { "content": [{ "type": "text", "text": "..." }] } }
                    if (eventType == "assistant" &&
                        lineRoot.TryGetProperty("message", out var msgEl) &&
                        msgEl.TryGetProperty("content", out var contentArr) &&
                        contentArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in contentArr.EnumerateArray())
                        {
                            if (item.TryGetProperty("type", out var t) && t.GetString() == "text" &&
                                item.TryGetProperty("text", out var txt))
                            {
                                var s = txt.GetString();
                                if (!string.IsNullOrWhiteSpace(s)) textParts.Add(s);
                            }
                        }
                    }
                }
                catch (JsonException) { }
            }

            if (resultText != null) return resultText;
            if (textParts.Count > 0) return string.Join("\n", textParts);
        }

        // 単一 JSON オブジェクト形式
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            // { "type": "result", "result": "...", ... } 形式（Claude / Qwen JSON output）
            if (root.TryGetProperty("result", out var resultProp) &&
                resultProp.ValueKind == JsonValueKind.String)
            {
                var text = resultProp.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }

            // { "text": "..." } 形式のフォールバック
            if (root.TryGetProperty("text", out var textProp) &&
                textProp.ValueKind == JsonValueKind.String)
            {
                var text = textProp.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }

            // content 配列形式のフォールバック（一部プロバイダー）
            if (root.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var item in content.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var t) && t.GetString() == "text" &&
                        item.TryGetProperty("text", out var txt))
                    {
                        var s = txt.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) parts.Add(s);
                    }
                }
                if (parts.Count > 0) return string.Join("\n", parts);
            }

            // その他のフィールド（message/response/content 文字列）
            if (root.TryGetProperty("message", out var messageProp) &&
                messageProp.ValueKind == JsonValueKind.String)
            {
                var text = messageProp.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }

            if (root.TryGetProperty("response", out var responseProp) &&
                responseProp.ValueKind == JsonValueKind.String)
            {
                var text = responseProp.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }

            if (root.TryGetProperty("content", out var contentProp) &&
                contentProp.ValueKind == JsonValueKind.String)
            {
                var text = contentProp.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }
        catch (JsonException)
        {
            // JSON パース失敗時は生テキストをそのまま返す
        }

        // 長すぎる場合は切り詰め
        return raw.Length > 2000 ? raw[..2000] + "..." : raw;
    }

    private async Task BroadcastMessage(int topicId, DebateMessage msg)
    {
        await _hub.Clients.Group($"debate_{topicId}").SendAsync("NewMessage", new
        {
            id = msg.Id,
            topicId = msg.TopicId,
            aiSide = msg.AiSide,
            aiProvider = msg.AiProvider,
            content = msg.Content,
            messageIndex = msg.MessageIndex,
            createdAt = msg.CreatedAt
        });
    }

    private async Task BroadcastStatus(int topicId, DebateStatusUpdate update)
    {
        await _hub.Clients.Group($"debate_{topicId}").SendAsync("StatusUpdate", new
        {
            status = update.Status,
            leftCount = update.LeftCount,
            rightCount = update.RightCount,
            error = update.Error
        });
    }
}
