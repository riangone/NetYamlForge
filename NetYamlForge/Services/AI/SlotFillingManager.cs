using System.Collections.Concurrent;
using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using NetYamlForge.Services;
using NetYamlForge.Services.BatchJob;

namespace NetYamlForge.Services.AI;

/// <summary>
/// スロットフィリング管理サービス
/// 複数対話を通じて必要な情報を収集・管理します
/// FSM 状態機を統合したバージョン
/// </summary>
public interface ISlotFillingManager
{
    /// <summary>
    /// 対話セッションのスロット状態を取得
    /// </summary>
    Task<SlotSession> GetSessionAsync(string conversationId, string scenario, string? projectId = null);

    /// <summary>
    /// スロット値を更新
    /// </summary>
    Task UpdateSlotAsync(string conversationId, string slotName, string value, string? projectId = null);

    /// <summary>
    /// 全スロットが埋まったかチェック
    /// </summary>
    Task<bool> IsCompleteAsync(string conversationId, string scenario, string? projectId = null);

    /// <summary>
    /// 次に収集すべきスロットを返す
    /// </summary>
    Task<SlotRequest?> GetNextRequiredSlotAsync(string conversationId, string scenario, string? projectId = null);

    /// <summary>
    /// スロットセッションをリセット
    /// </summary>
    Task ResetAsync(string conversationId, string? projectId = null);

    /// <summary>
    /// 収集済みスロットを取得
    /// </summary>
    Task<Dictionary<string, string>> GetCollectedSlotsAsync(string conversationId, string? projectId = null);

    /// <summary>
    /// アクティブ（未完了）なセッションのシナリオ名を返す。なければ null
    /// </summary>
    Task<string?> GetActiveScenarioAsync(string conversationId);

    /// <summary>
    /// FSM 状態を更新
    /// </summary>
    Task UpdateFsmStateAsync(string conversationId, AppointmentStateMachine.Trigger trigger, double confidence = 1.0);

    /// <summary>
    /// 現在の FSM 状態を文字列で取得
    /// </summary>
    Task<string?> GetCurrentFsmStateAsync(string conversationId);

    /// <summary>
    /// 状態に基づいて許可された Tool リストを取得
    /// </summary>
    Task<HashSet<string>> GetAllowedToolsAsync(string conversationId);

    /// <summary>
    /// Tool 呼び出しが現在の状態で許可されているかチェック
    /// </summary>
    Task<bool> IsToolAllowedAsync(string conversationId, string toolName);
}

/// <summary>
/// スロットセッション
/// </summary>
public class SlotSession
{
    public string ConversationId { get; set; } = string.Empty;
    public string Scenario { get; set; } = string.Empty;
    public Dictionary<string, SlotInfo> Slots { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsComplete => Slots.Values.All(s => s.IsFilled);

    /// <summary>
    /// 未収集のスロットを取得
    /// </summary>
    public List<SlotInfo> GetMissingSlots() => Slots.Values.Where(s => !s.IsFilled).ToList();

    /// <summary>
    /// 収集済みのスロットを取得
    /// </summary>
    public Dictionary<string, string> GetCollectedValues() =>
        Slots.Where(s => s.Value.IsFilled).ToDictionary(s => s.Key, s => s.Value.Value!);
}

/// <summary>
/// スロット情報
/// </summary>
public class SlotInfo
{
    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty; // 未収集時の質問プロンプト
    public string? Value { get; set; }
    public bool IsFilled => !string.IsNullOrWhiteSpace(Value);
    public bool IsRequired { get; set; }
    public string? ValidationPattern { get; set; } // 正規表現パターン
    public List<string>? AllowedValues { get; set; } // 許可された値のリスト
}

/// <summary>
/// スロット収集リクエスト
/// </summary>
public class SlotRequest
{
    public string SlotName { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty; // ユーザーへの質問
    public List<string>? QuickReplies { get; set; } // クイック返信オプション
}

/// <summary>
/// シナリオ定義
/// </summary>
public class ScenarioDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<SlotInfo> RequiredSlots { get; set; } = new();
    public List<SlotInfo> OptionalSlots { get; set; } = new();
}

/// <summary>
/// スロットフィリングマネージャー実装
/// </summary>
public class SlotFillingManager : ISlotFillingManager
{
    private readonly ConcurrentDictionary<string, SlotSession> _sessions = new();
    private readonly ConcurrentDictionary<string, AppointmentStateMachine> _fsmStates = new(); // FSM 状態管理
    private readonly ILogger<SlotFillingManager> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private const string DefaultProjectId = "auto-dealer-demo";

