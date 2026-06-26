// 自動生成スタブ: dotnet run -- --scaffold-missing-hooks --project=hospitalmgmt で生成
// 各クラスの ExecuteAsync にビジネスロジックを実装してください。

using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.HospitalMgmt.Hooks;

public sealed class CancelAppointmentHandler : ICustomActionHandler
{
    private readonly ILogger<CancelAppointmentHandler> _logger;
    public string Name => "cancel_appointment";

    public CancelAppointmentHandler(ILogger<CancelAppointmentHandler> logger) => _logger = logger;

    public Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(ActionHandlerResult.Success());
    }
}

public sealed class CompleteAppointmentHandler : ICustomActionHandler
{
    private readonly ILogger<CompleteAppointmentHandler> _logger;
    public string Name => "complete_appointment";

    public CompleteAppointmentHandler(ILogger<CompleteAppointmentHandler> logger) => _logger = logger;

    public Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(ActionHandlerResult.Success());
    }
}

public sealed class DischargePatientHandler : ICustomActionHandler
{
    private readonly ILogger<DischargePatientHandler> _logger;
    public string Name => "discharge_patient";

    public DischargePatientHandler(ILogger<DischargePatientHandler> logger) => _logger = logger;

    public Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(ActionHandlerResult.Success());
    }
}

public sealed class MarkBillingAsPaidHandler : ICustomActionHandler
{
    private readonly ILogger<MarkBillingAsPaidHandler> _logger;
    public string Name => "mark_billing_as_paid";

    public MarkBillingAsPaidHandler(ILogger<MarkBillingAsPaidHandler> logger) => _logger = logger;

    public Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(ActionHandlerResult.Success());
    }
}

public sealed class ViewMedicalRecordsHandler : ICustomActionHandler
{
    private readonly ILogger<ViewMedicalRecordsHandler> _logger;
    public string Name => "view_medical_records";

    public ViewMedicalRecordsHandler(ILogger<ViewMedicalRecordsHandler> logger) => _logger = logger;

    public Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(ActionHandlerResult.Success());
    }
}
