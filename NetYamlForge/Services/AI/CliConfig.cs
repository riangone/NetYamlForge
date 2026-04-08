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
    public int TaskTimeoutSeconds { get; set; } = 3600;

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

    /// <summary>Codex CLI 固有設定</summary>
    public CodexConfig Codex { get; set; } = new();

    /// <summary>Gemini CLI 固有設定</summary>
    public GeminiConfig Gemini { get; set; } = new();

    /// <summary>Ollama 固有設定</summary>
    public OllamaConfig Ollama { get; set; } = new();

    /// <summary>LM Studio 固有設定</summary>
    public LmStudioConfig LmStudio { get; set; } = new();

    /// <summary>GitHub Copilot CLI 固有設定</summary>
    public CopilotConfig Copilot { get; set; } = new();

}

/// <summary>
/// Claude Code 設定
/// </summary>
public partial class ClaudeConfig
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
public partial class QwenCodeConfig
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

/// <summary>
/// Codex CLI 設定
/// </summary>
public partial class CodexConfig
{
    /// <summary>
    /// OpenAI API キー（OPENAI_API_KEY）。
    /// Codex CLI の認証に必要。
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// OpenAI API エンドポイント（OPENAI_BASE_URL）。
    /// 省略時はデフォルトエンドポイントを使用する。
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// OpenAI Organization ID（OPENAI_ORG_ID）。
    /// 必要に応じて設定する。
    /// </summary>
    public string? Organization { get; set; }

    /// <summary>
    /// 使用するモデル名（例：codex-latest, gpt-4）。
    /// 省略時は Codex CLI のデフォルトモデルを使用する。
    /// </summary>
    public string? Model { get; set; }

    /// <summary>codex コマンドのフルパス（省略時は PATH から検索）</summary>
    public string? Path { get; set; }
}

/// <summary>
/// Gemini CLI 設定
/// </summary>
public partial class GeminiConfig
{
    /// <summary>
    /// Google API キー（GOOGLE_API_KEY）。
    /// Gemini CLI の認証に必要。
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Google Cloud Project ID（GOOGLE_CLOUD_PROJECT）。
    /// 必要に応じて設定する。
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// 開発者モードを有効にする（GOOGLE_GENAI_DEVELOPER_MODE）。
    /// </summary>
    public bool DeveloperMode { get; set; }

    /// <summary>
    /// 使用するモデル名（例：gemini-2.5-pro, gemini-pro）。
    /// 省略時は Gemini CLI のデフォルトモデルを使用する。
    /// </summary>
    public string? Model { get; set; }

    /// <summary>gemini コマンドのフルパス（省略時は PATH から検索）</summary>
    public string? Path { get; set; }
}

/// <summary>
/// Ollama 設定
/// </summary>
public class OllamaConfig
{
    /// <summary>
    /// Ollama API エンドポイント。
    /// 省略時は http://localhost:11434 を使用。
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// 使用するモデル名（例：qwen2.5-coder, llama3.2, deepseek-coder）。
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// ollama コマンドのフルパス（省略時は PATH から検索）。
    /// CLI モードで使用。
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// API モードを使用する（デフォルト：true）。
    /// false の場合は ollama CLI コマンドを使用。
    /// </summary>
    public bool UseApi { get; set; } = true;

    /// <summary>
    /// コンテキストウィンドウサイズ（デフォルト：4096）。
    /// </summary>
    public int ContextSize { get; set; } = 4096;

    /// <summary>
    /// 温度パラメータ（デフォルト：0.7）。
    /// </summary>
    public float Temperature { get; set; } = 0.7f;
}

/// <summary>
/// LM Studio 設定
/// </summary>
public class LmStudioConfig
{
    /// <summary>
    /// LM Studio API エンドポイント。
    /// 省略時は http://localhost:1234 を使用。
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// 使用するモデル名（例：qwen2.5-coder-7b, llama-3.2-3b-instruct）。
    /// LM Studio でロード済みのモデルを指定。
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// コンテキストウィンドウサイズ（デフォルト：4096）。
    /// </summary>
    public int ContextSize { get; set; } = 4096;

    /// <summary>
    /// 温度パラメータ（デフォルト：0.7）。
    /// </summary>
    public float Temperature { get; set; } = 0.7f;
}

/// <summary>
/// GitHub Copilot CLI 設定
/// </summary>
public partial class CopilotConfig
{
    /// <summary>
    /// GitHub Copilot トークン（GITHUB_COPILOT_TOKEN）。
    /// Copilot CLI の認証に必要。
    /// </summary>
    public string? Token { get; set; }

    /// <summary>copilot コマンドのフルパス（省略時は PATH から検索）</summary>
    public string? Path { get; set; }
}

/// <summary>
/// Codex CLI 設定（拡張）
/// </summary>
public partial class CodexConfig
{
}

/// <summary>
/// Gemini CLI 設定（拡張）
/// </summary>
public partial class GeminiConfig
{
}

/// <summary>
/// GitHub Copilot CLI 設定（拡張）
/// </summary>
public partial class CopilotConfig
{
}

/// <summary>
/// Claude Code 設定（拡張）
/// </summary>
public partial class ClaudeConfig
{
}

/// <summary>
/// Qwen Code 設定（拡張）
/// </summary>
public partial class QwenCodeConfig
{
}
