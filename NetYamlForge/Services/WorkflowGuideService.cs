using System.Data;
using Dapper;
using NetYamlForge.Models;

namespace NetYamlForge.Services;

public class WorkflowGuideService
{
    private readonly IDbConnection _db;
    private readonly ProjectScope _scope;
    private readonly ILogger<WorkflowGuideService> _logger;

    public WorkflowGuideService(
        IDbConnection db,
        ProjectScope scope,
        ILogger<WorkflowGuideService> logger)
    {
        _db = db;
        _scope = scope;
        _logger = logger;
    }

    private string ProjectName => _scope.Current.Name;

    public async Task<WorkflowGuideViewModel> BuildWorkflowDataAsync(
        List<string> userRoles, string userName)
    {
        var vm = new WorkflowGuideViewModel
        {
            UserRoles = userRoles,
            UserName = userName
        };

        var flows = new List<WorkflowFlow>
        {
            BuildSalesFlow(),
            BuildContractFlow(),
            BuildOrderFlow()
        };

        foreach (var flow in flows)
        {
            foreach (var step in flow.Steps)
            {
                await LoadStepStatusAsync(step);
                step.IsResponsible = userRoles.Any(r =>
                    step.ResponsibleRoles.Contains(r, StringComparer.OrdinalIgnoreCase));
            }

            DetermineCurrentStep(flow);
        }

        vm.Flows = flows;
        return vm;
    }

