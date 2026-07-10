// ファイル概要：システム設定画面（AI CLI チェーン）の編集フォーム用ビューモデル。
// CliChainOptions は Dictionary<string, CliProviderOptions> を持つが、フォーム編集は
// インデックス付きリストの方が扱いやすいため、行リストへ相互変換する。

using NetYamlForge.Services.AI;

namespace NetYamlForge.Models.Config;

public class AiCliChainSettingsViewModel
{
    /// <summary>各 CLI 実行のタイムアウト秒数。</summary>
    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>カンマ区切りの既定試行順（例：opencode, antigravity, claude）。</summary>
    public string DefaultOrderCsv { get; set; } = "";

    public List<CliProviderRow> Providers { get; set; } = new();

    /// <summary>
    /// 手入力を避けるためのドロップダウン用参照カタログ（<see cref="KnownCliProviders"/>）。
    /// Command / ArgsTemplate は実際の CLI --help を確認済みの値のみを含む。
    /// </summary>
    public List<KnownProviderOption> KnownProviders { get; set; } = KnownCliProviders.BuildCatalog();

    public static AiCliChainSettingsViewModel FromOptions(CliChainOptions options)
    {
        return new AiCliChainSettingsViewModel
        {
            TimeoutSeconds = options.TimeoutSeconds,
            DefaultOrderCsv = string.Join(", ", options.DefaultOrder),
            Providers = options.Providers
                .Select(kv => new CliProviderRow
                {
                    Name = kv.Key,
                    Command = kv.Value.Command,
                    ArgsTemplate = kv.Value.ArgsTemplate,
                    SupportsVariant = kv.Value.SupportsVariant,
                    VariantFlag = kv.Value.VariantFlag,
                    PreferredForImages = kv.Value.PreferredForImages,
                    DefaultModel = kv.Value.DefaultModel,
                    DefaultVariant = kv.Value.DefaultVariant
                })
                .ToList(),
            KnownProviders = KnownCliProviders.BuildCatalog()
        };
    }

    /// <summary>
    /// フォーム内容から CliChainOptions を組み立てる。
    /// 名前またはコマンドが空の行（削除ボタンで消したはずが送信されてしまった行など）は無視する。
    /// </summary>
    public CliChainOptions ToOptions()
    {
        var options = new CliChainOptions
        {
            TimeoutSeconds = TimeoutSeconds > 0 ? TimeoutSeconds : 90,
            DefaultOrder = SplitCsv(DefaultOrderCsv),
            Providers = new Dictionary<string, CliProviderOptions>(StringComparer.OrdinalIgnoreCase)
        };

        foreach (var row in Providers)
        {
            if (string.IsNullOrWhiteSpace(row.Name) || string.IsNullOrWhiteSpace(row.Command))
                continue;

            options.Providers[row.Name.Trim().ToLowerInvariant()] = new CliProviderOptions
            {
                Command = row.Command.Trim(),
                ArgsTemplate = string.IsNullOrWhiteSpace(row.ArgsTemplate)
                    ? "-p \"{prompt}\"{model} --dangerously-skip-permissions"
                    : row.ArgsTemplate,
                SupportsVariant = row.SupportsVariant,
                VariantFlag = string.IsNullOrWhiteSpace(row.VariantFlag) ? "--variant" : row.VariantFlag.Trim(),
                PreferredForImages = row.PreferredForImages,
                DefaultModel = row.DefaultModel?.Trim() ?? "",
                DefaultVariant = row.DefaultVariant?.Trim() ?? ""
            };
        }

        return options;
    }

    private static string[] SplitCsv(string? csv) =>
        (csv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .Distinct()
            .ToArray();
}

/// <summary>1 つの CLI プロバイダーに対応するフォーム行。</summary>
public class CliProviderRow
{
    public string Name { get; set; } = "";

    public string Command { get; set; } = "";

    public string ArgsTemplate { get; set; } = "-p \"{prompt}\"{model} --dangerously-skip-permissions";

    public bool SupportsVariant { get; set; }

    /// <summary>レベルを渡す実際のフラグ名（opencode: --variant、claude: --effort 等、CLI ごとに異なる）。</summary>
    public string VariantFlag { get; set; } = "--variant";

    public bool PreferredForImages { get; set; }

    /// <summary>呼び出し元が model を明示しなかった場合に使う既定モデル。</summary>
    public string DefaultModel { get; set; } = "";

    /// <summary>呼び出し元が variant を明示しなかった場合に使う既定レベル。</summary>
    public string DefaultVariant { get; set; } = "";
}
