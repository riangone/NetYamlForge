// 自動生成スタブ: dotnet run -- --scaffold-missing-hooks --project=hospitalmgmt で生成
// 各クラスの BeforeAsync / AfterAsync にビジネスロジックを実装してください。

using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.HospitalMgmt.Hooks;

public sealed class AuditLogHook : IEntityHook
{
    private readonly ILogger<AuditLogHook> _logger;
    public string Name => "audit_log";

    public AuditLogHook(ILogger<AuditLogHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

public sealed class CalculatePatientShareHook : IEntityHook
{
    private readonly ILogger<CalculatePatientShareHook> _logger;
    public string Name => "calculate_patient_share";

    public CalculatePatientShareHook(ILogger<CalculatePatientShareHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

public sealed class CheckPrescriptionLimitsHook : IEntityHook
{
    private readonly ILogger<CheckPrescriptionLimitsHook> _logger;
    public string Name => "check_prescription_limits";

    public CheckPrescriptionLimitsHook(ILogger<CheckPrescriptionLimitsHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

public sealed class GeneratePatientCodeHook : IEntityHook
{
    private readonly ILogger<GeneratePatientCodeHook> _logger;
    public string Name => "generate_patient_code";

    public GeneratePatientCodeHook(ILogger<GeneratePatientCodeHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

public sealed class NowHook : IEntityHook
{
    private readonly ILogger<NowHook> _logger;
    public string Name => "now";

    public NowHook(ILogger<NowHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

public sealed class ValidateAppointmentDateHook : IEntityHook
{
    private readonly ILogger<ValidateAppointmentDateHook> _logger;
    public string Name => "validate_appointment_date";

    public ValidateAppointmentDateHook(ILogger<ValidateAppointmentDateHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

public sealed class ValidateBedStatusHook : IEntityHook
{
    private readonly ILogger<ValidateBedStatusHook> _logger;
    public string Name => "validate_bed_status";

    public ValidateBedStatusHook(ILogger<ValidateBedStatusHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