    // 自動車販売向けシナリオ定義
    private static readonly Dictionary<string, ScenarioDefinition> Scenarios = new()
    {
        ["test_drive"] = new ScenarioDefinition
        {
            Name = "test_drive",
            Description = "試乗予約",
            RequiredSlots = new List<SlotInfo>
            {
                new() { Name = "vehicle_model", Prompt = "どの車種の試乗をご希望ですか？", IsRequired = true },
                new() { Name = "preferred_date", Prompt = "ご希望の日付を教えてください（例：明日、来週月曜日）", IsRequired = true },
                new() { Name = "preferred_time", Prompt = "ご希望の時間帯を教えてください（例：午前 10 時、午後 2 時）", IsRequired = true },
                new() { Name = "customer_name", Prompt = "お名前を教えてください", IsRequired = true },
                new() { Name = "customer_phone", Prompt = "ご連絡先電話番号を教えてください", IsRequired = true }
            },
            OptionalSlots = new List<SlotInfo>
            {
                new() { Name = "current_vehicle", Prompt = "現在お乗りの車はありますか？（任意）", IsRequired = false },
                new() { Name = "license_status", Prompt = "運転免許証はお持ちですか？（任意）", IsRequired = false }
            }
        },

        ["estimate"] = new ScenarioDefinition
        {
            Name = "estimate",
            Description = "見積もり依頼",
            RequiredSlots = new List<SlotInfo>
            {
                new() { Name = "vehicle_model", Prompt = "どの車種の御見積もりをご希望ですか？", IsRequired = true },
                new() { Name = "grade", Prompt = "グレードは決まっていますか？（例：G、X、Z など）", IsRequired = true },
                new() { Name = "customer_name", Prompt = "お名前を教えてください", IsRequired = true },
                new() { Name = "customer_phone", Prompt = "ご連絡先電話番号を教えてください", IsRequired = true }
            },
            OptionalSlots = new List<SlotInfo>
            {
                new() { Name = "budget_amount", Prompt = "ご予算はありますか？（例：300 万円以内）", IsRequired = false },
                new() { Name = "payment_method", Prompt = "お支払い方法はいかがなさいますか？（ローン/現金）", IsRequired = false, AllowedValues = ["ローン", "現金", "リース"] },
                new() { Name = "trade_in", Prompt = "下取り車両はございますか？", IsRequired = false, AllowedValues = ["はい", "いいえ"] },
                new() { Name = "options", Prompt = "ご希望のオプションはありますか？（例：ナビ、ETC、ドラレコ）", IsRequired = false }
            }
        },

        ["appointment_service"] = new ScenarioDefinition
        {
            Name = "appointment_service",
            Description = "サービス予約（車検・整備）",
            RequiredSlots = new List<SlotInfo>
            {
                new() { Name = "service_type", Prompt = "どのようなご用件でしょうか？（例：車検、点検、オイル交換）", IsRequired = true },
                new() { Name = "vehicle_model", Prompt = "お車の車種を教えてください", IsRequired = true },
                new() { Name = "preferred_date", Prompt = "ご希望の日付を教えてください", IsRequired = true },
                new() { Name = "preferred_time", Prompt = "ご希望の時間帯を教えてください", IsRequired = true },
                new() { Name = "customer_name", Prompt = "お名前を教えてください", IsRequired = true },
                new() { Name = "customer_phone", Prompt = "ご連絡先電話番号を教えてください", IsRequired = true }
            },
            OptionalSlots = new List<SlotInfo>
            {
                new() { Name = "mileage", Prompt = "現在の走行距離を教えてください（任意）", IsRequired = false },
                new() { Name = "concerns", Prompt = "気になる点はありますか？（例：異音、振動）", IsRequired = false }
            }
        },

        ["trade_in"] = new ScenarioDefinition
        {
            Name = "trade_in",
            Description = "下取り査定",
            RequiredSlots = new List<SlotInfo>
            {
                new() { Name = "vehicle_brand", Prompt = "お車のメーカーを教えてください（例：トヨタ、ホンダ）", IsRequired = true },
                new() { Name = "vehicle_model", Prompt = "車種を教えてください", IsRequired = true },
                new() { Name = "vehicle_year", Prompt = "初度登録年を教えてください（例：2018 年）", IsRequired = true },
                new() { Name = "mileage", Prompt = "走行距離を教えてください（例：5 万 km）", IsRequired = true },
                new() { Name = "customer_name", Prompt = "お名前を教えてください", IsRequired = true },
                new() { Name = "customer_phone", Prompt = "ご連絡先電話番号を教えてください", IsRequired = true }
            },
            OptionalSlots = new List<SlotInfo>
            {
                new() { Name = "vehicle_condition", Prompt = "お車の状態はいかがですか？（事故歴など）", IsRequired = false },
                new() { Name = "preferred_date", Prompt = "査定ご希望の日付はありますか？", IsRequired = false }
            }
        },

        ["vehicle_inquiry"] = new ScenarioDefinition
        {
            Name = "vehicle_inquiry",
            Description = "車両お問い合わせ",
            RequiredSlots = new List<SlotInfo>
            {
                new() { Name = "vehicle_model", Prompt = "どの車種についてお調べしますか？", IsRequired = true }
            },
            OptionalSlots = new List<SlotInfo>
            {
                new() { Name = "vehicle_type", Prompt = "ご希望の車体タイプはありますか？（例：SUV、セダン）", IsRequired = false },
                new() { Name = "budget_amount", Prompt = "ご予算はありますか？", IsRequired = false },
                new() { Name = "vehicle_color", Prompt = "ご希望の色はありますか？", IsRequired = false }
            }
        }
    };

