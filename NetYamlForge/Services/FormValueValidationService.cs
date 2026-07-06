// ファイル概要: フォーム送信値の型変換とバリデーションを共通化するサービスです。
// Entity/Section どちらでも使用できるよう、簡易フィールド仕様に変換して処理します。

using NetYamlForge.Models;
using NetYamlForge.Services.Validation;
using NetYamlForge.Services.Connection;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace NetYamlForge.Services;

public sealed record FormFieldSpec(
    string Name,
    string Type,
    bool Required,
    bool Editable = true,
    List<ValidatorDefinition>? Validators = null);

/// <summary>
/// フォーム送信値の型変換とバリデーションを共通化するサービス。
/// </summary>
public sealed class FormValueValidationService
{
    private readonly IValueConverter _converter;
    private readonly IEnumerable<IFieldValidator> _validators;
    private readonly IProjectValidatorHookRegistry _hookRegistry;
    private readonly IConnectionManager _connectionManager;

    public FormValueValidationService(IValueConverter converter)
        : this(
            converter,
            new IFieldValidator[]
            {
                new RegexFieldValidator(),
                new RangeFieldValidator(),
                new ConditionalFieldValidator()
            },
            new ProjectValidatorHookRegistry(),
            null!)
    {
    }

    public FormValueValidationService(
        IValueConverter converter,
        IEnumerable<IFieldValidator> validators,
        IProjectValidatorHookRegistry hookRegistry,
        IConnectionManager connectionManager)
    {
        _converter = converter;
        _validators = validators;
        _hookRegistry = hookRegistry;
        _connectionManager = connectionManager;
    }

    /// <summary>
    /// フォーム値をフィールド仕様に従って変換・検証します（同期版、カスタムDBフックはスキップ）。
    /// </summary>
    public (Dictionary<string, object?> values, Dictionary<string, string> errors) ConvertAndValidate(
        IEnumerable<FormFieldSpec> fields,
        Dictionary<string, string?> form,
        bool isPartial = false)
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

            if (isPartial && !hasField)
            {
                continue;
            }

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

                // 同期高級検証（regex, range, required_if）
                if (field.Validators != null && field.Validators.Any())
                {
                    foreach (var validatorDef in field.Validators)
                    {
                        var validator = _validators.FirstOrDefault(v => v.ValidatorType.Equals(validatorDef.Type, StringComparison.OrdinalIgnoreCase));
                        if (validator != null)
                        {
                            var result = validator.Validate(val, values, validatorDef);
                            if (!result.IsValid)
                            {
                                errors[field.Name] = result.ErrorMessage ?? "Validation failed";
                                break; // このフィールドの検証はここで終了
                            }
                        }
                    }
                }
            }
        }

        return (values, errors);
    }

    /// <summary>
    /// フォーム値をフィールド仕様に従って変換・検証します（非同期版、カスタムDBフックも実行）。
    /// </summary>
    public async Task<(Dictionary<string, object?> values, Dictionary<string, string> errors)> ConvertAndValidateAsync(
        IEnumerable<FormFieldSpec> fields,
        Dictionary<string, string?> form,
        string projectName,
        bool isPartial = false)
    {
        // 1. 同期チェックを実行
        var (values, errors) = ConvertAndValidate(fields, form, isPartial);

        // 2. 既にエラーがある場合はカスタムDB検証に進まない
        if (errors.Any())
        {
            return (values, errors);
        }

        // 3. 非同期のカスタムDB検証フックを実行
        IDbConnection? conn = null;
        try
        {
            foreach (var field in fields)
            {
                if (!field.Editable)
                {
                    continue;
                }

                // フォームに含まれていて正常に変換された値のみ検証対象
                if (!values.TryGetValue(field.Name, out var val))
                {
                    continue;
                }

                if (field.Validators != null && field.Validators.Any())
                {
                    foreach (var validatorDef in field.Validators)
                    {
                        if (string.Equals(validatorDef.Type, "custom_hook", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(validatorDef.Type, "hook", StringComparison.OrdinalIgnoreCase))
                        {
                            var hookName = validatorDef.HookName;
                            if (string.IsNullOrEmpty(hookName))
                            {
                                continue;
                            }

                            var hook = _hookRegistry.Find(projectName, hookName);
                            if (hook != null)
                            {
                                if (conn == null)
                                {
                                    conn = await _connectionManager.GetConnectionAsync(projectName);
                                }

                                var result = await hook.ValidateAsync(val, values, conn);
                                if (!result.IsValid)
                                {
                                    errors[field.Name] = result.ErrorMessage ?? $"Hook {hookName} validation failed";
                                    break; // このフィールドの検証はここで終了
                                }
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            if (conn != null)
            {
                _connectionManager.ReleaseConnection(conn);
            }
        }

        return (values, errors);
    }
}
