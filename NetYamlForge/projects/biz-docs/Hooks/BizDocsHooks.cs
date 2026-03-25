// biz-docs プロジェクト固有フック
// 各種ビジネス文書の PDF エクスポートで使用するカスタムフック実装

using System;
using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.BizDocs.Hooks;

/// <summary>
/// 報価単のステータスを検証するフック。
/// YAML: beforeCreate: [validate_quotation_status]
/// </summary>
public class ValidateQuotationStatusHook : IEntityHook
{
    public string Name => "validate_quotation_status";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("status", out var status))
        {
            var validStatuses = new[] { "draft", "sent", "accepted", "rejected", "expired" };
            if (!Array.Exists(validStatuses, s => s == status?.ToString()))
                return Task.FromResult(HookResult.Abort("ステータスは無効な値です（draft/sent/accepted/rejected/expired のいずれか）。"));
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 請款書のステータスを検証するフック。
/// YAML: beforeCreate: [validate_invoice_status]
/// </summary>
public class ValidateInvoiceStatusHook : IEntityHook
{
    public string Name => "validate_invoice_status";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("status", out var status))
        {
            var validStatuses = new[] { "draft", "issued", "paid", "overdue", "cancelled" };
            if (!Array.Exists(validStatuses, s => s == status?.ToString()))
                return Task.FromResult(HookResult.Abort("ステータスは無効な値です（draft/issued/paid/overdue/cancelled のいずれか）。"));
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 報関単のステータスを検証するフック。
/// YAML: beforeCreate: [validate_customs_declaration_status]
/// </summary>
public class ValidateCustomsDeclarationStatusHook : IEntityHook
{
    public string Name => "validate_customs_declaration_status";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("status", out var status))
        {
            var validStatuses = new[] { "draft", "submitted", "approved", "rejected", "cleared" };
            if (!Array.Exists(validStatuses, s => s == status?.ToString()))
                return Task.FromResult(HookResult.Abort("ステータスは無効な値です（draft/submitted/approved/rejected/cleared のいずれか）。"));
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 税率が 0-100 の範囲であることを検証するフック。
/// YAML: beforeCreate: [validate_tax_rate]
/// </summary>
public class ValidateTaxRateHook : IEntityHook
{
    public string Name => "validate_tax_rate";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("taxRate", out var rate) || ctx.Values.TryGetValue("TaxRate", out rate))
        {
            var rateStr = rate?.ToString() ?? "0";
            if (decimal.TryParse(rateStr, out var rateVal) && (rateVal < 0 || rateVal > 100))
                return Task.FromResult(HookResult.Abort("税率は 0-100 の範囲で入力してください。"));
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 金額が 0 以上であることを検証するフック。
/// YAML: beforeCreate: [validate_amount_positive]
/// </summary>
public class ValidateAmountPositiveHook : IEntityHook
{
    public string Name => "validate_amount_positive";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var amountFields = new[] { "Total", "Subtotal", "TaxAmount", "PaidAmount", "Amount" };
        foreach (var field in amountFields)
        {
            if (ctx.Values.TryGetValue(field, out var amount))
            {
                var amountStr = amount?.ToString() ?? "0";
                if (decimal.TryParse(amountStr, out var amountVal) && amountVal < 0)
                    return Task.FromResult(HookResult.Abort($"{field} は 0 以上の値を入力してください。"));
            }
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// PDF テンプレートのステータスを検証するフック。
/// YAML: beforeCreate: [validate_pdf_template_status]
/// </summary>
public class ValidatePdfTemplateStatusHook : IEntityHook
{
    public string Name => "validate_pdf_template_status";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("status", out var status))
        {
            var validStatuses = new[] { "active", "inactive", "draft" };
            if (!Array.Exists(validStatuses, s => s == status?.ToString()))
                return Task.FromResult(HookResult.Abort("ステータスは無効な値です（active/inactive/draft のいずれか）。"));
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 通貨コードを検証するフック。
/// YAML: beforeCreate: [validate_currency]
/// </summary>
public class ValidateCurrencyHook : IEntityHook
{
    public string Name => "validate_currency";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("currency", out var currency) || ctx.Values.TryGetValue("Currency", out currency))
        {
            var validCurrencies = new[] { "USD", "EUR", "CNY", "JPY" };
            if (!Array.Exists(validCurrencies, c => c == currency?.ToString()))
                return Task.FromResult(HookResult.Abort("通貨は無効な値です（USD/EUR/CNY/JPY のいずれか）。"));
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