    private async Task LoadStepStatusAsync(WorkflowStep step)
    {
        try
        {
            var sql = step.FlowId switch
            {
                "sales" => step.StepNo switch
                {
                    1 => "SELECT 0,0,0", // c_bpartner master - no status tracking
                    2 => @"SELECT
                        COALESCE(SUM(CASE WHEN docstatus = 'DR' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus = 'IN' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus IN ('CO','CL') THEN 1 ELSE 0 END),0)
                    FROM jp_estimation WHERE isactive = 'Y'",
                    3 => @"SELECT
                        COALESCE(SUM(CASE WHEN docstatus IN ('DR','IN') THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus='CO' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus IN ('CL','VO') THEN 1 ELSE 0 END),0)
                    FROM jp_contract WHERE isactive = 'Y'",
                    4 => @"SELECT
                        COALESCE(SUM(CASE WHEN docstatus='DR' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus='CO' THEN 1 ELSE 0 END),0),
                        0
                    FROM jp_recognition WHERE isactive = 'Y'",
                    5 => @"SELECT
                        COALESCE(SUM(CASE WHEN docstatus='DR' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus='CO' AND openamt>0 THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus='CL' THEN 1 ELSE 0 END),0)
                    FROM jp_bill WHERE isactive = 'Y'",
                    6 => @"SELECT
                        0,
                        COALESCE(SUM(CASE WHEN docstatus='DR' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus='CO' THEN 1 ELSE 0 END),0)
                    FROM c_payment WHERE isactive = 'Y' AND isreceipt = 'Y'",
                    _ => "SELECT 0,0,0"
                },
                "contract" => step.StepNo switch
                {
                    1 => @"SELECT
                        COALESCE(SUM(CASE WHEN docstatus IN ('DR','IN') THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus='CO' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus IN ('CL','VO') THEN 1 ELSE 0 END),0)
                    FROM jp_contract WHERE isactive = 'Y'",
                    2 => @"SELECT 0,
                        COALESCE(SUM(CASE WHEN docstatus='CO' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus='CL' THEN 1 ELSE 0 END),0)
                    FROM jp_contract WHERE isactive = 'Y'",
                    3 => @"SELECT
                        COALESCE(SUM(CASE WHEN docstatus='DR' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus='CO' AND openamt>0 THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus='CL' THEN 1 ELSE 0 END),0)
                    FROM jp_bill WHERE isactive = 'Y'",
                    4 => @"SELECT
                        COALESCE(SUM(CASE WHEN docstatus='DR' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus='CO' THEN 1 ELSE 0 END),0),
                        0
                    FROM jp_recognition WHERE isactive = 'Y'",
                    5 => @"SELECT 0,
                        COALESCE(SUM(CASE WHEN docstatus='CO' AND jp_contractperioddate_to IS NOT NULL
                            AND julianday(jp_contractperioddate_to)-julianday('now') BETWEEN 0 AND 90 THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus='CL' THEN 1 ELSE 0 END),0)
                    FROM jp_contract WHERE isactive = 'Y'",
                    _ => "SELECT 0,0,0"
                },
                "order" => step.StepNo switch
                {
                    1 => @"SELECT
                        COALESCE(SUM(CASE WHEN docstatus='DR' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus IN ('IN','CO') THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus='CL' THEN 1 ELSE 0 END),0)
                    FROM c_order WHERE isactive = 'Y' AND issotrx = 'Y'",
                    2 => @"SELECT
                        COALESCE(SUM(CASE WHEN docstatus='DR' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus='CO' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus='CL' THEN 1 ELSE 0 END),0)
                    FROM c_invoice WHERE isactive = 'Y' AND issotrx = 'Y'",
                    3 => @"SELECT
                        0,
                        COALESCE(SUM(CASE WHEN docstatus='DR' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN docstatus='CO' THEN 1 ELSE 0 END),0)
                    FROM c_payment WHERE isactive = 'Y' AND isreceipt = 'Y'",
                    _ => "SELECT 0,0,0"
                },
                _ => "SELECT 0,0,0"
            };

            var result = await _db.QuerySingleAsync<(int draft, int progress, int done)>(sql);
            step.DraftCount = result.draft;
            step.InProgressCount = result.progress;
            step.CompletedCount = result.done;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load status for step {FlowId}/{StepNo}",
                step.FlowId, step.StepNo);
        }
    }

    private static void DetermineCurrentStep(WorkflowFlow flow)
    {
        WorkflowStep? lastNonCompleted = null;
        foreach (var step in flow.Steps)
        {
            var hasDraft = step.DraftCount > 0;
            var hasProgress = step.InProgressCount > 0;
            var hasDone = step.CompletedCount > 0;

            if (hasDraft || hasProgress)
            {
                step.Status = WorkflowStepStatus.InProgress;
                lastNonCompleted = step;
            }
            else if (hasDone)
            {
                step.Status = WorkflowStepStatus.Completed;
            }
            else
            {
                step.Status = WorkflowStepStatus.NotStarted;
            }
        }

        if (lastNonCompleted != null)
        {
            lastNonCompleted.Status = WorkflowStepStatus.Current;
        }
        else if (flow.Steps.Count > 0)
        {
            var firstNotStarted = flow.Steps.FirstOrDefault(s => s.Status == WorkflowStepStatus.NotStarted);
            if (firstNotStarted != null)
            {
                firstNotStarted.Status = WorkflowStepStatus.Current;
            }
            else
            {
                flow.Steps.Last().Status = WorkflowStepStatus.Current;
            }
        }
    }

    private static WorkflowFlow BuildSalesFlow()
    {
        return new WorkflowFlow
        {
            FlowId = "sales",
            Title = "销售流程",
            Description = "业务伙伴登录 → 估价 → 契约 → 收入确认 → 账单 → 回款",
            Icon = "📈",
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    StepNo = 1, FlowId = "sales",
                    Title = "业务伙伴登录",
                    Description = "登录客户及供应商主数据。设置法人编号、信用额度及付款条件。",
                    Icon = "🏢", TargetUrl = "/{project}/DynamicEntity/Index?entity=c_bpartner",
                    ResponsibleRoles = new() { "employee", "contract_manager" }
                },
                new()
                {
                    StepNo = 2, FlowId = "sales",
                    Title = "创建与提交估价",
                    Description = "向业务伙伴创建估价单（EST-），将 docstatus 改为 IN（提交中）后发送。确认订单后改为 CO（已确定）。",
                    Icon = "📋", TargetUrl = "/{project}/DynamicEntity/Index?entity=jp_estimation",
                    ResponsibleRoles = new() { "contract_manager", "employee" }
                },
                new()
                {
                    StepNo = 3, FlowId = "sales",
                    Title = "签订契约",
                    Description = "订单确认后创建契约书（CON-），将 docstatus 改为 CO 使其生效。注意设定月营收及有效期间。",
                    Icon = "📄", TargetUrl = "/{project}/DynamicEntity/Index?entity=jp_contract",
                    ResponsibleRoles = new() { "contract_manager" }
                },
                new()
                {
                    StepNo = 4, FlowId = "sales",
                    Title = "月次收入确认",
                    Description = "每月将实绩以收入确认（REC-）记录。将 docstatus 改为 CO 确定后，系统自动生成会计分录。",
                    Icon = "📊", TargetUrl = "/{project}/DynamicEntity/Index?entity=jp_recognition",
                    ResponsibleRoles = new() { "accountant" }
                },
                new()
                {
                    StepNo = 5, FlowId = "sales",
                    Title = "发行账单",
                    Description = "通过月次批处理或手动创建账单（BILL-）。确定后自动生成应收款会计分录。",
                    Icon = "💴", TargetUrl = "/{project}/DynamicEntity/Index?entity=jp_bill",
                    ResponsibleRoles = new() { "contract_manager", "accountant" }
                },
                new()
                {
                    StepNo = 6, FlowId = "sales",
                    Title = "确认回款与核销",
                    Description = "确认收款后记录付款（PAY-）。openamt=0 时账单自动转为 CL（回款完成）。",
                    Icon = "🏦", TargetUrl = "/{project}/DynamicEntity/Index?entity=c_payment",
                    ResponsibleRoles = new() { "accountant" }
                }
            }
        };
    }

    private static WorkflowFlow BuildContractFlow()
    {
        return new WorkflowFlow
        {
            FlowId = "contract",
            Title = "合同管理流程",
            Description = "合同协商 → 签订 → 月次账单 → 收入确认 → 续约/解约",
            Icon = "📑",
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    StepNo = 1, FlowId = "contract",
                    Title = "合同协商",
                    Description = "从估价创建合同，或直接创建新合同。起草合同书，以草稿（DR）状态管理。",
                    Icon = "🤝", TargetUrl = "/{project}/DynamicEntity/Index?entity=jp_contract",
                    ResponsibleRoles = new() { "contract_manager" }
                },
                new()
                {
                    StepNo = 2, FlowId = "contract",
                    Title = "合同签订与生效",
                    Description = "达成合意后将 docstatus 改为 CO 使其生效。该合同将成为月次收入确认与账单的依据。",
                    Icon = "✅", TargetUrl = "/{project}/DynamicEntity/Index?entity=jp_contract",
                    ResponsibleRoles = new() { "contract_manager" }
                },
                new()
                {
                    StepNo = 3, FlowId = "contract",
                    Title = "月次账单（批处理/手动）",
                    Description = "每月1日的批处理任务将根据合同自动生成账单。如有需要可手动调整。",
                    Icon = "📅", TargetUrl = "/{project}/BatchJob/Index",
                    ResponsibleRoles = new() { "accountant", "contract_manager" }
                },
                new()
                {
                    StepNo = 4, FlowId = "contract",
                    Title = "收入确认",
                    Description = "将月次实绩作为收入确认记录并确定。确定时自动生成会计分录。",
                    Icon = "📊", TargetUrl = "/{project}/DynamicEntity/Index?entity=jp_recognition",
                    ResponsibleRoles = new() { "accountant" }
                },
                new()
                {
                    StepNo = 5, FlowId = "contract",
                    Title = "合同续约与解约管理",
                    Description = "到期前确认自动续约设置。续约时延长有效期，解约时将 docstatus 改为 CL。",
                    Icon = "🔄", TargetUrl = "/{project}/DynamicEntity/Index?entity=jp_contract",
                    ResponsibleRoles = new() { "contract_manager" }
                }
            }
        };
    }

    private static WorkflowFlow BuildOrderFlow()
    {
        return new WorkflowFlow
        {
            FlowId = "order",
            Title = "订单与付款流程",
            Description = "销售订单 → 发票 → 收款",
            Icon = "📦",
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    StepNo = 1, FlowId = "order",
                    Title = "创建销售订单",
                    Description = "根据客户需求创建销售订单（SO-），确认数量、单价及交期后将 docstatus 改为 CO。",
                    Icon = "🛒", TargetUrl = "/{project}/DynamicEntity/Index?entity=c_order",
                    ResponsibleRoles = new() { "employee", "contract_manager" }
                },
                new()
                {
                    StepNo = 2, FlowId = "order",
                    Title = "生成发票",
                    Description = "根据销售订单生成发票（INV-），确认内容后将 docstatus 改为 CO，自动生成应收款分录。",
                    Icon = "📄", TargetUrl = "/{project}/DynamicEntity/Index?entity=c_invoice",
                    ResponsibleRoles = new() { "accountant", "contract_manager" }
                },
                new()
                {
                    StepNo = 3, FlowId = "order",
                    Title = "确认收款",
                    Description = "收到付款后创建收款记录（PAY-），确定后应收款余额减少，发票完成核销。",
                    Icon = "💰", TargetUrl = "/{project}/DynamicEntity/Index?entity=c_payment",
                    ResponsibleRoles = new() { "accountant" }
                }
            }
        };
    }
}
