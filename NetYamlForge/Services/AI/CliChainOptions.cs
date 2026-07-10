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
                SupportsVariant = true,
                VariantFlag = "--variant"
            },
            ["antigravity"] = new CliProviderOptions
            {
                Command = "antigravity",
                ArgsTemplate = "-p \"{prompt}\"{model} --dangerously-skip-permissions",
                PreferredForImages = true
                // antigravity に --variant 相当のオプションは無い（`antigravity --help` で確認済み、2026-07-08）。
                // 「レベル」は `antigravity models` が返すモデル名自体に埋め込まれている
                // （例："Gemini 3.5 Flash (High)"）ため、SupportsVariant は false のままにする。
            },
            ["claude"] = new CliProviderOptions
            {
                Command = "claude",
                // claude は --variant ではなく --effort というフラグ名でレベルを受け取る
                // （`claude --help` で確認済み、2026-07-08: "--effort <level> ... low, medium, high, xhigh, max"）。
                // 以前は ArgsTemplate に {variant} が無く、variant を渡しても黙って無視されていたバグがあった。
                ArgsTemplate = "-p \"{prompt}\"{model}{variant} --dangerously-skip-permissions",
                SupportsVariant = true,
                VariantFlag = "--effort"
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
    /// {variant} … " {VariantFlag} \"xxx\"" 形式の断片（SupportsVariant=false または未指定なら空文字）
    /// </summary>
    public string ArgsTemplate { get; set; } = "-p \"{prompt}\"{model} --dangerously-skip-permissions";

    /// <summary>この CLI が「レベル（reasoning effort 相当）」を渡すオプションを持つか。</summary>
    public bool SupportsVariant { get; set; } = false;

    /// <summary>
    /// レベルを渡す実際のフラグ名。CLI ごとに異なる（opencode は --variant、claude は --effort）ため
    /// 固定文字列にせず設定可能にしている。SupportsVariant=false の場合は使用されない。
    /// </summary>
    public string VariantFlag { get; set; } = "--variant";

    /// <summary>
    /// 画像解析タスクでこの CLI を優先的に先頭へ寄せるか。
    /// ユーザーが preferredProvider を明示指定しなかった場合にのみ参照される。
    /// </summary>
    public bool PreferredForImages { get; set; } = false;

    /// <summary>
    /// システム設定画面で管理者が指定する「既定モデル」。呼び出し元が model を明示しなかった場合に使用される。
    /// </summary>
    public string DefaultModel { get; set; } = "";

    /// <summary>
    /// システム設定画面で管理者が指定する「既定レベル」。呼び出し元が variant を明示しなかった場合に使用される。
    /// SupportsVariant=false の CLI では無視される。
    /// </summary>
    public string DefaultVariant { get; set; } = "";
}