    public SlotFillingManager(ILogger<SlotFillingManager> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task<SlotSession> GetSessionAsync(string conversationId, string scenario, string? projectId = null)
    {
        var key = $"{conversationId}:{scenario}";
        
        if (_sessions.TryGetValue(key, out var session))
            return session;

        await EnsureSessionsLoadedAsync(conversationId, projectId);

        if (_sessions.TryGetValue(key, out session))
            return session;

        // 新規セッション作成
        session = CreateNewSession(conversationId, scenario);
        _sessions[key] = session;
        await PersistSessionsAsync(conversationId, projectId);

        return session;
    }

    /// <inheritdoc />
    public async Task UpdateSlotAsync(string conversationId, string slotName, string value, string? projectId = null)
    {
        await EnsureSessionsLoadedAsync(conversationId, projectId);

        var updated = false;

        // 全てのシナリオを検索して該当スロットを更新
        foreach (var kvp in _sessions)
        {
            if (kvp.Key.StartsWith($"{conversationId}:") && 
                kvp.Value.Slots.TryGetValue(slotName, out var slot))
            {
                slot.Value = value;
                kvp.Value.UpdatedAt = DateTime.UtcNow;
                updated = true;
                _logger.LogInformation("スロット更新：Conv={ConvId}, Slot={Slot}, Value={Value}", 
                    conversationId, slotName, value);
            }
        }

        if (updated)
            await PersistSessionsAsync(conversationId, projectId);
    }

    /// <inheritdoc />
    public async Task<bool> IsCompleteAsync(string conversationId, string scenario, string? projectId = null)
    {
        var key = $"{conversationId}:{scenario}";
        if (_sessions.TryGetValue(key, out var session))
            return session.IsComplete;

        await EnsureSessionsLoadedAsync(conversationId, projectId);
        return _sessions.TryGetValue(key, out session) && session.IsComplete;
    }

    /// <inheritdoc />
    public async Task<SlotRequest?> GetNextRequiredSlotAsync(string conversationId, string scenario, string? projectId = null)
    {
        var key = $"{conversationId}:{scenario}";

        if (!_sessions.TryGetValue(key, out var session))
        {
            await EnsureSessionsLoadedAsync(conversationId, projectId);
            if (!_sessions.TryGetValue(key, out session))
                return null;
        }

        var missingSlot = session.GetMissingSlots().FirstOrDefault(s => s.IsRequired);
        if (missingSlot == null)
            return null;

        return new SlotRequest
        {
            SlotName = missingSlot.Name,
            Prompt = missingSlot.Prompt,
            QuickReplies = missingSlot.AllowedValues
        };
    }

    /// <inheritdoc />
    public async Task ResetAsync(string conversationId, string? projectId = null)
    {
        var keysToRemove = _sessions.Keys.Where(k => k.StartsWith($"{conversationId}:")).ToList();
        foreach (var key in keysToRemove)
        {
            _sessions.TryRemove(key, out _);
        }

        _logger.LogInformation("スロットセッションリセット：Conv={ConvId}", conversationId);
        await RemoveSessionsFromContextAsync(conversationId, projectId);
    }

    /// <inheritdoc />
    public async Task<string?> GetActiveScenarioAsync(string conversationId)
    {
        var prefix = $"{conversationId}:";
        var activeSession = _sessions
            .Where(kvp => kvp.Key.StartsWith(prefix) && !kvp.Value.IsComplete)
            .Select(kvp => kvp.Value.Scenario)
            .FirstOrDefault();
        if (activeSession != null)
            return activeSession;

        await EnsureSessionsLoadedAsync(conversationId, null);
        activeSession = _sessions
            .Where(kvp => kvp.Key.StartsWith(prefix) && !kvp.Value.IsComplete)
            .Select(kvp => kvp.Value.Scenario)
            .FirstOrDefault();
        return activeSession;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> GetCollectedSlotsAsync(string conversationId, string? projectId = null)
    {
        await EnsureSessionsLoadedAsync(conversationId, projectId);

        var slots = new Dictionary<string, string>();
        
        foreach (var kvp in _sessions)
        {
            if (kvp.Key.StartsWith($"{conversationId}:"))
            {
                foreach (var slot in kvp.Value.GetCollectedValues())
                {
                    if (!slots.ContainsKey(slot.Key))
                        slots[slot.Key] = slot.Value;
                }
            }
        }

        return slots;
    }

    /// <summary>
    /// 新規セッションを作成
    /// </summary>
    private static SlotSession CreateNewSession(string conversationId, string scenario)
    {
        var now = DateTime.UtcNow;
        var session = new SlotSession
        {
            ConversationId = conversationId,
            Scenario = scenario,
            CreatedAt = now,
            UpdatedAt = now
        };

        // シナリオ定義からスロットを初期化
        if (Scenarios.TryGetValue(scenario, out var definition))
        {
            foreach (var slot in definition.RequiredSlots.Concat(definition.OptionalSlots))
            {
                session.Slots[slot.Name] = new SlotInfo
                {
                    Name = slot.Name,
                    Prompt = slot.Prompt,
                    IsRequired = slot.IsRequired,
                    ValidationPattern = slot.ValidationPattern,
                    AllowedValues = slot.AllowedValues
                };
            }
        }

        return session;
    }

    /// <summary>
    /// メッセージからスロットを自動検出
    /// </summary>
    public static string? DetectScenarioFromMessage(string message, string intent)
    {
        var lowerMessage = message.ToLowerInvariant();

        // 意図からシナリオをマッピング
        return intent switch
        {
            "test_drive_booking" => "test_drive",
            "price_inquiry" or "estimate_request" => "estimate",
            "service_booking" or "service_inquiry" or "maintenance" => "appointment_service",
            "trade_inquiry" => "trade_in",
            "vehicle_inquiry" => "vehicle_inquiry",
            _ => null
        };
    }

    private async Task EnsureSessionsLoadedAsync(string conversationId, string? projectId)
    {
        if (_sessions.Keys.Any(k => k.StartsWith($"{conversationId}:")))
            return;

        var context = await LoadContextDataAsync(conversationId, projectId);
        var slotSessionsNode = context["slot_sessions"] as JsonObject;
        if (slotSessionsNode == null)
            return;

        foreach (var entry in slotSessionsNode)
        {
            if (entry.Value is not JsonObject sessionObj)
                continue;

            var scenario = entry.Key;
            var session = CreateSessionFromPayload(conversationId, scenario, sessionObj);
            _sessions[$"{conversationId}:{scenario}"] = session;
        }
    }

    private async Task<JsonObject> LoadContextDataAsync(string conversationId, string? projectId)
    {
        return await WithDbAsync(projectId, async db =>
        {
            var contextJson = await db.QueryFirstOrDefaultAsync<string>(
                "SELECT context_data FROM ai_conversations WHERE conversation_id = @Id",
                new { Id = conversationId });
            return ParseContextData(contextJson);
        });
    }

    private async Task PersistSessionsAsync(string conversationId, string? projectId)
    {
        var slotSessions = new JsonObject();
        foreach (var kvp in _sessions.Where(kvp => kvp.Key.StartsWith($"{conversationId}:")))
        {
            var session = kvp.Value;
            var slotsNode = new JsonObject();
            foreach (var slot in session.Slots)
            {
                slotsNode[slot.Key] = new JsonObject
                {
                    ["value"] = slot.Value.Value,
                    ["filled"] = slot.Value.IsFilled
                };
            }

            slotSessions[session.Scenario] = new JsonObject
            {
                ["scenario"] = session.Scenario,
                ["slots"] = slotsNode,
                ["created_at"] = session.CreatedAt.ToString("O"),
                ["updated_at"] = session.UpdatedAt.ToString("O")
            };
        }

        await UpdateContextDataAsync(conversationId, projectId, ctx =>
        {
            ctx["slot_sessions"] = slotSessions;
        });
    }

    private async Task RemoveSessionsFromContextAsync(string conversationId, string? projectId)
    {
        await UpdateContextDataAsync(conversationId, projectId, ctx =>
        {
            ctx.Remove("slot_sessions");
        });
    }

    private async Task UpdateContextDataAsync(string conversationId, string? projectId, Action<JsonObject> mutator)
    {
        await WithDbAsync(projectId, async db =>
        {
            var contextJsonRaw = await db.QueryFirstOrDefaultAsync<string>(
                "SELECT context_data FROM ai_conversations WHERE conversation_id = @Id",
                new { Id = conversationId });
            var context = ParseContextData(contextJsonRaw);
            mutator(context);
            var contextJsonUpdated = context.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = false
            });

            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            await db.ExecuteAsync(@"
UPDATE ai_conversations
SET context_data = @ContextData, updated_at = @Now
WHERE conversation_id = @ConversationId",
                new { ContextData = contextJsonUpdated, Now = now, ConversationId = conversationId });
            return 0;
        });
    }

