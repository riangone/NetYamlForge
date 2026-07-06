

namespace NetYamlForge.Models;

// ── セクションフック定義 ─────────────────────────────────────────────────
/// <summary>
/// セクション CRUD フック定義。entities の EntityHooksDefinition に相当。
/// YAML キーは camelCase（beforeCreate / afterCreate 等）を使用します（entities と統一）。
/// フック名は EntityHookRegistry に登録された IEntityHook.Name と一致する必要があります。
/// @presetName 形式で presets に定義したフックリストを参照できます（entities と同様）。
/// </summary>
public class SectionHooksDefinition : HooksDefinitionBase
{
    public List<string> BeforeCreate { get; set; } = new();
    public List<string> AfterCreate  { get; set; } = new();
    public List<string> BeforeUpdate { get; set; } = new();
    public List<string> AfterUpdate  { get; set; } = new();
    public List<string> BeforeDelete { get; set; } = new();
    public List<string> AfterDelete  { get; set; } = new();
    /// <summary>
    /// 再利用可能なフックリスト。entities の hooks.presets に相当。
    /// YAML: hooks: { presets: { common: [trim, audit_log] }, beforeCreate: [@common] }
    /// </summary>
    public Dictionary<string, List<string>>? Presets { get; set; }

    /// <summary>プリセット名に対応するフック名リストを返す（HooksDefinitionBase の実装）。</summary>
    protected override IReadOnlyList<string>? GetPreset(string presetName)
    {
        if (Presets == null || !Presets.TryGetValue(presetName, out var preset)) return null;
        return preset.Count == 0 ? null : preset;
    }

    /// <summary>@preset 参照を展開した実行順リストを返す（HooksDefinitionBase.ExpandHookEntries を使用）。</summary>
    public List<string> GetExpandedHooks(List<string> hooks, Action<string>? onWarning = null)
        => hooks.Count == 0 ? hooks : ExpandHookEntries(hooks, onWarning);
}
