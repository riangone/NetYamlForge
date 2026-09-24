using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NetYamlForge.Models;
using NetYamlForge.Services;

namespace NetYamlForge.Controllers;

/// <summary>
/// 多场景样板间中心 (Showcase Gallery) 控制器：
/// 集中呈现 NetYamlForge 涵盖安全护栏、AI Agent 互联协议、企业级表单与工业级 ERP 的全场景落地矩阵。
/// 支持未登录访客与已登录用户无缝查阅、架构透视与直达体验。
/// </summary>
[AllowAnonymous]
public class ShowcaseController : Controller
{
    private readonly ProjectManager _projectManager;
    private readonly ProjectScope _projectScope;
    private readonly ILogger<ShowcaseController> _logger;

    public ShowcaseController(
        ProjectManager projectManager,
        ProjectScope projectScope,
        ILogger<ShowcaseController> logger)
    {
        _projectManager = projectManager;
        _projectScope = projectScope;
        _logger = logger;
    }

    [HttpGet]
    [HttpHead]
    [Route("showcase")]
    [Route("{project}/showcase")]
    public IActionResult Index([FromQuery] string? category = null)
    {
        var allShowcases = GetCatalog();

        var filtered = string.IsNullOrWhiteSpace(category) || category.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? allShowcases
            : allShowcases.Where(s => s.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();

        var currentProject = _projectScope.IsSet ? _projectScope.Current.Name : "agent-studio";

        var vm = new ShowcaseIndexViewModel
        {
            Showcases = filtered,
            CurrentCategory = category?.ToLowerInvariant() ?? "all",
            IsAuthenticated = User.Identity?.IsAuthenticated == true,
            CurrentUserProject = currentProject,
            PromotionalCopyMarkdown = GeneratePromotionalCopy()
        };

        return View(vm);
    }

    [HttpGet]
    [HttpHead]
    [Route("showcase/api/spec/{id}")]
    public IActionResult GetSpec(string id)
    {
        var item = GetCatalog().FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            return NotFound(new { success = false, message = $"未找到样板间: {id}" });
        }

        return Json(new
        {
            success = true,
            item.Id,
            item.Title,
            item.Category,
            item.Tagline,
            item.TargetAudience,
            item.ArchitectInsight,
            item.Highlights,
            item.TechTags,
            item.CodeSnippetTitle,
            item.CodeLanguage,
            item.CodeSnippet,
            item.PrimaryActionUrl,
            item.PrimaryActionLabel,
            item.IsBlueprintOnly
        });
    }

