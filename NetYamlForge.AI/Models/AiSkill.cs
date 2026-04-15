namespace NetYamlForge.AI.Models;

/// <summary>
/// AI スキル（プロンプトテンプレート）。
/// skills/*.md から読み込まれる。
/// </summary>
public class AiSkill
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "⚡";
    public string Description { get; set; } = string.Empty;
    /// <summary>クリック時にチャット入力に挿入されるプロンプト本文</summary>
    public string Prompt { get; set; } = string.Empty;
    /// <summary>true の場合、ユーザーが追記してから送信する</summary>
    public bool NeedsInput { get; set; }
    public string? InputPlaceholder { get; set; }
    /// <summary>表示順（小さいほど先に表示）</summary>
    public int Order { get; set; } = 99;
}
