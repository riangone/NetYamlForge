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
    private readonly FormValueValidationService _validator;

    public SectionRowValidationService(FormValueValidationService validator)
    {
        _validator = validator;
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
        var fields = section.GetFormFields(mode)
            .Select(fieldName =>
            {
                var def = section.GetFieldDef(fieldName);
                return new FormFieldSpec(fieldName, def.Type, def.Required, def.Editable);
            });

        return _validator.ConvertAndValidate(fields, form);
    }
}
