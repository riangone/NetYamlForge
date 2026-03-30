// ファイル概要：スロットフィリング管理 - 複数対話での情報収集を管理します
// 自動車販売の主要シナリオ（予約、見積もり、下取りなど）に必要なスロットを定義

using System.Collections.Concurrent;

namespace NetYamlForge.Services.AI;

/// <summary>
/// スロットフィリング管理サービス
/// 複数対話を通じて必要な情報を収集・管理します
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
    private readonly ILogger<SlotFillingManager> _logger;

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

    public SlotFillingManager(ILogger<SlotFillingManager> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<SlotSession> GetSessionAsync(string conversationId, string scenario, string? projectId = null)
    {
        var key = $"{conversationId}:{scenario}";
        
        if (!_sessions.TryGetValue(key, out var session))
        {
            // 新規セッション作成
            session = CreateNewSession(conversationId, scenario);
            _sessions[key] = session;
        }

        return Task.FromResult(session);
    }

    /// <inheritdoc />
    public Task UpdateSlotAsync(string conversationId, string slotName, string value, string? projectId = null)
    {
        // 全てのシナリオを検索して該当スロットを更新
        foreach (var kvp in _sessions)
        {
            if (kvp.Key.StartsWith($"{conversationId}:") && 
                kvp.Value.Slots.TryGetValue(slotName, out var slot))
            {
                slot.Value = value;
                kvp.Value.UpdatedAt = DateTime.UtcNow;
                _logger.LogInformation("スロット更新：Conv={ConvId}, Slot={Slot}, Value={Value}", 
                    conversationId, slotName, value);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> IsCompleteAsync(string conversationId, string scenario, string? projectId = null)
    {
        var key = $"{conversationId}:{scenario}";
        return Task.FromResult(_sessions.TryGetValue(key, out var session) && session.IsComplete);
    }

    /// <inheritdoc />
    public Task<SlotRequest?> GetNextRequiredSlotAsync(string conversationId, string scenario, string? projectId = null)
    {
        var key = $"{conversationId}:{scenario}";
        
        if (!_sessions.TryGetValue(key, out var session))
            return Task.FromResult<SlotRequest?>(null);

        var missingSlot = session.GetMissingSlots().FirstOrDefault(s => s.IsRequired);
        if (missingSlot == null)
            return Task.FromResult<SlotRequest?>(null);

        return Task.FromResult<SlotRequest?>(new SlotRequest
        {
            SlotName = missingSlot.Name,
            Prompt = missingSlot.Prompt,
            QuickReplies = missingSlot.AllowedValues
        });
    }

    /// <inheritdoc />
    public Task ResetAsync(string conversationId, string? projectId = null)
    {
        var keysToRemove = _sessions.Keys.Where(k => k.StartsWith($"{conversationId}:")).ToList();
        foreach (var key in keysToRemove)
        {
            _sessions.TryRemove(key, out _);
        }

        _logger.LogInformation("スロットセッションリセット：Conv={ConvId}", conversationId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Dictionary<string, string>> GetCollectedSlotsAsync(string conversationId, string? projectId = null)
    {
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

        return Task.FromResult(slots);
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
            "service_inquiry" or "maintenance" => "appointment_service",
            "trade_inquiry" => "trade_in",
            "vehicle_inquiry" => "vehicle_inquiry",
            _ => null
        };
    }
}
