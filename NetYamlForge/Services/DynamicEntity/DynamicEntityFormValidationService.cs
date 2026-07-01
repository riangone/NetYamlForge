// ファイル概要: HTTP フォームから受け取った文字列値を、エンティティ定義の型情報に基づいて
// 適切な .NET 型に変換し、バリデーションエラーを収集する服务。
// Controller がこのサービスを呼んで values / errors ペアを取得し、
// エラーがなければ CommandService に渡します。

using NetYamlForge.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetYamlForge.Services;

/// <summary>
/// フォーム送信値の型変換とバリデーションを行うサービス。
/// IValueConverter に変換ロジックを委譲し、identity 列をスキップして
/// 変換済み値マップとエラーマップを返します。
/// </summary>
public sealed class DynamicEntityFormValidationService
{
    private readonly FormValueValidationService _validator;

    /// <summary>
    /// FormValueValidationService を注入するコンストラクタ（DI コンテナ向け）。
    /// </summary>
    public DynamicEntityFormValidationService(FormValueValidationService validator)
    {
        _validator = validator;
    }

    /// <summary>
    /// フォーム値を EntityDefinition の列型定義に従って変換・検証します（同期版）。
    /// </summary>
    public (Dictionary<string, object?> values, Dictionary<string, string> errors) ConvertAndValidate(
        EntityDefinition meta,
        Dictionary<string, string?> form)
    {
        var fields = meta.Columns.Select(kv =>
        {
            var name = kv.Key;
            var col = kv.Value;
            var editable = !(col.Identity || !string.IsNullOrWhiteSpace(col.Expression));
            return new FormFieldSpec(name, col.Type, col.Required, editable, col.Validators);
        });

        return _validator.ConvertAndValidate(fields, form);
    }

    /// <summary>
    /// フォーム値を EntityDefinition の列型定義に従って変換・検証します（非同期版）。
    /// </summary>
    public async Task<(Dictionary<string, object?> values, Dictionary<string, string> errors)> ConvertAndValidateAsync(
        EntityDefinition meta,
        Dictionary<string, string?> form,
        string projectName)
    {
        var fields = meta.Columns.Select(kv =>
        {
            var name = kv.Key;
            var col = kv.Value;
            var editable = !(col.Identity || !string.IsNullOrWhiteSpace(col.Expression));
            return new FormFieldSpec(name, col.Type, col.Required, editable, col.Validators);
        });

        return await _validator.ConvertAndValidateAsync(fields, form, projectName);
    }
}
