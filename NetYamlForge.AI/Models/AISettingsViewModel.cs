namespace NetYamlForge.AI.Models;

/// <summary>
/// AI プロバイダー設定ページのビューモデル
/// </summary>
public class AISettingsViewModel
{
    /// <summary>全プロバイダーの稼働状況（ツール名 → CliToolInfo）</summary>
    public Dictionary<string, CliToolInfo> Tools { get; set; } = new();

    /// <summary>現在のデフォルトツール名（appsettings.json の AICli.DefaultTool）</summary>
    public string DefaultTool { get; set; } = "qwen";

    /// <summary>AI 窓口チャットのプロバイダー優先順序（AiWindow.ProviderPriority）</summary>
    public List<string> WindowProviderPriority { get; set; } = new();
}
