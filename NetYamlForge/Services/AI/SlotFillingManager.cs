using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetYamlForge.Services;
using NetYamlForge.Services.BatchJob;
using NetYamlForge.Services.AI.SlotFilling;

namespace NetYamlForge.Services.AI;

/// <summary>
/// スロットフィリングマネージャー実装
/// </summary>
public class SlotFillingManager : ISlotFillingManager
{
    private readonly ISlotSessionStore _sessionStore;
    private readonly IConversationFsmStore _fsmStore;
    private readonly ScenarioResolver _scenarioResolver;
    private readonly SlotFillingOptions _options;
    private readonly ILogger<SlotFillingManager> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ProjectScope? _projectScope;

    // 自動車販売向けシナリオ定義
    private readonly IAiScenarioYamlLoader _aiScenarioYamlLoader;

    public SlotFillingManager(
        ILogger<SlotFillingManager> logger,
        IServiceScopeFactory scopeFactory,
        IAiScenarioYamlLoader aiScenarioYamlLoader,
        ISlotSessionStore sessionStore,
        IConversationFsmStore fsmStore,
        ScenarioResolver scenarioResolver,
        IOptions<SlotFillingOptions> options,
        ProjectScope? projectScope = null)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _aiScenarioYamlLoader = aiScenarioYamlLoader;
        _sessionStore = sessionStore;
        _fsmStore = fsmStore;
        _scenarioResolver = scenarioResolver;
        _options = options.Value;
        _projectScope = projectScope;
    }

    private string GetResolvedProjectId(string? projectId)
    {
        if (!string.IsNullOrWhiteSpace(projectId))
            return projectId;
        if (_projectScope != null && _projectScope.IsSet)
            return _projectScope.Current.Name;
        
        var defaultPid = _options.DefaultProjectId;
        if (string.IsNullOrWhiteSpace(defaultPid))
        {
            throw new InvalidOperationException("Project ID is not specified and no DefaultProjectId is configured in SlotFillingOptions.");
        }
        return defaultPid;
    }

    private string GetSessionKey(string conversationId, string scenario, string? projectId)
    {
        var pid = GetResolvedProjectId(projectId);
        return $"{pid}:{conversationId}:{scenario}";
    }

    private string GetFsmKey(string conversationId, string? projectId)
    {
        var pid = GetResolvedProjectId(projectId);
        return $"{pid}:{conversationId}";
    }

    /// <inheritdoc />
    public async Task<SlotSession> GetSessionAsync(string conversationId, string scenario, string? projectId = null)
    {
        var key = GetSessionKey(conversationId, scenario, projectId);
        
        if (_sessionStore.TryGet(key, out var session) && session != null)
            return session;

        await EnsureSessionsLoadedAsync(conversationId, projectId);

        if (_sessionStore.TryGet(key, out session) && session != null)
            return session;

        // 新規セッション作成
        session = CreateNewSession(conversationId, scenario, projectId);
        _sessionStore.Set(key, session);
        await PersistSessionsAsync(conversationId, projectId);

        return session;
    }

    /// <inheritdoc />
    public async Task UpdateSlotAsync(string conversationId, string slotName, string value, string? projectId = null)
    {
        await EnsureSessionsLoadedAsync(conversationId, projectId);

        var updated = false;
        var pid = GetResolvedProjectId(projectId);
        var prefix = $"{pid}:{conversationId}:";

        // 全てのシナリオを検索して該当スロットを更新
        foreach (var kvp in _sessionStore.GetAllSessions())
        {
            if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && 
                kvp.Value.Slots.TryGetValue(slotName, out var slot))
            {
                slot.Value = value;
                kvp.Value.UpdatedAt = DateTime.UtcNow;
                updated = true;
                _logger.LogInformation("スロット更新：Conv={ConvId}, Slot={Slot}, Value={Value}, Project={Project}", 
                    conversationId, slotName, value, pid);
            }
        }

        if (updated)
            await PersistSessionsAsync(conversationId, projectId);
    }

    /// <inheritdoc />
    public async Task<bool> IsCompleteAsync(string conversationId, string scenario, string? projectId = null)
    {
        var key = GetSessionKey(conversationId, scenario, projectId);
        if (_sessionStore.TryGet(key, out var session) && session != null)
            return session.IsComplete;

        await EnsureSessionsLoadedAsync(conversationId, projectId);
        return _sessionStore.TryGet(key, out session) && session != null && session.IsComplete;
    }

    /// <inheritdoc />
    public async Task<SlotRequest?> GetNextRequiredSlotAsync(string conversationId, string scenario, string? projectId = null)
    {
        var key = GetSessionKey(conversationId, scenario, projectId);

        if (!_sessionStore.TryGet(key, out var session) || session == null)
        {
            await EnsureSessionsLoadedAsync(conversationId, projectId);
            if (!_sessionStore.TryGet(key, out session) || session == null)
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
        var pid = GetResolvedProjectId(projectId);
        var prefix = $"{pid}:{conversationId}:";
        var keysToRemove = _sessionStore.GetAllSessions().Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
        {
            _sessionStore.Remove(key);
        }

        _logger.LogInformation("スロットセッションリセット：Conv={ConvId}, Project={Project}", conversationId, pid);
        await RemoveSessionsFromContextAsync(conversationId, projectId);
    }

    /// <inheritdoc />
    public async Task<string?> GetActiveScenarioAsync(string conversationId, string? projectId = null)
    {
        var pid = GetResolvedProjectId(projectId);
        var prefix = $"{pid}:{conversationId}:";
        var activeSession = _sessionStore.GetAllSessions()
            .Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !kvp.Value.IsComplete)
            .Select(kvp => kvp.Value.Scenario)
            .FirstOrDefault();
        if (activeSession != null)
            return activeSession;

        await EnsureSessionsLoadedAsync(conversationId, pid);
        activeSession = _sessionStore.GetAllSessions()
            .Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !kvp.Value.IsComplete)
            .Select(kvp => kvp.Value.Scenario)
            .FirstOrDefault();
        return activeSession;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> GetCollectedSlotsAsync(string conversationId, string? projectId = null)
    {
        await EnsureSessionsLoadedAsync(conversationId, projectId);

        var slots = new Dictionary<string, string>();
        var pid = GetResolvedProjectId(projectId);
        var prefix = $"{pid}:{conversationId}:";
        
        foreach (var kvp in _sessionStore.GetAllSessions())
        {
            if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
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
    private SlotSession CreateNewSession(string conversationId, string scenario, string? projectId)
    {
        var now = DateTime.UtcNow;
        var session = new SlotSession
        {
            ConversationId = conversationId,
            Scenario = scenario,
            CreatedAt = now,
            UpdatedAt = now
        };

        var resolvedProjectId = GetResolvedProjectId(projectId);
        var config = _aiScenarioYamlLoader.GetConfig(resolvedProjectId);

        if (config.Scenarios.TryGetValue(scenario, out var definition))
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
        var resolver = new ScenarioResolver();
        return resolver.DetectScenarioFromMessage(message, intent);
    }

    private async Task EnsureSessionsLoadedAsync(string conversationId, string? projectId)
    {
        var pid = GetResolvedProjectId(projectId);
        var prefix = $"{pid}:{conversationId}:";
        if (_sessionStore.GetAllSessions().Any(kvp => kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
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
            var session = CreateSessionFromPayload(conversationId, scenario, sessionObj, projectId);
            _sessionStore.Set(GetSessionKey(conversationId, scenario, projectId), session);
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
        var pid = GetResolvedProjectId(projectId);
        var prefix = $"{pid}:{conversationId}:";
        var slotSessions = new JsonObject();
        foreach (var kvp in _sessionStore.GetAllSessions().Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
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

    private SlotSession CreateSessionFromPayload(string conversationId, string scenario, JsonObject payload, string? projectId)
    {
        var session = CreateNewSession(conversationId, scenario, projectId);

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
        var resolvedProject = GetResolvedProjectId(projectId);
        using var db = factory.CreateConnection(resolvedProject);
        db.Open();
        return await action(db);
    }

    // ===== FSM 統合メソッド =====

    /// <inheritdoc />
    public async Task UpdateFsmStateAsync(string conversationId, string trigger, double confidence = 1.0, string? projectId = null)
    {
        var fsm = await GetOrRestoreFsmAsync(conversationId, projectId);

        if (confidence < 0.6)
        {
            fsm.TriggerLowConfidence(confidence);
            _logger.LogInformation(
                "[FSM] 低信頼度検出 Conv={ConvId}, Confidence={Confidence}, State={State}",
                conversationId, confidence, fsm.CurrentState);
        }
        else
        {
            fsm.FireTrigger(trigger, confidence);
            _logger.LogInformation(
                "[FSM] 状態更新 Conv={ConvId}, Trigger={Trigger}, State={State}",
                conversationId, trigger, fsm.CurrentState);
        }

        // 状態をDBに永続化
        await PersistFsmStateAsync(conversationId, fsm, projectId);
    }

    /// <inheritdoc />
    public async Task<string?> GetCurrentFsmStateAsync(string conversationId, string? projectId = null)
    {
        var fsm = await GetOrRestoreFsmAsync(conversationId, projectId);
        return fsm.CurrentState;
    }

    /// <inheritdoc />
    public async Task<HashSet<string>> GetAllowedToolsAsync(string conversationId, string? projectId = null)
    {
        var fsm = await GetOrRestoreFsmAsync(conversationId, projectId);
        return new HashSet<string>(fsm.AllowedTools);
    }

    public async Task<bool> IsToolAllowedAsync(string conversationId, string toolName, string? projectId = null)
    {
        var fsm = await GetOrRestoreFsmAsync(conversationId, projectId);
        return fsm.AllowedTools.Contains(toolName);
    }

    /// <summary>
    /// DB から FSM 状態を復元、または新規作成して返す
    /// </summary>
    private async Task<IConversationFsm> GetOrRestoreFsmAsync(string conversationId, string? projectId)
    {
        var fsmKey = GetFsmKey(conversationId, projectId);
        if (_fsmStore.TryGet(fsmKey, out var existing) && existing != null)
            return existing;

        // DB から状態を読み込んで復元
        var dbState = await WithDbAsync(projectId, async db =>
        {
            return await db.QueryFirstOrDefaultAsync<(string? currentState, int lowConfidenceCount)>(
                "SELECT current_state, low_confidence_count FROM ai_conversations WHERE conversation_id = @Id",
                new { Id = conversationId });
        });

        var scenario = await GetActiveScenarioAsync(conversationId, projectId) ?? "test_drive";
        var resolvedProjectId = GetResolvedProjectId(projectId);
        var config = _aiScenarioYamlLoader.GetConfig(resolvedProjectId);

        if (!config.Scenarios.TryGetValue(scenario, out var scenarioConfig))
        {
            config.Scenarios.TryGetValue("test_drive", out scenarioConfig);
        }

        IConversationFsm newFsm;
        if (scenarioConfig != null)
        {
            newFsm = new DynamicConversationFsm(conversationId, scenarioConfig, dbState.currentState);
        }
        else
        {
            var initialState = ParseFsmState(dbState.currentState);
            newFsm = new AppointmentStateMachine(conversationId, initialState);
        }

        // 低信頼度カウントを復元
        for (int i = 0; i < dbState.lowConfidenceCount; i++)
            newFsm.TriggerLowConfidence(0.5);

        _fsmStore.Set(fsmKey, newFsm);
        return newFsm;
    }

    /// <summary>
    /// FSM 状態を DB に永続化する
    /// </summary>
    private async Task PersistFsmStateAsync(string conversationId, IConversationFsm fsm, string? projectId)
    {
        var stateStr = fsm.CurrentState.ToLowerInvariant();
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

    /// <summary>
    /// FSM 状態をリセット
    /// </summary>
    public void ResetFsm(string conversationId, string? projectId = null)
    {
        var fsmKey = GetFsmKey(conversationId, projectId);
        _fsmStore.Remove(fsmKey);
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
        var resolvedProjectId = GetResolvedProjectId(projectId);
        var config = _aiScenarioYamlLoader.GetConfig(resolvedProjectId);
        var activeScenario = await GetActiveScenarioAsync(conversationId, projectId) ?? "test_drive";

        string? trigger = null;
        if (config.Scenarios.TryGetValue(activeScenario, out var scenarioConfig))
        {
            var slotConfig = scenarioConfig.RequiredSlots
                .Concat(scenarioConfig.OptionalSlots)
                .FirstOrDefault(s => string.Equals(s.Name, slotName, StringComparison.OrdinalIgnoreCase));

            if (slotConfig != null)
            {
                trigger = slotConfig.Trigger;
            }
        }

        if (string.IsNullOrEmpty(trigger))
        {
            trigger = slotName switch
            {
                "vehicle_model" => "VehicleProvided",
                "preferred_date" => "DateProvided",
                "preferred_time" => "TimeProvided",
                "customer_name" => "NameProvided",
                "customer_phone" => "PhoneProvided",
                _ => null
            };
        }

        // スロットを更新
        await UpdateSlotAsync(conversationId, slotName, value, projectId);

        // トリガーが存在する場合、FSM を進行
        if (!string.IsNullOrEmpty(trigger))
        {
            await UpdateFsmStateAsync(conversationId, trigger, projectId: projectId);
        }
    }
}
