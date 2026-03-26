namespace NetYamlForge.Services.AI;

/// <summary>
/// AI CLI 共通設定（appsettings.json の "AICli" セクション）
/// </summary>
public class CliConfig
{
    public const string SectionName = "AICli";

    /// <summary>デフォルトで使用する CLI ツール名（claude / qwen-code / mock）</summary>
    public string DefaultTool { get; set; } = "claude";

    /// <summary>タスクタイムアウト（秒）</summary>
    public int TaskTimeoutSeconds { get; set; } = 1800;

    /// <summary>最大同時実行タスク数</summary>
    public int MaxConcurrentTasks { get; set; } = 2;

    /// <summary>デフォルト作業ディレクトリ</summary>
    public string? DefaultWorkingDirectory { get; set; }

    /// <summary>デフォルト許可ツールリスト</summary>
    public List<string> DefaultAllowedTools { get; set; } = new()
    {
        "Read", "Write", "Edit", "Bash", "Git"
    };

    /// <summary>Claude Code 固有設定</summary>
    public ClaudeConfig Claude { get; set; } = new();

    /// <summary>Qwen Code 固有設定</summary>
    public QwenCodeConfig QwenCode { get; set; } = new();

    /// <summary>
    /// 全 CLI 呼び出しに追加されるシステムプロンプト。
    /// フレームワーク固有の操作ガイドを AI に伝えるために使用する。
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>よく使う操作のプロンプトテンプレート一覧（UI のクイックアクション）</summary>
    public List<AiSkill> Skills { get; set; } = new();
}

/// <summary>
/// AI スキル（プロンプトテンプレート）定義
/// </summary>
public class AiSkill
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "⚡";
    public string Description { get; set; } = string.Empty;
    /// <summary>クリック時にチャット入力に挿入されるプロンプトテンプレート</summary>
    public string Prompt { get; set; } = string.Empty;
    /// <summary>true の場合、ユーザーが追記してから送信する</summary>
    public bool NeedsInput { get; set; }
    public string? InputPlaceholder { get; set; }
}

/// <summary>
/// Claude Code 設定
/// </summary>
public class ClaudeConfig
{
    /// <summary>
    /// Anthropic API キー（ANTHROPIC_API_KEY）。
    /// 空の場合は claude login で保存済みの認証情報を使用する。
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>claude コマンドのフルパス（省略時は PATH から検索）</summary>
    public string? Path { get; set; }
}

/// <summary>
/// Qwen Code 設定
/// </summary>
public class QwenCodeConfig
{
    /// <summary>
    /// Alibaba Cloud DashScope API キー（DASHSCOPE_API_KEY）。
    /// Qwen Code の認証に必要。
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// DashScope API エンドポイント（DASHSCOPE_BASE_URL）。
    /// 省略時はデフォルトエンドポイントを使用する。
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// 使用するモデル名（例: qwen-coder-plus, qwen-max）。
    /// 省略時は Qwen Code のデフォルトモデルを使用する。
    /// </summary>
    public string? Model { get; set; }

    /// <summary>qwen-code コマンドのフルパス（省略時は PATH から検索）</summary>
    public string? Path { get; set; }
}
