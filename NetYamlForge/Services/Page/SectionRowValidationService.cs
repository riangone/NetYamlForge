// ファイル概要: Section 側のフォームバリデーションサービス。
// DynamicEntityFormValidationService の Section 版として、SectionDefinition の
// フィールド型定義に基づいて HTTP フォーム値を変換・検証します。
// IValueConverter に変換ロジックを委譲し、変換済み値マップとエラーマップを返します。

using NetYamlForge.Models;

namespace NetYamlForge.Services.Page;

/// <summary>
/// Section フォーム送信値の型変換とバリデーションを行うサービス。
/// IValueConverter に変換ロジックを委譲し、editable=false 列をスキップして
/// 変換済み値マップとエラーマップを返します。
/// </summary>
public sealed class SectionRowValidationService
{
    private readonly IValueConverter _converter;

    public SectionRowValidationService(IValueConverter converter)
    {
        _converter = converter;
    }

    /// <summary>
    /// フォーム値を SectionDefinition のフィールド型定義に従って変換・検証します。
    /// </summary>
    /// <param name="section">セクション定義（フィールド型・必須・編集可能フラグを使用）</param>
    /// <param name="form">IFormCollection から取り出した文字列値マップ</param>
    /// <param name="mode">フォームモード（"create" / "edit"）</param>
    /// <returns>
    /// values: 変換済み値マップ（INSERT/UPDATE の引数になる）、
    /// errors: フィールド名 → エラーメッセージ（空の場合は検証通過）
    /// </returns>
    public (Dictionary<string, object?> values, Dictionary<string, string> errors) ConvertAndValidate(
        SectionDefinition section,
        Dictionary<string, string?> form,
        string mode = "edit")
    {
        var values = new Dictionary<string, object?>();
        var errors = new Dictionary<string, string>();

        var fields = section.GetFormFields(mode);
        foreach (var fieldName in fields)
        {
            var fieldDef = section.GetFieldDef(fieldName);

            // editable=false のフィールドはスキップ
            if (!fieldDef.Editable) continue;

            form.TryGetValue(fieldName, out var raw);

            // bool/チェックボックス列: フォームに含まれない = チェックなし = false として扱う
            if (fieldDef.Type.Equals("bool", StringComparison.OrdinalIgnoreCase) && !form.ContainsKey(fieldName))
                raw = "false";

            // SectionFormFieldDef を ColumnDefinition に変換して IValueConverter に渡す
            var colDef = new ColumnDefinition
            {
                Type = fieldDef.Type,
                Required = fieldDef.Required
            };

            if (!_converter.TryConvert(raw, colDef, out var val, out var error))
                errors[fieldName] = error ?? "Invalid value";
            else
                values[fieldName] = val;
        }

        return (values, errors);
    }
}
