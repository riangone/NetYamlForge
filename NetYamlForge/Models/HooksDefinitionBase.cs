// ファイル概要: EntityHooksDefinition と SectionHooksDefinition に共通するプリセット展開ロジックの基底クラス。

namespace NetYamlForge.Models;

/// <summary>
/// CRUD フック定義の基底クラス。
/// @presetName 形式のプリセット参照を展開する共通アルゴリズムを一元化する。
/// 派生クラスは <see cref="GetPreset"/> を実装してプリセット辞書へのアクセスを提供する。
/// </summary>
public abstract class HooksDefinitionBase
{
    /// <summary>
    /// 指定されたプリセット名に対応するフック名リストを返す。
    /// 未定義の場合は null を返す。
    /// </summary>
    protected abstract IReadOnlyList<string>? GetPreset(string presetName);

    /// <summary>
    /// フック名リストに含まれる @preset 参照を再帰的に展開し、実行順リストを返す。
    /// 循環参照は <paramref name="onWarning"/> を呼び出して無視する。
    /// </summary>
    protected List<string> ExpandHookEntries(IEnumerable<string>? hooks, Action<string>? onWarning = null)
    {
        if (hooks == null) return new List<string>();
        var result = new List<string>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in hooks)
            ExpandEntry(h, result, visiting, onWarning);
        return result;
    }

    private void ExpandEntry(
        string? name,
        List<string> output,
        HashSet<string> visiting,
        Action<string>? onWarning)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var trimmed = name.Trim();
        if (!trimmed.StartsWith('@')) { output.Add(trimmed); return; }

        var presetName = trimmed[1..].Trim();
        if (string.IsNullOrWhiteSpace(presetName))
        {
            onWarning?.Invoke("Hook preset 名が空です。'@' のみは無効です。");
            return;
        }

        var preset = GetPreset(presetName);
        if (preset == null)
        {
            onWarning?.Invoke($"Hook preset '{presetName}' が定義されていません。");
            return;
        }

        if (!visiting.Add(presetName))
        {
            onWarning?.Invoke($"Hook preset '{presetName}' に循環参照があります。");
            return;
        }

        foreach (var h in preset)
            ExpandEntry(h, output, visiting, onWarning);
        visiting.Remove(presetName);
    }
}
