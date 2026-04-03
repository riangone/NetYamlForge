using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TaskStatus = NetYamlForge.Models.AI.TaskStatus;
using NetYamlForge.Models.AI;
using Microsoft.Extensions.Options;

namespace NetYamlForge.Services.AI;

/// <summary>
/// CLI 服务基类
/// </summary>
public abstract class BaseCLIService : ICLIService
{
    protected readonly ProcessExecutor Executor;
    protected readonly CliConfig Config;
    protected readonly ILogger Logger;
    protected readonly string _toolName;
    protected readonly SkillLoader SkillLoader;

    protected BaseCLIService(
        ProcessExecutor executor,
        IOptions<CliConfig> config,
        SkillLoader skillLoader,
        ILogger logger,
        string toolName)
    {
        Executor = executor;
        Config = config.Value;
        SkillLoader = skillLoader;
        Logger = logger;
        _toolName = toolName;
    }

    public virtual string ToolName => _toolName;

    /// <summary>
    /// 実際に起動するコマンドパス。
    /// サブクラスで設定ファイルのパスを返すようオーバーライドできる。
    /// デフォルトは ToolName（PATH から検索）。
    /// </summary>
    protected virtual string CommandPath => ToolName;

    /// <summary>
    /// プロセス起動時に設定する環境変数。
    /// サブクラスで API キー等を返すようオーバーライドできる。
    /// </summary>
    protected virtual IReadOnlyDictionary<string, string>? GetEnvironmentVariables() => null;

    public abstract Task<CliToolInfo> GetToolInfoAsync(CancellationToken ct = default);

    public async IAsyncEnumerable<ProgressUpdate> ExecuteStreamingAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        string? systemPromptOverride = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var argList = BuildArgumentList(message, true, sessionId, allowedTools, systemPromptOverride);
        var workingDir = workingDirectory ?? Config.DefaultWorkingDirectory;

