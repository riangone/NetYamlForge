namespace NetYamlForge.Services.AI;

/// <summary>
/// <see cref="CliChainService"/> が使用する対話型 AI CLI の設定。
/// appsettings.json の "AiCliChain" セクションから読み込まれる。
///
/// 従来は実行ファイル名（opencode/antigravity/claude）と引数の組み立て方が
/// C# コードにハードコードされていたため、CLI の入れ替え・追加・引数変更の
/// たびにビルドが必要だった。設定ファイル化することで、CLI の追加やコマンド名の
/// 変更（例：PATH に無い場合のフルパス指定）を appsettings.json / appsettings.{env}.json
/// の変更のみで行えるようにする。
/// </summary>
public class CliChainOptions
{
    public const string SectionName = "AiCliChain";

    /// <summary>既定の優先順位。プロジェクト .env の CLI_CHAIN_ORDER で上書き可能。</summary>
    public string[] DefaultOrder { get; set; } = ["opencode", "antigravity", "claude"];

    /// <summary>各 CLI プロセスの実行タイムアウト（秒）。</summary>
    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>プロバイダー名 → CLI 起動設定。</summary>
    public Dictionary<string, CliProviderOptions> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["opencode"] = new CliProviderOptions
            {
                Command = "opencode",
                ArgsTemplate = "run \"{prompt}\"{model}{variant} --dangerously-skip-permissions",
                SupportsVariant = true
            },
            ["antigravity"] = new CliProviderOptions
            {
                Command = "antigravity",
                ArgsTemplate = "-p \"{prompt}\"{model} --dangerously-skip-permissions",
                PreferredForImages = true
            },
            ["claude"] = new CliProviderOptions
            {
                Command = "claude",
                ArgsTemplate = "-p \"{prompt}\"{model} --dangerously-skip-permissions"
            }
        };
}

/// <summary>単一 CLI プロバイダーの起動設定。</summary>
public class CliProviderOptions
{
    /// <summary>実行するコマンド名（PATH 上の実行ファイル名、または絶対パス）。</summary>
    public string Command { get; set; } = "";

    /// <summary>
    /// 引数テンプレート。以下のプレースホルダーを置換して使用する。
    /// {prompt}  … エスケープ済みのプロンプト文字列（呼び出し側でクォートを付与すること）
    /// {model}   … " --model \"xxx\"" 形式の断片（未指定なら空文字）
    /// {variant} … " --variant \"xxx\"" 形式の断片（SupportsVariant=false または未指定なら空文字）
    /// </summary>
    public string ArgsTemplate { get; set; } = "-p \"{prompt}\"{model} --dangerously-skip-permissions";

    /// <summary>この CLI が --variant オプションをサポートするか。</summary>
    public bool SupportsVariant { get; set; } = false;

    /// <summary>
    /// 画像解析タスクでこの CLI を優先的に先頭へ寄せるか。
    /// ユーザーが preferredProvider を明示指定しなかった場合にのみ参照される。
    /// </summary>
    public bool PreferredForImages { get; set; } = false;
}
