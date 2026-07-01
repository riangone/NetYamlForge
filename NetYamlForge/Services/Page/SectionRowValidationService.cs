// ファイル概要: Section 側のフォームバリデーションサービス。
// DynamicEntityFormValidationService の Section 版として、SectionDefinition の
// フィールド型定義に基づいて HTTP フォーム値を変換・検証します。
// IValueConverter に変換ロジックを委譲し、変換済み値マップとエラーマップを返します。

using NetYamlForge.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
    /// フォーム値を SectionDefinition のフィールド型定義に従って変換・検証します（同期版）。
    /// </summary>
    public (Dictionary<string, object?> values, Dictionary<string, string> errors) ConvertAndValidate(
        SectionDefinition section,
        Dictionary<string, string?> form,
        string mode = "edit")
    {
        var fields = section.GetFormFields(mode)
            .Select(fieldName =>
            {
                var def = section.GetFieldDef(fieldName);
                return new FormFieldSpec(fieldName, def.Type, def.Required, def.Editable, def.Validators);
            });

        return _validator.ConvertAndValidate(fields, form);
    }

    /// <summary>
    /// フォーム値を SectionDefinition のフィールド型定义に従って変換・検証します（非同期版）。
    /// </summary>
    public async Task<(Dictionary<string, object?> values, Dictionary<string, string> errors)> ConvertAndValidateAsync(
        SectionDefinition section,
        Dictionary<string, string?> form,
        string projectName,
        string mode = "edit")
    {
        var fields = section.GetFormFields(mode)
            .Select(fieldName =>
            {
                var def = section.GetFieldDef(fieldName);
                return new FormFieldSpec(fieldName, def.Type, def.Required, def.Editable, def.Validators);
            });

        return await _validator.ConvertAndValidateAsync(fields, form, projectName);
    }
}