    private static List<ShowcaseItemViewModel> GetCatalog()
    {
        return new List<ShowcaseItemViewModel>
        {
            // 0. 旗舰前沿 AI 智能体自举演进工作台 (Live Runtime)
            new()
            {
                Id = "agent-studio",
                Title = "🤖 Agent Studio (智能体自举演进工作台)",
                Category = "ai",
                CategoryLabel = "前沿 AI 智能体",
                Badge = "Official Live",
                BadgeClass = "badge-success",
                Icon = "🤖",
                Tagline = "3 CLI 角色（Architect / Developer / Tester）闭环协作与自举演进工作台",
                Description = "NetYamlForge 阶段 2 核心落地的声明式 AI 协作引擎。基于 roles.yaml 动态元数据驱动与 Model Context Protocol (MCP) 端点，实现端到端的角色提示词装配、协同流水线调度与执行日志追踪。",
                TargetAudience = "AI 架构师、全栈工程师、Agent 开发者、自动化研发运维团队",
                ArchitectInsight = "摒弃代码中生硬的 switch-case 角色分支，将角色职责与交互准则全面 YAML 元数据化。配合 MCP 开放接口与确定性租户数据隔离，让 AI 协作既具备无限扩展性，又绝对 Safe by Construction。",
                Highlights = new List<string>
                {
                    "roles.yaml 声明式角色驱动：元数据配置化定义职责、准则与三阶段协同流水线",
                    "动态 CRUD 任务管理：通过 agent_task 与 agent_task_log 实时跟踪演进任务与执行步骤",
                    "MCP 标准协议端点：内置 EntityToolService 与 JSON-RPC 2.0，无缝对接外部分析器",
                    "Fail-Closed 租户熔断隔离：多租户会话与上下文强绑定，杜绝数据串话穿透"
                },
                TechTags = new List<string> { "Agent Studio", "Declarative Roles", "MCP Protocol", "Self-Evolution", "Fail-Closed" },
                PrimaryActionUrl = "/agent-studio/Dashboard",
                PrimaryActionLabel = "进入 Agent Studio 看板",
                SecondaryActionUrl = "/agent-studio/DynamicEntity/Index?entity=agent_task",
                SecondaryActionLabel = "🚀 协作任务工作台",
                CodeSnippetTitle = "roles.yaml (声明式 AI 角色与协作规范定义)",
                CodeLanguage = "yaml",
                CodeSnippet = @"roles:
  Architect:
    description: ""System Architect""
    system_prompt: |
      You are the project architect. Focus on high-level structure and design patterns.
      NetYamlForge is a lightweight .NET 10 Web framework driven by YAML configurations and C# hooks.
    responsibilities:
      - ""Analyze the business and technical requirements thoroughly.""
      - ""Define architectural boundaries, interfaces, and component interactions.""
      - ""Produce a clear Specification/RFC with concrete file paths and contract signatures.""
  Developer:
    description: ""Senior Software Developer""
    responsibilities:
      - ""Implement the feature or fix according to the Architect's specification.""
      - ""Write clean, robust, and idiomatic C# code targeting .NET 10.""
  Tester:
    description: ""QA & Test Engineer""
    responsibilities:
      - ""Design and write comprehensive xUnit unit and integration tests.""
      - ""Verify test cases against edge cases, failure modes, and regression risks.""",
                IsBlueprintOnly = false
            },

            // 1. 安全与架构基准标杆 (Architecture Blueprint)
            new()
            {
                Id = "security-demo",
                Title = "🛡️ Security Guardrails (安全护栏标杆)",
                Category = "security",
                CategoryLabel = "安全与架构基线",
                Badge = "Architecture Blueprint",
                BadgeClass = "badge-neutral",
                Icon = "🛡️",
                Tagline = "专为 AI Agent 生成代码打造的编译期与运行期安全底座",
                Description = "作为 NetYamlForge 框架重构后的核心官方标杆，完整演示了动态 CRUD 管道、聚合看板与默认拒绝策略。内置 AST 校验器，能在启动解析期直接阻断 AI 编写的恶意 SQL 与危险外键 Join，运行期彻底隔离路径穿越。（注：物理 Demo 已归档至 NetYamlForge.Tests/TestProjects 作为自动化回归基线）",
                TargetAudience = "技术总监、系统架构师、安全工程师、关注 AI Agent 落地生产环境的开发者",
                ArchitectInsight = "AI 生成的代码存在天然的不可控漏洞。NetYamlForge 不依赖黑盒提示词约束，而是在元数据加载期通过 AST 强校验直接熔断威胁，使开发者和 Agent 能在确定性安全的沙箱内敏捷协作。",
                Highlights = new List<string>
                {
                    "元数据解析期 AST 阻断：非白名单 Join 类型与恶意列标识符直接报错拦截",
                    "运行时路径遍历防御：强制项目基目录规范化隔离，阻断 ../ 逃逸",
                    "Default-Deny 默认拒绝原则：实体默认不对外暴露 REST / MCP API，需显式标注授权",
                    "内置红蓝对抗沙箱：规范定义已归档至集成测试套件作为确定性断言标准"
                },
                TechTags = new List<string> { "AST Guardrail", "SqlSafetyGuard", "PathSafetyGuard", "Default-Deny", "MCP Tool" },
                PrimaryActionUrl = "",
                PrimaryActionLabel = "",
                SecondaryActionUrl = null,
                SecondaryActionLabel = null,
                CodeSnippetTitle = "note.yml (声明式定义与生命周期 Hooks 挂载)",
                CodeLanguage = "yaml",
                CodeSnippet = @"entities:
  note:
    table: 'Note'
    key: 'Id'
    displayName: 'Note'
    api: enabled    # 显式声明 api: enabled，允许通过 REST / MCP 访问
    hooks:
      - generate_slug       # Title 自动转 slug
      - sanitize_content     # XSS 防护过滤
      - set_published_at     # 发布状态自动落时间戳
      - audit_log            # 变更记录审计日志
    columns:
      Id: { type: int, identity: true, label: 'ID' }
      Title: { type: string, required: true, label: 'Title', searchable: true }
      Slug: { type: string, label: 'Slug', searchable: true }
      Content: { type: string, label: 'Content' }
      Status: { type: string, label: 'Status', searchable: true }",
                IsBlueprintOnly = true
            },

            // 2. 前沿 AI 智能体协议 (Architecture Blueprint)
            new()
            {
                Id = "ai-card",
                Title = "🤖 AI Card & Handshake Protocol (AHP 协议)",
                Category = "ai",
                CategoryLabel = "前沿 AI 智能体",
                Badge = "Architecture Blueprint",
                BadgeClass = "badge-neutral",
                Icon = "🤖",
                Tagline = "基于声明式元数据与状态机 Hook 构建的 AI 时代智能名片与握手交互协议",
                Description = "演示人类与 AI、AI 与外部 Agent 之间的受控数字化连接。通过动态 QR 令牌生命周期、声明式身份管理、有限状态机拦截与自动多轮协商，实现去中心化的 Agent 身份互认。",
                TargetAudience = "AI 应用开发者、Agent 互联极客、智能助理开发团队、协议研究者",
                ArchitectInsight = "Agent-to-Agent 交互必须有明确的权限边界与会话状态转移保护。借助 NetYamlForge 的状态机 Hook 与生命周期守护，开发者用极低的代码成本即可构建生产级安全的智能体互联协议栈。",
                Highlights = new List<string>
                {
                    "有限状态机 Hook 守护：强制 pending → connected → completed 严格转移",
                    "动态 QR 令牌生命周期：支持过期自动失效与后台定期清理任务 (BatchJob)",
                    "双模接入兼容：普通用户浏览器交互 + AI Agent 标准 JSON 握手对话",
                    "完整业务实体：覆盖 ai_identity、ai_profile、handshake_session"
                },
                TechTags = new List<string> { "AHP Protocol", "State Machine Hook", "Dynamic QR", "AI Chat", "Session Guard" },
                PrimaryActionUrl = "",
                PrimaryActionLabel = "",
                SecondaryActionUrl = null,
                SecondaryActionLabel = null,
                CodeSnippetTitle = "HandshakeStateGuardHook.cs (状态机安全流转守卫)",
                CodeLanguage = "csharp",
                CodeSnippet = @"public sealed class HandshakeStateGuardHook : IEntityHook
{
    public string Name => ""handshake_state_guard"";

    // 严密的状态机迁移矩阵，防止会话状态被越权或反向篡改
    private static readonly Dictionary<string, HashSet<string>> ValidTransitions = new(StringComparer.OrdinalIgnoreCase)
    {
        [""pending""]   = new() { ""connected"", ""expired"", ""rejected"", ""revoked"" },
        [""connected""] = new() { ""completed"", ""expired"", ""rejected"", ""revoked"" },
        [""completed""] = new() { ""revoked"" },
        [""expired""]   = new() { },
        [""rejected""]  = new() { },
        [""revoked""]   = new() { },
    };
    // ... 状态转移校验与 updated_at 自动维护
}",
                IsBlueprintOnly = true
            },

            // 3. 企业级动态表单 (Architecture Blueprint)
            new()
            {
                Id = "form-forge",
                Title = "📋 FormForge (企业级动态表单构建器)",
                Category = "forms",
                CategoryLabel = "企业级动态表单",
                Badge = "Architecture Blueprint",
                BadgeClass = "badge-neutral",
                Icon = "📋",
                Tagline = "零手写代码快速发布表单、字段级动态验证、自适应响应与数据收集",
                Description = "解决企业内部高频出现的信息采集、工单申报、调研问卷与用户反馈回流需求。内置可视化的表单拖拽设计器与动态数据持久化引擎，支持外网公开填报。",
                TargetAudience = "企业中后台全栈开发、内部工具工程师、业务运营与企业信息化 IT 部门",
                ArchitectInsight = "企业中后台 60% 以上的增删改查都是表单收集。FormForge 将表单模型 Schema 化，结合 NetYamlForge 动态存储与权限管理，让业务表单从需求提出到上线缩短到 5 分钟以内。",
                Highlights = new List<string>
                {
                    "动态可视化构建：支持单选多选、下拉、文本、日期与多行文本灵活排版",
                    "公开填报通道 (Public Fill)：支持匿名外部分享填报与企业内网认证模式",
                    "数据聚合与导出：填报结果自动关联，支持一键导出结构化 CSV"
                },
                TechTags = new List<string> { "Dynamic Schema", "Form Builder", "Public Fill", "Data Collection" },
                PrimaryActionUrl = "",
                PrimaryActionLabel = "",
                SecondaryActionUrl = null,
                SecondaryActionLabel = null,
                CodeSnippetTitle = "project.yaml (FormForge 声明式模块配置)",
                CodeLanguage = "yaml",
                CodeSnippet = @"name: form-forge
displayName: ""FormForge""
description: ""Form builder — create, publish, and collect responses""
database:
  type: sqlite
  path: database/form-forge.db
features:
  userAuthentication: true
layout:
  navigation:
    items:
      - label: ""My Forms""
        url: /form-forge/FormForge
        icon: 📋
      - label: ""Create Form""
        url: /form-forge/FormForge/Create
        icon: ➕",
                IsBlueprintOnly = true
            },

            // 4. 工业级 ERP 业务集成 (Architecture Blueprint)
            new()
            {
                Id = "jpiere-cs",
                Title = "🏭 JPiere Contract Service (企业级契约与 ERP 系统)",
                Category = "erp",
                CategoryLabel = "工业级企业业务",
                Badge = "Architecture Blueprint",
                BadgeClass = "badge-neutral",
                Icon = "🏭",
                Tagline = "企业核心业务的合同、报价与供应链协同系统，证明深水区业务承载力",
                Description = "基于开源 ERP/CRM JPiere 业务理念构建的契约服务。承载多达 28 个工业级关联实体，覆盖合同生命周期、报价审批、订单跟踪、采购核对与日记账核算，深度集成 C# 业务 Hooks。",
                TargetAudience = ".NET 企业级架构师、大型企业信息化团队、供应链系统与核心 ERP 开发者",
                ArchitectInsight = "低代码框架常被质疑无法承接复杂 ERP 的深度业务规则。JPiere 样板间证明：NetYamlForge 既能用 YAML 快速组织 28 个实体的级联关系，又通过原生 C# Hooks 允许无上限的深度工业逻辑扩展。",
                Highlights = new List<string>
                {
                    "28 个工业级关联实体：合同、报价、订单、发票、凭证、库存调拨等完整业务网",
                    "多角色工作台分流：一般员工视角（MyPage）与合同专员视角（ContractDetail）",
                    "复杂 C# 业务 Hooks：合同状态流转审批、金额税率校验与自动化待办流转",
                    "企业级审计与追踪：严格记录操作人、变动轨迹与状态时间线"
                },
                TechTags = new List<string> { "Enterprise ERP", "28 Entities", "Complex Hooks", "Approval Flow", "Audit Log" },
                PrimaryActionUrl = "",
                PrimaryActionLabel = "",
                SecondaryActionUrl = null,
                SecondaryActionLabel = null,
                CodeSnippetTitle = "contract.yml (合同核心实体定义与外键关联)",
                CodeLanguage = "yaml",
                CodeSnippet = @"entities:
  contract:
    table: 'c_contract'
    key: 'c_contract_id'
    displayName: 'Contract'
    columns:
      c_contract_id: { type: int, identity: true, label: 'ID' }
      documentno: { type: string, required: true, label: 'Contract No', searchable: true }
      c_bpartner_id: { type: int, label: 'Partner', foreignKey: { entity: business_partner, displayColumn: name } }
      totalamt: { type: decimal, label: 'Total Amount' }
      docstatus: { type: string, label: 'Status' } # Drafted, InProgress, Completed",
                IsBlueprintOnly = true
            },

            // 5. 智能文档流水线 (Architecture Blueprint)
            new()
            {
                Id = "ai-doc-processor",
                Title = "📑 AI Doc Processor (智能文档批处理流水线)",
                Category = "workflow",
                CategoryLabel = "流程与批处理",
                Badge = "Architecture Blueprint",
                BadgeClass = "badge-neutral",
                Icon = "📑",
                Tagline = "非结构化多格式文档的自动化批处理、智能解析与结构化流水线",
                Description = "展示 NetYamlForge 在面对大量非结构化文件（PDF、Word、图片）时的批处理与流水线编排能力。通过 Document Task 实体追踪任务状态与 AI 提取结果。",
                TargetAudience = "企业内容管理 (ECM) 团队、法务/财务文档自动化处理工程师",
                ArchitectInsight = "将耗时、异步的文档处理任务抽象为声明式实体，天然获得了状态监控、重试日志与 REST/MCP 调用能力，无需额外搭建臃肿的工作流系统。",
                Highlights = new List<string>
                {
                    "多阶段处理流水线：Upload → Extract → Classify → Completed",
                    "BatchJob 自动调度：支持按 Cron 定期执行超时任务归档与重试",
                    "结构化提取与审计：文档摘要、关键信息直接沉淀至实体数据库"
                },
                TechTags = new List<string> { "Document Task", "BatchJob", "Structured Extraction" },
                PrimaryActionUrl = "",
                PrimaryActionLabel = "",
                SecondaryActionUrl = null,
                SecondaryActionLabel = null,
                CodeSnippetTitle = "document_task.yml (批处理实体定义)",
                CodeLanguage = "yaml",
                CodeSnippet = @"entities:
  document_task:
    table: 'document_task'
    key: 'id'
    displayName: 'Document Task'
    columns:
      id: { type: int, identity: true, label: 'ID' }
      task_name: { type: string, required: true, label: 'Task Name' }
      status: { type: string, label: 'Status' } # pending, processing, completed, failed
      extracted_json: { type: string, label: 'Extracted Data' }",
                IsBlueprintOnly = true
            },

            // 6. 复杂交互与状态机游戏 (Architecture Blueprint)
            new()
            {
                Id = "dungeon-forge",
                Title = "⚔️ Dungeon Forge (文字地下城探索)",
                Category = "workflow",
                CategoryLabel = "交互状态机",
                Badge = "Architecture Blueprint",
                BadgeClass = "badge-neutral",
                Icon = "⚔️",
                Tagline = "展示高频状态流转、战斗计算与 AI DM 叙事引导的复杂互动状态机",
                Description = "文字交互式地下城探索，展示框架承载高频交互、回合制战斗规则与 AI 叙事编排的弹性能力。证明框架不仅能做静态中后台，也能承接富有动态趣味的交互系统。",
                TargetAudience = "游戏逻辑开发者、交互状态机探索者、LLM 叙事与动态决策爱好者",
                ArchitectInsight = "游戏逻辑拥有最具代表性的复合状态迁移与规则判定。NetYamlForge 能轻松承载其背包、属性与战斗决策，展现了极强的通用性。",
                Highlights = new List<string>
                {
                    "多实体回合制状态引擎：角色、怪物、物品、地图事件动态协同",
                    "AI 地下城主 (DM) 叙事：结合上下文动态生成环境描述与战斗战报",
                    "模块化 MVC 路由：无缝嵌入自定义前端交互与实时 WebSocket/API"
                },
                TechTags = new List<string> { "State Engine", "Interactive Storytelling", "Custom Controller" },
                PrimaryActionUrl = "",
                PrimaryActionLabel = "",
                SecondaryActionUrl = null,
                SecondaryActionLabel = null,
                CodeSnippetTitle = "DungeonExploreController.cs (自定义交互控制器挂载)",
                CodeLanguage = "csharp",
                CodeSnippet = @"[Route(""{project}/Dungeon"")]
public class DungeonExploreController(ProjectScope scope) : BaseProjectController
{
    [HttpGet(""Explore"")]
    public IActionResult Explore()
    {
        // 渲染富交互地下城探索视图，数据通过 /api/{project}/dungeon/* 实时通讯
        return View($""~/projects/{scope.Current.Name}/views/Dungeon/Explore.cshtml"");
    }
}",
                IsBlueprintOnly = true
            }
        };
    }

    private static string GeneratePromotionalCopy()
    {
        return @"### 🚀 NetYamlForge 平台样板间宣发矩阵

> **「声明式 YAML + 安全护栏 + 原生 C# Hooks」—— 专为 AI Agent 时代设计的现代 .NET 应用运行时**

1. **🤖 Agent Studio (自举演进工作台 · 运行中)**
   - 3 角色（Architect / Developer / Tester）闭环协作！基于 roles.yaml 声明式定义与 MCP 标准端点，打造透明可控的 AI 自主编程工作台。
2. **🛡️ Security Guardrails (安全标杆 · 架构蓝图)**
   - 告别 AI 生成代码的 SQL 注入与路径穿越风险！元数据 AST 编译期熔断 + Default-Deny 默认拒绝，为 Agent 提供生产级安全网。
3. **🤖 AI Handshake Protocol (AHP 智能体名片 · 架构蓝图)**
   - AI 时代的数字化身份与名片！有限状态机守卫 + 动态 QR 令牌生命周期，实现 Agent 与人类的受控握手。
4. **📋 FormForge (动态表单引擎 · 架构蓝图)**
   - 纯配置驱动的表单设计器！5 分钟完成信息采集、问卷调研与对外发布，内网外网双模填报。
5. **🏭 JPiere Contract Service (工业级 ERP 业务 · 架构蓝图)**
   - 28 个关联实体协同！涵盖合同、报价、审批流与日记账，证明框架承接深水区企业级业务的卓越弹性。

体验地址：https://nyf.0101.click/showcase
";
    }
}
