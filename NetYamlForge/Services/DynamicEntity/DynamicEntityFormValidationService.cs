// ファイル概要: HTTP フォームから受け取った文字列値を、エンティティ定義の型情報に基づいて
// 適切な .NET 型に変換し、バリデーションエラーを収集するサービスです。
// Controller がこのサービスを呼んで values / errors ペアを取得し、
// エラーがなければ CommandService に渡します。

using NetYamlForge.Models;

namespace NetYamlForge.Services;

/// <summary>
/// フォーム送信値の型変換とバリデーションを行うサービス。
/// IValueConverter に変換ロジックを委譲し、identity 列をスキップして
/// 変換済み値マップとエラーマップを返します。
/// </summary>
public sealed class DynamicEntityFormValidationService
{
    private readonly IValueConverter _converter;

    public DynamicEntityFormValidationService(IValueConverter converter)
    {
        _converter = converter;
    }

    /// <summary>
    /// フォーム値を EntityDefinition の列型定義に従って変換・検証します。
    /// </summary>
    /// <param name="meta">エンティティ定義（columns セクションの型情報を使用）</param>
    /// <param name="form">IFormCollection から取り出した文字列値マップ</param>
    /// <returns>
    /// values: 変換済み値マップ（INSERT/UPDATE の引数になる）、
    /// errors: フィールド名 → エラーメッセージ（空の場合は検証通過）
    /// </returns>
    public (Dictionary<string, object?> values, Dictionary<string, string> errors) ConvertAndValidate(
        EntityDefinition meta,
        Dictionary<string, string?> form)
    {
        var values = new Dictionary<string, object?>();
        var errors = new Dictionary<string, string>();

        foreach (var kv in meta.Columns)
        {
            var name = kv.Key;
            var col = kv.Value;

            // identity 列（自動採番）はフォームから受け取らない
            // expression 列（JOIN等の計算列）は実テーブルに存在しないためスキップ
            if (col.Identity || !string.IsNullOrWhiteSpace(col.Expression))
            {
                continue;
            }

            form.TryGetValue(name, out var raw);

            // bool/チェックボックス列: フォームに含まれない = チェックなし = false として扱う
            if (col.Type.Equals("bool", StringComparison.OrdinalIgnoreCase) && !form.ContainsKey(name))
            {
                raw = "false";
            }

            if (!_converter.TryConvert(raw, col, out var val, out var error))
            {
                errors[name] = error ?? "Invalid value";
            }
            else
            {
                values[name] = val;
            }
        }

        return (values, errors);
    }
}
