// ファイル概要: フォーム送信値の型変換とバリデーションを共通化するサービスです。
// Entity/Section どちらでも使用できるよう、簡易フィールド仕様に変換して処理します。

using NetYamlForge.Models;

namespace NetYamlForge.Services;

public sealed record FormFieldSpec(
    string Name,
    string Type,
    bool Required,
    bool Editable = true);

/// <summary>
/// フォーム送信値の型変換とバリデーションを共通化するサービス。
/// </summary>
public sealed class FormValueValidationService
{
    private readonly IValueConverter _converter;

    public FormValueValidationService(IValueConverter converter)
    {
        _converter = converter;
    }

    /// <summary>
    /// フォーム値をフィールド仕様に従って変換・検証します。
    /// </summary>
    public (Dictionary<string, object?> values, Dictionary<string, string> errors) ConvertAndValidate(
        IEnumerable<FormFieldSpec> fields,
        Dictionary<string, string?> form)
    {
        var values = new Dictionary<string, object?>();
        var errors = new Dictionary<string, string>();

        foreach (var field in fields)
        {
            if (!field.Editable)
            {
                continue;
            }

            var hasField = form.TryGetValue(field.Name, out var raw);

            // bool/チェックボックス列: フォームに含まれない = チェックなし = false として扱う
            if (field.Type.Equals("bool", StringComparison.OrdinalIgnoreCase) && !hasField)
            {
                raw = "false";
                hasField = true;
            }

            var colDef = new ColumnDefinition
            {
                Type = field.Type,
                Required = field.Required
            };

            if (!_converter.TryConvert(raw, colDef, out var val, out var error))
            {
                errors[field.Name] = error ?? "Invalid value";
            }
            else if (hasField)
            {
                // フォームに含まれないフィールドは values に含めず、
                // INSERT/UPDATE 時に DB の DEFAULT や既存値を保持させる
                values[field.Name] = val;
            }
        }

        return (values, errors);
    }
}
