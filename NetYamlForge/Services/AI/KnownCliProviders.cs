namespace NetYamlForge.Services.AI;

/// <summary>
/// システム設定画面（SystemSettings）で「既知の対話型 AI CLI」を手入力ではなく
/// ドロップダウンから選ばせるための参照カタログ。
///
/// ここに載せる Command / ArgsTemplate / SupportsVariant / VariantFlag / PreferredForImages /
/// モデル一覧・レベル一覧の値は、このリポジトリのサンドボックス環境に実際にインストール済みの
/// CLI (opencode / antigravity / claude) に対して `--help`（opencode は `run --help`）や
/// `models` サブコマンドを実行し、出力を目視で確認したうえで採用している。
/// 推測でフラグ名やモデル名を追加することはしない。検証日: 2026-07-08。
///
///   opencode run --help  … --dangerously-skip-permissions (boolean、実在) /
///                            --variant <string> (実在、"model variant (provider-specific
///                            reasoning effort, e.g., high, max, minimal)" と説明されており、
///                            固定の列挙値ではなくモデル依存の自由入力)
///   opencode models       … 実行環境依存（models.dev のキャッシュ + ユーザーが有効化した provider/plugin
///                            構成に依存）。本サンドボックスでは以下6件が返った（参考値であり全環境共通ではない）：
///                            opencode/big-pickle, opencode/deepseek-v4-flash-free, opencode/hy3-free,
///                            opencode/mimo-v2.5-free, opencode/nemotron-3-ultra-free, opencode/north-mini-code-free
///   antigravity --help   … --dangerously-skip-permissions (boolean、実在) / --model (実在) /
///                            --variant オプションは無い（"レベル"はモデル名自体に埋め込まれている）
///   antigravity models   … 固定8件を返す（本サンドボックスで実行確認済み）：
///                            Gemini 3.5 Flash (Medium/High/Low)、Gemini 3.1 Pro (Low/High)、
///                            Claude Sonnet 4.6 (Thinking)、Claude Opus 4.6 (Thinking)、GPT-OSS 120B (Medium)
///   claude --help        … --dangerously-skip-permissions (boolean、実在)。
///                            --model <model> … "alias for the latest model (e.g. 'fable', 'opus',
///                            or 'sonnet') or a model's full name (e.g. 'claude-fable-5')"
///                            --effort <level> … "low, medium, high, xhigh, max"（固定5値、実在）。
///                            claude に models 一覧サブコマンドは無い（`claude models` はヘルプにフォールバックする
///                            ことを確認済み）ため、エイリアスのみを既知候補として提示する。
///
/// 3 CLI ともレベルを渡すフラグ名が異なる（opencode: --variant、antigravity: 無し、claude: --effort）。
/// これを吸収するため CliProviderOptions.VariantFlag を CLI ごとに設定可能にしてある。
/// カタログに無い CLI は設定画面側で "Custom" を選ばせ、ArgsTemplate は管理者自身が当該 CLI の実際の
/// ヘルプを確認したうえで入力する運用とする（このクラスでは推測値を生成しない）。
/// </summary>
public static class KnownCliProviders
{
    /// <summary>claude --model が受け付けるエイリアス（`claude --help` で確認済み、固定モデル一覧APIは無い）。</summary>
    public static readonly IReadOnlyList<string> ClaudeModelAliases = ["fable", "opus", "sonnet"];

    /// <summary>claude --effort が受け付ける固定5値（`claude --help` で確認済み）。</summary>
    public static readonly IReadOnlyList<string> ClaudeEffortLevels = ["low", "medium", "high", "xhigh", "max"];

