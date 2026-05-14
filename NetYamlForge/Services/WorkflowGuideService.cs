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
            BuildProcurementFlow(),
            BuildApprovalFlow()
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
                    1 => "SELECT 0,0,0", // business_partner master - no status tracking
                    2 => @"SELECT
                        COALESCE(SUM(CASE WHEN doc_status IN ('DR') THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN doc_status IN ('IN') THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN doc_status IN ('CO','CL') THEN 1 ELSE 0 END),0)
                    FROM estimations WHERE is_active = 1",
                    3 => @"SELECT
                        COALESCE(SUM(CASE WHEN doc_status IN ('DR','IN') THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN doc_status='CO' AND contract_status IN ('WP') THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN contract_status IN ('AC','EX','CL') THEN 1 ELSE 0 END),0)
                    FROM contracts WHERE is_active = 1",
                    4 => @"SELECT
                        COALESCE(SUM(CASE WHEN doc_status='DR' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN doc_status='CO' THEN 1 ELSE 0 END),0),
                        0
                    FROM recognitions WHERE is_active = 1",
                    5 => @"SELECT
                        COALESCE(SUM(CASE WHEN doc_status='DR' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN doc_status='CO' AND outstanding_amt>0 THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN doc_status='CL' THEN 1 ELSE 0 END),0)
                    FROM bills WHERE is_active = 1",
                    6 => @"SELECT
                        0,
                        COALESCE(SUM(CASE WHEN doc_status='CO' AND outstanding_amt>0 THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN doc_status='CL' THEN 1 ELSE 0 END),0)
                    FROM bills WHERE is_active = 1",
                    _ => "SELECT 0,0,0"
                },
                "contract" => step.StepNo switch
                {
                    1 => @"SELECT
                        COALESCE(SUM(CASE WHEN doc_status IN ('DR','IN') THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN contract_status='WP' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN contract_status='AC' THEN 1 ELSE 0 END),0)
                    FROM contracts WHERE is_active = 1",
                    2 => @"SELECT 0,
                        COALESCE(SUM(CASE WHEN contract_status='AC' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN contract_status IN ('EX','CL') THEN 1 ELSE 0 END),0)
                    FROM contracts WHERE is_active = 1",
                    3 => @"SELECT
                        COALESCE(SUM(CASE WHEN b.doc_status='DR' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN b.doc_status='CO' AND b.outstanding_amt>0 THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN b.doc_status='CL' THEN 1 ELSE 0 END),0)
                    FROM bills b JOIN contracts c ON c.id = b.contract_id
                    WHERE b.is_active = 1 AND c.is_active = 1",
                    4 => @"SELECT
                        COALESCE(SUM(CASE WHEN r.doc_status='DR' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN r.doc_status='CO' THEN 1 ELSE 0 END),0),
                        0
                    FROM recognitions r JOIN contracts c ON c.id = r.contract_id
                    WHERE r.is_active = 1 AND c.is_active = 1",
                    5 => @"SELECT 0,
                        COALESCE(SUM(CASE WHEN contract_status='AC' AND period_date_to IS NOT NULL
                            AND julianday(period_date_to)-julianday('now') BETWEEN 0 AND 90 THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN contract_status IN ('EX','CL') THEN 1 ELSE 0 END),0)
                    FROM contracts WHERE is_active = 1",
                    _ => "SELECT 0,0,0"
                },
                "procurement" => step.StepNo switch
                {
                    1 => @"SELECT
                        COALESCE(SUM(CASE WHEN doc_status='DR' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN doc_status IN ('IP','CO') THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN doc_status='CL' THEN 1 ELSE 0 END),0)
                    FROM purchase_orders WHERE is_active = 1",
                    2 => @"SELECT
                        COALESCE(SUM(CASE WHEN ar.status='PENDING' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN ar.status='APPROVED' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN ar.status IN ('REJECTED','CANCELLED') THEN 1 ELSE 0 END),0)
                    FROM approval_requests ar
                    JOIN purchase_orders po ON po.id = ar.document_id AND ar.document_type='purchase_order'",
                    3 => @"SELECT
                        COALESCE(SUM(CASE WHEN pr.doc_status='DR' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN pr.doc_status='CO' THEN 1 ELSE 0 END),0),
                        0
                    FROM purchase_receipts pr WHERE pr.is_active = 1",
                    4 => @"SELECT
                        COALESCE(SUM(CASE WHEN ai.doc_status='DR' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN ai.doc_status='CO' AND ai.outstanding_amt>0 THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN ai.doc_status='CL' THEN 1 ELSE 0 END),0)
                    FROM ap_invoices ai WHERE ai.is_active = 1",
                    5 => @"SELECT
                        0,
                        COALESCE(SUM(CASE WHEN p.doc_status='DR' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN p.doc_status='CO' THEN 1 ELSE 0 END),0)
                    FROM payments p WHERE p.is_active = 1 AND p.payment_type='AP'",
                    _ => "SELECT 0,0,0"
                },
                "approval" => step.StepNo switch
                {
                    1 => @"SELECT
                        COALESCE(SUM(CASE WHEN status='PENDING' AND current_step=0 THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN status='PENDING' AND current_step>0 THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN status='APPROVED' THEN 1 ELSE 0 END),0)
                    FROM approval_requests WHERE is_active = 1",
                    2 => @"SELECT
                        COALESCE(SUM(CASE WHEN ar.status='PENDING' AND ast.status='PENDING' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN ast.status='APPROVED' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN ast.status='REJECTED' THEN 1 ELSE 0 END),0)
                    FROM approval_requests ar
                    JOIN approval_steps ast ON ast.approval_request_id = ar.id",
                    3 => @"SELECT
                        COALESCE(SUM(CASE WHEN ar.status='PENDING' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN ar.status='APPROVED' THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN ar.status='REJECTED' THEN 1 ELSE 0 END),0)
                    FROM approval_requests ar
                    WHERE ar.document_type IS NOT NULL",
                    4 => @"SELECT
                        COALESCE(SUM(CASE WHEN po.doc_status='DR' AND po.approval_status IS NOT NULL THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN po.doc_status IN ('CO','IP') THEN 1 ELSE 0 END),0),
                        COALESCE(SUM(CASE WHEN po.doc_status='VO' THEN 1 ELSE 0 END),0)
                    FROM purchase_orders po WHERE po.is_active = 1",
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
            Title = "売上フロー",
            Description = "取引先登録 → 見積 → 契約 → 売上認識 → 請求 → 入金",
            Icon = "📈",
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    StepNo = 1, FlowId = "sales",
                    Title = "取引先登録",
                    Description = "顧客・仕入先のマスタを登録します。法人番号・与信限度額・支払条件も設定します。",
                    Icon = "🏢", TargetUrl = "/{project}/DynamicEntity/Index?entity=business_partner",
                    ResponsibleRoles = new() { "employee", "contract_manager" }
                },
                new()
                {
                    StepNo = 2, FlowId = "sales",
                    Title = "見積作成・提出",
                    Description = "取引先に見積書(EST-)を作成し、doc_status を IN（提出中）に変更して送付。受注後は CO（受注確定）へ。",
                    Icon = "📋", TargetUrl = "/{project}/DynamicEntity/Index?entity=estimation",
                    ResponsibleRoles = new() { "contract_manager", "employee" }
                },
                new()
                {
                    StepNo = 3, FlowId = "sales",
                    Title = "契約締結",
                    Description = "受注確定後、契約書(CON-)を作成。contract_status を WP → AC に変更して有効化。月次売上・期間設定を忘れずに。",
                    Icon = "📄", TargetUrl = "/{project}/DynamicEntity/Index?entity=contract",
                    ResponsibleRoles = new() { "contract_manager" }
                },
                new()
                {
                    StepNo = 4, FlowId = "sales",
                    Title = "月次売上認識",
                    Description = "毎月の作業実績を売上認識(REC-)として記録。doc_status を CO に確定すると仕訳が自動起票されます。",
                    Icon = "📊", TargetUrl = "/{project}/DynamicEntity/Index?entity=recognition",
                    ResponsibleRoles = new() { "accountant" }
                },
                new()
                {
                    StepNo = 5, FlowId = "sales",
                    Title = "請求書発行",
                    Description = "月次バッチまたは手動で請求書(BILL-)を作成。CO 確定で売掛金仕訳が自動起票。",
                    Icon = "💴", TargetUrl = "/{project}/DynamicEntity/Index?entity=bill",
                    ResponsibleRoles = new() { "contract_manager", "accountant" }
                },
                new()
                {
                    StepNo = 6, FlowId = "sales",
                    Title = "入金確認・消込",
                    Description = "入金確認後、支払処理(PAY-)を記録。outstanding_amt=0 で請求書が CL（入金完了）に自動遷移。",
                    Icon = "🏦", TargetUrl = "/{project}/DynamicEntity/Index?entity=bill",
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
            Title = "契約管理フロー",
            Description = "契約交渉 → 締結 → 月次請求 → 売上認識 → 更新/解約",
            Icon = "📑",
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    StepNo = 1, FlowId = "contract",
                    Title = "契約交渉",
                    Description = "見積から契約を作成、または新規契約を交渉。契約書ドラフトを作成し WP 状態で管理。",
                    Icon = "🤝", TargetUrl = "/{project}/DynamicEntity/Index?entity=contract",
                    ResponsibleRoles = new() { "contract_manager" }
                },
                new()
                {
                    StepNo = 2, FlowId = "contract",
                    Title = "契約締結・有効化",
                    Description = "合意後 doc_status=CO、contract_status=AC に変更。月次売上認識と請求の基準となります。",
                    Icon = "✅", TargetUrl = "/{project}/DynamicEntity/Index?entity=contract",
                    ResponsibleRoles = new() { "contract_manager" }
                },
                new()
                {
                    StepNo = 3, FlowId = "contract",
                    Title = "月次請求（バッチ/手動）",
                    Description = "毎月1日のバッチジョブで契約から請求書が自動生成。必要に応じて手動調整も可能。",
                    Icon = "📅", TargetUrl = "/{project}/BatchJob/Index",
                    ResponsibleRoles = new() { "accountant", "contract_manager" }
                },
                new()
                {
                    StepNo = 4, FlowId = "contract",
                    Title = "売上認識",
                    Description = "月次作業実績を売上認識として記録・確定。確定時に自動で仕訳起票。",
                    Icon = "📊", TargetUrl = "/{project}/DynamicEntity/Index?entity=recognition",
                    ResponsibleRoles = new() { "accountant" }
                },
                new()
                {
                    StepNo = 5, FlowId = "contract",
                    Title = "契約更新・解約管理",
                    Description = "期限切れ前に自動更新設定を確認。更新は期間延長、解約は contract_status=CL に変更。",
                    Icon = "🔄", TargetUrl = "/{project}/DynamicEntity/Index?entity=contract",
                    ResponsibleRoles = new() { "contract_manager" }
                }
            }
        };
    }

    private static WorkflowFlow BuildProcurementFlow()
    {
        return new WorkflowFlow
        {
            FlowId = "procurement",
            Title = "購買フロー",
            Description = "発注 → 承認 → 受入 → 仕入請求 → 支払",
            Icon = "📦",
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    StepNo = 1, FlowId = "procurement",
                    Title = "発注書作成",
                    Description = "仕入先に発注書(PO-)を作成。ドラフトから必要に応じて承認ワークフローへ。",
                    Icon = "🛒", TargetUrl = "/{project}/DynamicEntity/Index?entity=purchase_order",
                    ResponsibleRoles = new() { "purchaser" }
                },
                new()
                {
                    StepNo = 2, FlowId = "procurement",
                    Title = "承認（ワークフロー）",
                    Description = "発注金額に応じて承認ルートが自動選択。10万未満:不要、10万〜100万:1段階、100万超:2段階。",
                    Icon = "✅", TargetUrl = "/{project}/DynamicEntity/Index?entity=approval_request",
                    ResponsibleRoles = new() { "purchaser", "approver" }
                },
                new()
                {
                    StepNo = 3, FlowId = "procurement",
                    Title = "受入処理",
                    Description = "発注した商品・サービスの受入を記録。在庫品は在庫移動も同時更新。",
                    Icon = "📦", TargetUrl = "/{project}/DynamicEntity/Index?entity=purchase_receipt",
                    ResponsibleRoles = new() { "purchaser" }
                },
                new()
                {
                    StepNo = 4, FlowId = "procurement",
                    Title = "仕入請求書処理",
                    Description = "仕入先から届いた請求書をAP請求として登録。3方向照合（数量・単価±5%・PO）を実施。",
                    Icon = "📄", TargetUrl = "/{project}/DynamicEntity/Index?entity=ap_invoice",
                    ResponsibleRoles = new() { "purchaser", "accountant" }
                },
                new()
                {
                    StepNo = 5, FlowId = "procurement",
                    Title = "支払処理",
                    Description = "確定したAP請求に対して支払を実行。支払時に仕訳が自動起票。買掛金残高が減少。",
                    Icon = "💰", TargetUrl = "/{project}/DynamicEntity/Index?entity=payment",
                    ResponsibleRoles = new() { "accountant" }
                }
            }
        };
    }

    private static WorkflowFlow BuildApprovalFlow()
    {
        return new WorkflowFlow
        {
            FlowId = "approval",
            Title = "承認ワークフロー",
            Description = "申請 → ステップ承認 → 完了/却下",
            Icon = "🔐",
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    StepNo = 1, FlowId = "approval",
                    Title = "承認申請",
                    Description = "発注書・契約書などから承認申請を作成。金額に応じて承認ルートが自動選択されます。",
                    Icon = "📝", TargetUrl = "/{project}/DynamicEntity/Index?entity=approval_request",
                    ResponsibleRoles = new() { "employee", "purchaser", "contract_manager" }
                },
                new()
                {
                    StepNo = 2, FlowId = "approval",
                    Title = "ステップ承認",
                    Description = "各承認者が順次承認/却下。全ステップ承認で元書類が確定、1つでも却下で無効に。",
                    Icon = "👤", TargetUrl = "/{project}/DynamicEntity/Index?entity=approval_step",
                    ResponsibleRoles = new() { "approver" }
                },
                new()
                {
                    StepNo = 3, FlowId = "approval",
                    Title = "承認完了・文書確定",
                    Description = "全承認完了で元の書類が自動確定(doc_status=CO)。却下の場合は VO に。",
                    Icon = "🎯", TargetUrl = "/{project}/Page/ApprovalInquiry",
                    ResponsibleRoles = new() { "approver", "admin" }
                },
                new()
                {
                    StepNo = 4, FlowId = "approval",
                    Title = "発注書確定・購買進行",
                    Description = "承認完了後、発注書が確定し購買フローが進行。購買担当者が出荷調整・受入準備。",
                    Icon = "🚀", TargetUrl = "/{project}/DynamicEntity/Index?entity=purchase_order",
                    ResponsibleRoles = new() { "purchaser" }
                }
            }
        };
    }
}
