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
    private readonly FormValueValidationService _validator;

    public DynamicEntityFormValidationService(FormValueValidationService validator)
    {
        _validator = validator;
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
        var fields = meta.Columns.Select(kv =>
        {
            var name = kv.Key;
            var col = kv.Value;
            var editable = !(col.Identity || !string.IsNullOrWhiteSpace(col.Expression));
            return new FormFieldSpec(name, col.Type, col.Required, editable);
        });

        return _validator.ConvertAndValidate(fields, form);
    }
}