    /// <summary>
    /// antigravity models の実行結果（本サンドボックスで確認済み）。ライブ取得に失敗した場合の
    /// フォールバック値として使う。将来 antigravity 側でラインナップが変わっても、
    /// <see cref="ICliModelCatalogService"/> がまずライブ実行を試みるため実害は小さい。
    /// </summary>
    public static readonly IReadOnlyList<string> AntigravityFallbackModels =
    [
        "Gemini 3.5 Flash (Medium)",
        "Gemini 3.5 Flash (High)",
        "Gemini 3.5 Flash (Low)",
        "Gemini 3.1 Pro (Low)",
        "Gemini 3.1 Pro (High)",
        "Claude Sonnet 4.6 (Thinking)",
        "Claude Opus 4.6 (Thinking)",
        "GPT-OSS 120B (Medium)"
    ];

    private static readonly Dictionary<string, string> VerifiedNotes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["opencode"] =
            "`opencode run --help` で確認済み（2026-07-08）: --dangerously-skip-permissions は実在する boolean フラグ。--variant <string> も実在するが、固定の列挙値ではなくモデル依存の自由入力（例: high/max/minimal）。モデル一覧は `opencode models` で取得できるが、ユーザー環境の provider 設定に依存するため下の一覧は参考値。",
        ["antigravity"] =
            "`antigravity --help` / `antigravity models` で確認済み（2026-07-08）: --dangerously-skip-permissions は実在する boolean フラグ。--variant オプションは無いため SupportsVariant=false。「レベル」は `antigravity models` が返すモデル名自体に埋め込まれている（例: \"Gemini 3.5 Flash (High)\"）。",
        ["claude"] =
            "`claude --help` で確認済み（2026-07-08）: --dangerously-skip-permissions は実在する boolean フラグ。--model はエイリアス（fable/opus/sonnet）またはフルネームを受け付ける。レベルは --variant ではなく --effort（low/medium/high/xhigh/max の固定5値）で渡す。`claude models` のような一覧サブコマンドは無い。",
    };

    /// <summary>
    /// appsettings.json の既定値（<see cref="CliChainOptions"/> の C# 側デフォルト）をそのまま
    /// 「検証済みカタログ」として使う。値の二重管理を避けるため、ここでは値を再定義しない。
    /// </summary>
    public static List<KnownProviderOption> BuildCatalog()
    {
        var defaults = new CliChainOptions().Providers;
        return defaults.Select(kv => new KnownProviderOption
        {
            Name = kv.Key,
            Command = kv.Value.Command,
            ArgsTemplate = kv.Value.ArgsTemplate,
            SupportsVariant = kv.Value.SupportsVariant,
            VariantFlag = kv.Value.VariantFlag,
            PreferredForImages = kv.Value.PreferredForImages,
            VerifiedNote = VerifiedNotes.GetValueOrDefault(kv.Key, ""),
            ModelChoices = BuildModelChoices(kv.Key),
            VariantChoices = string.Equals(kv.Key, "claude", StringComparison.OrdinalIgnoreCase)
                ? ClaudeEffortLevels.ToList()
                : new List<string>(),
            ModelsAreLive = kv.Key is "antigravity" or "opencode"
        }).ToList();
    }

    private static List<string> BuildModelChoices(string providerName) => providerName.ToLowerInvariant() switch
    {
        "antigravity" => AntigravityFallbackModels.ToList(),
        "claude" => ClaudeModelAliases.ToList(),
        _ => new List<string>()
    };
}

/// <summary>ドロップダウン表示・自動入力に使う、既知プロバイダー1件分の参照情報。</summary>
public class KnownProviderOption
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string ArgsTemplate { get; set; } = "";
    public bool SupportsVariant { get; set; }
    public string VariantFlag { get; set; } = "--variant";
    public bool PreferredForImages { get; set; }
    public string VerifiedNote { get; set; } = "";

    /// <summary>「既定モデル」ドロップダウンの初期選択肢。</summary>
    public List<string> ModelChoices { get; set; } = new();

    /// <summary>「既定レベル」ドロップダウンの選択肢（claude の --effort のような固定列挙のみ設定）。</summary>
    public List<string> VariantChoices { get; set; } = new();

    /// <summary>true の場合、画面から「CLI から最新一覧を取得」ボタンで ModelChoices を再取得できる。</summary>
    public bool ModelsAreLive { get; set; }
}