    private static JsonObject ParseContextData(string? contextJson)
    {
        if (string.IsNullOrWhiteSpace(contextJson))
            return new JsonObject();

        try
        {
            var node = JsonNode.Parse(contextJson) as JsonObject;
            return node ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private SlotSession CreateSessionFromPayload(string conversationId, string scenario, JsonObject payload)
    {
        var session = CreateNewSession(conversationId, scenario);

        if (payload["created_at"]?.GetValue<string>() is string createdStr &&
            DateTime.TryParse(createdStr, out var createdAt))
            session.CreatedAt = createdAt;

        if (payload["updated_at"]?.GetValue<string>() is string updatedStr &&
            DateTime.TryParse(updatedStr, out var updatedAt))
            session.UpdatedAt = updatedAt;

        if (payload["slots"] is JsonObject slotsNode)
        {
            foreach (var slotEntry in slotsNode)
            {
                if (!session.Slots.TryGetValue(slotEntry.Key, out var slotInfo))
                    continue;
                if (slotEntry.Value is not JsonObject slotValueObj)
                    continue;

                if (slotValueObj["value"]?.GetValue<string>() is string slotValue)
                    slotInfo.Value = slotValue;
            }
        }

        return session;
    }

    private async Task<T> WithDbAsync<T>(string? projectId, Func<IDbConnection, Task<T>> action)
    {
        using var scope = _scopeFactory.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        var resolvedProject = ResolveProjectId(projectId, scope.ServiceProvider.GetService<ProjectScope>());
        using var db = factory.CreateConnection(resolvedProject);
        db.Open();
        return await action(db);
    }

    private static string ResolveProjectId(string? projectId, ProjectScope? projectScope)
    {
        if (!string.IsNullOrWhiteSpace(projectId))
            return projectId;
        if (projectScope != null && projectScope.IsSet)
            return projectScope.Current.Name;
        return DefaultProjectId;
    }

    // ===== FSM 統合メソッド =====

    /// <inheritdoc />
    public async Task UpdateFsmStateAsync(string conversationId, AppointmentStateMachine.Trigger trigger, double confidence = 1.0)
    {
        var fsm = await GetOrRestoreFsmAsync(conversationId, null);

        if (confidence < 0.6)
        {
            fsm.TriggerLowConfidence(confidence);
            _logger.LogInformation(
                "[FSM] 低信頼度検出 Conv={ConvId}, Confidence={Confidence}, State={State}",
                conversationId, confidence, fsm.CurrentState);
        }
        else
        {
            if (fsm.CanFire(trigger))
            {
                fsm.Fire(trigger);
                _logger.LogInformation(
                    "[FSM] 状態更新 Conv={ConvId}, Trigger={Trigger}, State={State}",
                    conversationId, trigger, fsm.CurrentState);
            }
        }

        // 状態をDBに永続化
        await PersistFsmStateAsync(conversationId, fsm, null);
    }

    /// <inheritdoc />
    public async Task<string?> GetCurrentFsmStateAsync(string conversationId)
    {
        var fsm = await GetOrRestoreFsmAsync(conversationId, null);
        return fsm.CurrentState.ToString();
    }

    /// <inheritdoc />
    public async Task<HashSet<string>> GetAllowedToolsAsync(string conversationId)
    {
        var fsm = await GetOrRestoreFsmAsync(conversationId, null);
        return fsm.GetAllowedTools();
    }

    public async Task<bool> IsToolAllowedAsync(string conversationId, string toolName)
    {
        var fsm = await GetOrRestoreFsmAsync(conversationId, null);
        return fsm.IsToolAllowed(toolName);
    }

    /// <summary>
    /// DB から FSM 状態を復元、または新規作成して返す
    /// </summary>
    private async Task<AppointmentStateMachine> GetOrRestoreFsmAsync(string conversationId, string? projectId)
    {
        if (_fsmStates.TryGetValue(conversationId, out var existing))
            return existing;

        // DB から状態を読み込んで復元
        var dbState = await WithDbAsync(projectId, async db =>
        {
            return await db.QueryFirstOrDefaultAsync<(string? currentState, int lowConfidenceCount)>(
                "SELECT current_state, low_confidence_count FROM ai_conversations WHERE conversation_id = @Id",
                new { Id = conversationId });
        });

        var initialState = ParseFsmState(dbState.currentState);
        var newFsm = new AppointmentStateMachine(conversationId, initialState);

        // 低信頼度カウントを復元
        for (int i = 0; i < dbState.lowConfidenceCount; i++)
            newFsm.TriggerLowConfidence(0.5);

        _fsmStates.TryAdd(conversationId, newFsm);
        return newFsm;
    }

    /// <summary>
    /// FSM 状態を DB に永続化する
    /// </summary>
    private async Task PersistFsmStateAsync(string conversationId, AppointmentStateMachine fsm, string? projectId)
    {
        var stateStr = FsmStateToDbString(fsm.CurrentState);
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        await WithDbAsync(projectId, async db =>
        {
            await db.ExecuteAsync(@"
UPDATE ai_conversations
SET current_state = @State,
    low_confidence_count = @Count,
    updated_at = @Now
WHERE conversation_id = @Id",
                new { State = stateStr, Count = fsm.LowConfidenceCount, Now = now, Id = conversationId });
            return 0;
        });
    }

    private static AppointmentStateMachine.State ParseFsmState(string? stateStr) =>
        stateStr switch
        {
            "collect_vehicle" => AppointmentStateMachine.State.CollectVehicle,
            "collect_date"    => AppointmentStateMachine.State.CollectDate,
            "collect_time"    => AppointmentStateMachine.State.CollectTime,
            "collect_name"    => AppointmentStateMachine.State.CollectName,
            "collect_phone"   => AppointmentStateMachine.State.CollectPhone,
            "confirming"      => AppointmentStateMachine.State.Confirming,
            "booked"          => AppointmentStateMachine.State.Booked,
            "cancelled"       => AppointmentStateMachine.State.Cancelled,
            "escalate"        => AppointmentStateMachine.State.Escalate,
            _                 => AppointmentStateMachine.State.Init
        };

    private static string FsmStateToDbString(AppointmentStateMachine.State state) =>
        state switch
        {
            AppointmentStateMachine.State.CollectVehicle => "collect_vehicle",
            AppointmentStateMachine.State.CollectDate    => "collect_date",
            AppointmentStateMachine.State.CollectTime    => "collect_time",
            AppointmentStateMachine.State.CollectName    => "collect_name",
            AppointmentStateMachine.State.CollectPhone   => "collect_phone",
            AppointmentStateMachine.State.Confirming     => "confirming",
            AppointmentStateMachine.State.Booked         => "booked",
            AppointmentStateMachine.State.Cancelled      => "cancelled",
            AppointmentStateMachine.State.Escalate       => "escalate",
            _                                            => "init"
        };

    /// <summary>
    /// FSM 状態をリセット
    /// </summary>
    public void ResetFsm(string conversationId)
    {
        _fsmStates.TryRemove(conversationId, out _);
        _logger.LogInformation("[FSM] 状態リセット Conv={ConvId}", conversationId);
    }

    /// <summary>
    /// 槽位更新時に FSM を自動進行
    /// </summary>
    public async Task UpdateSlotWithFsmAsync(
        string conversationId,
        string slotName,
        string value,
        string? projectId = null)
    {
        // スロットを更新
        await UpdateSlotAsync(conversationId, slotName, value, projectId);

        // スロット名に基づいて FSM トリガーを決定
        var trigger = slotName switch
        {
            "vehicle_model" => AppointmentStateMachine.Trigger.VehicleProvided,
            "preferred_date" => AppointmentStateMachine.Trigger.DateProvided,
            "preferred_time" => AppointmentStateMachine.Trigger.TimeProvided,
            "customer_name" => AppointmentStateMachine.Trigger.NameProvided,
            "customer_phone" => AppointmentStateMachine.Trigger.PhoneProvided,
            _ => default
        };

        // トリガーが存在する場合、FSM を進行
        if (trigger != default || Enum.IsDefined(trigger))
        {
            await UpdateFsmStateAsync(conversationId, trigger);
        }
    }
}
