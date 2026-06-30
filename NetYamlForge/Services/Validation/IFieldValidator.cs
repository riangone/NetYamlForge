using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NetYamlForge.Models;

namespace NetYamlForge.Services.Validation;

public interface IFieldValidator
{
    string ValidatorType { get; }
    ValidationResult Validate(object? value, Dictionary<string, object?> rowContext, ValidatorDefinition config);
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }

    public static ValidationResult Success() => new() { IsValid = true };
    public static ValidationResult Failure(string? msg) => new() { IsValid = false, ErrorMessage = msg };
}

public interface ICustomValidatorHook
{
    string HookName { get; }
    Task<ValidationResult> ValidateAsync(object? value, Dictionary<string, object?> rowContext, IDbConnection db, IDbTransaction? tx = null);
}

public interface IProjectValidatorHookRegistry
{
    ICustomValidatorHook? Find(string projectName, string hookName);
    void Register(string projectName, ICustomValidatorHook hook);
    void Clear(string projectName);
}

public class ProjectValidatorHookRegistry : IProjectValidatorHookRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ICustomValidatorHook>> _projectHooks = 
        new(StringComparer.OrdinalIgnoreCase);

    public ICustomValidatorHook? Find(string projectName, string hookName)
    {
        if (_projectHooks.TryGetValue(projectName, out var hooks))
        {
            hooks.TryGetValue(hookName, out var hook);
            return hook;
        }
        return null;
    }

    public void Register(string projectName, ICustomValidatorHook hook)
    {
        var hooks = _projectHooks.GetOrAdd(projectName, _ => new ConcurrentDictionary<string, ICustomValidatorHook>(StringComparer.OrdinalIgnoreCase));
        hooks[hook.HookName] = hook;
    }

    public void Clear(string projectName)
    {
        _projectHooks.TryRemove(projectName, out _);
    }
}

public class RegexFieldValidator : IFieldValidator
{
    public string ValidatorType => "regex";

    public ValidationResult Validate(object? value, Dictionary<string, object?> rowContext, ValidatorDefinition config)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
            return ValidationResult.Success();

        var pattern = config.Pattern;
        if (string.IsNullOrEmpty(pattern))
            return ValidationResult.Success();

        try
        {
            var isMatch = Regex.IsMatch(value.ToString()!, pattern);
            return isMatch 
                ? ValidationResult.Success() 
                : ValidationResult.Failure(config.ErrorMessage ?? "格式不匹配。");
        }
        catch
        {
            return ValidationResult.Failure("正则表达式无效。");
        }
    }
}

public class RangeFieldValidator : IFieldValidator
{
    public string ValidatorType => "range";

    public ValidationResult Validate(object? value, Dictionary<string, object?> rowContext, ValidatorDefinition config)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
            return ValidationResult.Success();

        var valStr = value.ToString()!;

        if (double.TryParse(valStr, out var val))
        {
            if (double.TryParse(config.Min, out var min) && val < min)
                return ValidationResult.Failure(config.ErrorMessage ?? $"数值不能小于 {min}。");

            if (double.TryParse(config.Max, out var max) && val > max)
                return ValidationResult.Failure(config.ErrorMessage ?? $"数值不能大于 {max}。");
        }
        else if (DateTime.TryParse(valStr, out var dt))
        {
            if (DateTime.TryParse(config.Min, out var minDate) && dt < minDate)
                return ValidationResult.Failure(config.ErrorMessage ?? $"日期不能早于 {minDate:yyyy-MM-dd}。");

            if (DateTime.TryParse(config.Max, out var maxDate) && dt > maxDate)
                return ValidationResult.Failure(config.ErrorMessage ?? $"日期不能晚于 {maxDate:yyyy-MM-dd}。");
        }

        return ValidationResult.Success();
    }
}

public class ConditionalFieldValidator : IFieldValidator
{
    public string ValidatorType => "required_if";

    public ValidationResult Validate(object? value, Dictionary<string, object?> rowContext, ValidatorDefinition config)
    {
        var condition = config.Condition;
        if (string.IsNullOrEmpty(condition))
            return ValidationResult.Success();

        var parts = condition.Split(new[] { "==", "!=" }, StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            var fieldName = parts[0];
            var expectedVal = parts[1].Trim('\'', '"');
            var isEquals = condition.Contains("==");

            rowContext.TryGetValue(fieldName, out var actualRaw);
            var actualVal = actualRaw?.ToString() ?? "";

            bool conditionMet = isEquals 
                ? actualVal.Equals(expectedVal, StringComparison.OrdinalIgnoreCase) 
                : !actualVal.Equals(expectedVal, StringComparison.OrdinalIgnoreCase);

            if (conditionMet)
            {
                if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                {
                    return ValidationResult.Failure(config.ErrorMessage ?? $"当 {fieldName} 为 {expectedVal} 时，此字段为必填项。");
                }
            }
        }

        return ValidationResult.Success();
    }
}