        await foreach (var line in Executor.ExecuteStreamingAsync(CommandPath, argList, workingDir, GetEnvironmentVariables(), ct))
        {
            // 解析 stream-json 输出
            var update = ParseStreamLine(line);
            if (update != null)
            {
                yield return update;
            }
        }
    }

    public async Task<string> ExecuteAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        string? systemPromptOverride = null,
        CancellationToken ct = default)
    {
        var argList = BuildArgumentList(message, false, sessionId, allowedTools, systemPromptOverride);
        var workingDir = workingDirectory ?? Config.DefaultWorkingDirectory;

        Logger.LogInformation("[CLI执行] 開始：Command={Command}, Args={Args}, WorkingDir={WorkingDir}", 
            CommandPath, string.Join(" ", argList), workingDir);

        var result = await Executor.ExecuteAsync(CommandPath, argList, workingDir, GetEnvironmentVariables(), ct);

        Logger.LogInformation("[CLI执行] 完了：ExitCode={ExitCode}, OutputLength={OutputLength}, ErrorLength={ErrorLength}", 
            result.ExitCode, result.Output?.Length ?? 0, result.Error?.Length ?? 0);

        if (result.ExitCode != 0)
        {
            Logger.LogError("[CLI执行] エラー：ExitCode={ExitCode}, Error={Error}, Output={Output}", 
                result.ExitCode, result.Error, result.Output?.Substring(0, Math.Min(200, result.Output?.Length ?? 0)));
            throw new InvalidOperationException($"CLI failed: {result.Error}");
        }

        return ExtractTextFromOutput(result.Output);
    }

    /// <summary>
    /// CLI の非ストリーミング出力からテキストを抽出します。
    /// JSON 形式（--output-format json）の場合は result/text フィールドを取り出し、
    /// 生テキストの場合はそのまま返します。
    /// </summary>
    protected virtual string ExtractTextFromOutput(string raw)
    {
        Logger.LogInformation("[ExtractTextFromOutput] 生出力長：{Length}", raw?.Length ?? 0);
        
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        var trimmed = raw.Trim();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('[')) return raw;

        // Qwen CLI 返回 JSON 数组格式：[{...}, {...}, {...}]
        if (trimmed.StartsWith('['))
        {
            try
            {
                using var arrDoc = JsonDocument.Parse(trimmed);
                var arr = arrDoc.RootElement;
                if (arr.GetArrayLength() > 0)
                {
                    string? resultText = null;
                    var textParts = new List<string>();

                    foreach (var item in arr.EnumerateArray())
                    {
                        if (!item.TryGetProperty("type", out var typeEl)) continue;
                        var eventType = typeEl.GetString();

                        // { "type": "result", "result": "..." } — 最終結果イベント（最優先）
                        if (eventType == "result" &&
                            item.TryGetProperty("result", out var resultEl) &&
                            resultEl.ValueKind == JsonValueKind.String)
                        {
                            var t = resultEl.GetString();
                            if (!string.IsNullOrWhiteSpace(t)) return t;  // 立即返回 result
                        }

                        // { "type": "assistant", "message": { "content": [{ "type": "text", "text": "..." }] } }
                        if (eventType == "assistant" &&
                            item.TryGetProperty("message", out var msgEl) &&
                            msgEl.TryGetProperty("content", out var contentArr) &&
                            contentArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var c in contentArr.EnumerateArray())
                            {
                                if (c.TryGetProperty("type", out var ct) && ct.GetString() == "text" &&
                                    c.TryGetProperty("text", out var txt))
                                {
                                    var s = txt.GetString();
                                    if (!string.IsNullOrWhiteSpace(s)) textParts.Add(s);
                                }
                            }
                        }
                    }

                    if (resultText != null) return resultText;
                    if (textParts.Count > 0)
                    {
                        var combined = string.Join("\n", textParts);
                        Logger.LogInformation("[ExtractTextFromOutput] 抽出テキスト長：{Length}, 先頭 100 文字：{Preview}", combined.Length, combined.Length > 100 ? combined[..100] : combined);
                        return combined;
                    }
                }
            }
            catch (JsonException) { }
            // JSON 数组解析失败，继续下面的 NDJSON 处理
        }

        // NDJSON（stream-json）形式の検出: Claude/Qwen CLI が複数行の JSON イベントを出力する
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

            if (resultText != null)
            {
                Logger.LogInformation("[ExtractTextFromOutput] NDJSON: resultText 長={Length}", resultText.Length);
                return resultText;
            }
            if (textParts.Count > 0)
            {
                var combined = string.Join("\n", textParts);
                Logger.LogInformation("[ExtractTextFromOutput] NDJSON: textParts 長={Count}, 結合長={Length}", textParts.Count, combined.Length);
                return combined;
            }
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
                if (!string.IsNullOrWhiteSpace(text))
                {
                    Logger.LogInformation("[ExtractTextFromOutput] 単一 JSON: result 長={Length}", text.Length);
                    return text;
                }
            }

            // { "text": "..." } 形式のフォールバック
            if (root.TryGetProperty("text", out var textProp) &&
                textProp.ValueKind == JsonValueKind.String)
            {
                var text = textProp.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    Logger.LogInformation("[ExtractTextFromOutput] 単一 JSON: text 長={Length}", text.Length);
                    return text;
                }
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
        }
        catch (JsonException)
        {
            // JSON パース失敗時は生テキストをそのまま返す
        }

        return raw;
    }

    public async Task CancelAsync(int processId, CancellationToken ct = default)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(ct);
                Logger.LogInformation("CLI process {Pid} killed", processId);
            }
        }
        catch (ArgumentException)
        {
            // 进程已不存在
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to kill CLI process {Pid}", processId);
        }
    }

    /// <summary>
    /// CLI 引数リストを構築する（サブクラスで実装）。
    /// ArgumentList に直接渡すため、引用符エスケープ不要。
    /// systemPromptOverride が指定された場合、デフォルトのシステムプロンプトを完全に置き換える。
    /// </summary>
    protected abstract List<string> BuildArgumentList(
        string message,
        bool streaming,
        string? sessionId,
        List<string>? allowedTools,
        string? systemPromptOverride = null);

    /// <summary>
    /// 解析流式输出行。返回 null 时调用方跳过该行（不产生日志）。
    /// </summary>
    protected virtual ProgressUpdate? ParseStreamLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        try
        {
            var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
            {
                // 没有 type 字段的 JSON，尝试作为 assistant 消息解析（兼容旧格式）
                return ParseAssistantMessage(root);
            }

            return typeProp.GetString() switch
            {
                "result" => ParseResultMessage(root),
                "assistant" => ParseAssistantMessage(root),
                "system" => ParseSystemMessage(root),
                "progress" => ParseProgressMessage(root),
                "error" => ParseErrorMessage(root),
                // user / rate_limit_event / その他 → 尝试解析 content 字段
                _ => ParseUnknownTypeMessage(root)
            };
        }
        catch (JsonException ex)
        {
            // 非 JSON テキストはそのままログに出力（CLI の補助メッセージ等）
            Logger.LogDebug(ex, "Non-JSON line received: {Line}", line);
            return new ProgressUpdate { Logs = new() { line }, Status = TaskStatus.Running };
        }
    }

    /// <summary>
    /// 解析未知类型的 JSON 消息（如 user、rate_limit_event 等）
    /// 尝试提取 content 字段作为消息内容
    /// </summary>
    protected virtual ProgressUpdate? ParseUnknownTypeMessage(JsonElement root)
    {
        // 尝试从 content 字段提取文本
        if (root.TryGetProperty("content", out var content))
        {
            if (content.ValueKind == JsonValueKind.String)
            {
                var text = content.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return new ProgressUpdate { Message = text, Logs = new() { text }, Status = TaskStatus.Running };
                }
            }
            else if (content.ValueKind == JsonValueKind.Array)
            {
                // 尝试解析 content 数组（类似 assistant 消息）
                return ParseContentArray(content);
            }
        }

        // 尝试从 text 字段提取
        if (root.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
        {
            var text = textProp.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return new ProgressUpdate { Message = text, Logs = new() { text }, Status = TaskStatus.Running };
            }
        }

        // 尝试从 message 字段提取
        if (root.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
        {
            var text = msgProp.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return new ProgressUpdate { Message = text, Logs = new() { text }, Status = TaskStatus.Running };
            }
        }

        // 无法提取有用信息，返回 null 跳过
        return null;
    }

    /// <summary>
    /// result メッセージを解析。
    /// result フィールドに実際の応答テキストが含まれる場合（Claude Code 等）は Message にセットする。
    /// TaskQueueService 側で assistant メッセージの蓄積テキストを優先し、
    /// 取得できなかった場合のフォールバックとして利用される。
    /// </summary>
    protected static ProgressUpdate ParseResultMessage(JsonElement root)
    {
        // result フィールドを読み取る（Claude Code は実際の応答テキストを含む場合がある）
        string? text = null;
        if (root.TryGetProperty("result", out var r))
            text = r.ValueKind == JsonValueKind.String ? r.GetString() : null;

        string? sessionId = null;
        if (root.TryGetProperty("session_id", out var sid) && sid.ValueKind == JsonValueKind.String)
            sessionId = sid.GetString();

        return new ProgressUpdate { Message = text, Progress = 100, Status = TaskStatus.Completed, SessionId = sessionId };
    }

    protected static ProgressUpdate ParseProgressMessage(JsonElement root)
    {
        return new ProgressUpdate
        {
            Message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null,
            Progress = root.TryGetProperty("percentage", out var p) ? p.GetInt32() : 50,
            Status = TaskStatus.Running
        };
    }

    protected static ProgressUpdate ParseErrorMessage(JsonElement root)
    {
        // error フィールドは String または Object（{"type":"...","message":"..."}）の場合がある
        string? errorMsg = null;
        if (root.TryGetProperty("error", out var e))
        {
            if (e.ValueKind == JsonValueKind.String)
                errorMsg = e.GetString();
            else if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var em) && em.ValueKind == JsonValueKind.String)
                errorMsg = em.GetString();
        }

        return new ProgressUpdate
        {
            Message = errorMsg ?? "Unknown error",
            Status = TaskStatus.Failed
        };
    }

    /// <summary>
    /// assistant メッセージを解析し、text/tool_use のみ人間が読めるログとして返す。
    /// thinking ブロックや不明なコンテンツは無視する（生 JSON を出力しない）。
    /// Claude Code / Qwen Code 共通形式：message.content 配列
    /// </summary>
    private static ProgressUpdate ParseAssistantMessage(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var msg)) return new ProgressUpdate { Status = TaskStatus.Running };
        if (!msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return new ProgressUpdate { Status = TaskStatus.Running };

        return ParseContentArray(content);
    }

    /// <summary>
    /// content 配列を解析
    /// </summary>
    private static ProgressUpdate ParseContentArray(JsonElement content)
    {
        var textParts = new List<string>();
        var toolLogs = new List<string>();

        foreach (var item in content.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var itemType)) continue;

            switch (itemType.GetString())
            {
                case "text":
                    if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        var txt = text.GetString();
                        if (!string.IsNullOrWhiteSpace(txt))
                            textParts.Add(txt);
                    }
                    break;

                case "tool_use":
                    var toolName = item.TryGetProperty("name", out var n) ? n.GetString() ?? "tool" : "tool";
                    var hint = item.TryGetProperty("input", out var inp) ? GetToolInputHint(toolName, inp) : "";
                    toolLogs.Add($"🔧 {toolName}{hint}");
                    break;

                // "thinking" → intentionally skipped (no log)
            }
        }

        // テキストメッセージがある場合は Message に設定
        var combinedText = textParts.Count > 0 ? string.Join("\n", textParts) : null;

        // ツールログとテキストメッセージを両方返す（テキストを優先）
        if (combinedText != null)
        {
            return new ProgressUpdate { Message = combinedText, Logs = new List<string> { combinedText }, Status = TaskStatus.Running };
        }
        else if (toolLogs.Count > 0)
        {
            return new ProgressUpdate { Logs = toolLogs, Status = TaskStatus.Running };
        }

        return new ProgressUpdate { Status = TaskStatus.Running };
    }

    protected static ProgressUpdate ParseSystemMessage(JsonElement root)
    {
        var parts = new List<string>();
        if (root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String) parts.Add($"model: {model.GetString()}");
        if (root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array) parts.Add($"tools: {tools.GetArrayLength()}");
        if (root.TryGetProperty("session_id", out var sid) && sid.ValueKind == JsonValueKind.String) { var s = sid.GetString(); if (s != null) parts.Add($"session: {s[..Math.Min(8, s.Length)]}…"); }

        var summary = parts.Count > 0 ? string.Join(", ", parts) : "initialized";
        return new ProgressUpdate { Logs = new() { $"⚙ {summary}" }, Status = TaskStatus.Running };
    }

    private static string GetToolInputHint(string toolName, JsonElement input)
    {
        try
        {
            // input 必须是对象类型才能提取属性
            if (input.ValueKind != JsonValueKind.Object)
                return "";

            switch (toolName.ToLowerInvariant())
            {
                case "bash":
                case "execute_command":
                    if (input.TryGetProperty("command", out var cmd))
                    {
                        if (cmd.ValueKind == JsonValueKind.String)
                            return $": {cmd.GetString()?.Split('\n')[0]?.Trim()}";
                    }
                    break;
                case "read":
                case "readfile":
                    if (input.TryGetProperty("file_path", out var fp))
                    {
                        if (fp.ValueKind == JsonValueKind.String)
                            return $": {fp.GetString()}";
                    }
                    break;
                case "write":
                case "writefile":
                    if (input.TryGetProperty("file_path", out var fp2))
                    {
                        if (fp2.ValueKind == JsonValueKind.String)
                            return $": {fp2.GetString()}";
                    }
                    break;
                case "edit":
                    if (input.TryGetProperty("file_path", out var fp3))
                    {
                        if (fp3.ValueKind == JsonValueKind.String)
                            return $": {fp3.GetString()}";
                    }
                    break;
                case "grep":
                    if (input.TryGetProperty("pattern", out var pat))
                    {
                        if (pat.ValueKind == JsonValueKind.String)
                            return $": {pat.GetString()}";
                    }
                    break;
            }
        }
        catch { /* ignore */ }
        return "";
    }
}
