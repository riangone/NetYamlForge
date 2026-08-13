using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetYamlForge.Services.AI;

/// <summary>
/// スロットフィリング管理サービス
/// 複数対話を通じて必要な情報を収集・管理します
/// FSM 状態机を統合したバージョン
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
    /// <param name="conversationId">対話セッション ID</param>
    /// <param name="projectId">
    /// テナント ID。省略時は ProjectScope（リクエストスコープ）または DefaultProjectId にフォールバックするため、
    /// マルチテナント文脈で呼び出す場合は必ず明示的に渡すこと（呼び出し元で検証済みの projectId をそのまま伝播する）。
    /// </param>
    Task<string?> GetActiveScenarioAsync(string conversationId, string? projectId = null);

    /// <summary>
    /// FSM 状態を更新
    /// </summary>
    /// <param name="conversationId">対話セッション ID</param>
    /// <param name="trigger">発火させる FSM トリガー名</param>
    /// <param name="confidence">信頼度（0.6 未満は低信頼度として扱う）</param>
    /// <param name="projectId">テナント ID。省略時のフォールバック挙動は <see cref="GetActiveScenarioAsync"/> 参照。</param>
    Task UpdateFsmStateAsync(string conversationId, string trigger, double confidence = 1.0, string? projectId = null);

    /// <summary>
    /// 現在の FSM 状態を文字列で取得
    /// </summary>
    /// <param name="conversationId">対話セッション ID</param>
    /// <param name="projectId">テナント ID。省略時のフォールバック挙動は <see cref="GetActiveScenarioAsync"/> 参照。</param>
    Task<string?> GetCurrentFsmStateAsync(string conversationId, string? projectId = null);

    /// <summary>
    /// 状態に基づいて許可された Tool リストを取得
    /// </summary>
    /// <param name="conversationId">対話セッション ID</param>
    /// <param name="projectId">テナント ID。省略時のフォールバック挙動は <see cref="GetActiveScenarioAsync"/> 参照。</param>
    Task<HashSet<string>> GetAllowedToolsAsync(string conversationId, string? projectId = null);

    /// <summary>
    /// Tool 呼び出しが現在の状態で許可されているかチェック
    /// </summary>
    /// <param name="conversationId">対話セッション ID</param>
    /// <param name="toolName">チェック対象の Tool 名</param>
    /// <param name="projectId">テナント ID。省略時のフォールバック挙動は <see cref="GetActiveScenarioAsync"/> 参照。</param>
    Task<bool> IsToolAllowedAsync(string conversationId, string toolName, string? projectId = null);
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
    public List<string>? QuickReplies { get; set; } // クイック返信选项
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
