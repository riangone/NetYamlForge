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
                Description = "NetYamlForge 核心落地的声明式 AI 协作引擎。基于 roles.yaml 动态元数据驱动与 Model Context Protocol (MCP) 端点，实现端到端的角色提示词装配、协同流水线调度与执行日志追踪。",
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
                PrimaryActionUrl = "/agent-studio/Page/HomePage",
                PrimaryActionLabel = "进入 Agent Studio 工作台",
                SecondaryActionUrl = "/agent-studio/DynamicEntity/Index?entity=agent_task",
                SecondaryActionLabel = "🚀 协作任务管理",
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

            // 1. 现代高效待办工作台 (Todo Workspace)
            new()
            {
                Id = "todo-app",
                Title = "⚡ Todo Workspace (现代高效待办工作台)",
                Category = "productivity",
                CategoryLabel = "效率与协同",
                Badge = "Live App",
                BadgeClass = "badge-primary",
                Icon = "⚡",
                Tagline = "多维卡片流、行内极速添加、紧急预警与双轨解耦待办工作台",
                Description = "基于 NetYamlForge 声明式架构与 Claude 架构师协同自举落地的现代化任务协作平台。告别单调冰冷的数据表格，提供四维指标看板、可视化百分比完成度进度条、毫秒级模糊检索与圆形一键勾选微交互。",
                TargetAudience = "个人开发者、敏捷研发团队、项目主管、GTD 效率追求者",
                ArchitectInsight = "采用前台业务门户（HomePage）与底层元数据运维（DynamicEntity）彻底解耦的双轨模式。前台专注于极致的用户人机工效与响应式微交互，后台提供零成本的完整字段审计与数据管理，兼顾审美与工程严谨性。",
                Highlights = new List<string>
                {
                    "四维指标看板与动态完成率进度条：实时掌控全局与紧急高优任务进展",
                    "行内极速创建卡片（Inline Fast Creator）：支持回车快捷建档与优先级智能选择",
                    "截止日期状态动态预警：自动高亮 ⚠️ 已逾期（红底警示）与 ⏳ 今日截止（黄底提醒）",
                    "双轨分离架构：前台现代化卡片流交互 + 后台 DynamicEntity 完整运维审计"
                },
                TechTags = new List<string> { "Todo App", "Kanban & Cards", "Dual-Track UI", "Productivity", "Real-Time Filter" },
                PrimaryActionUrl = "/todo-app/Page/HomePage",
                PrimaryActionLabel = "进入待办工作台",
                SecondaryActionUrl = "/todo-app/DynamicEntity/Index?entity=todo",
                SecondaryActionLabel = "📋 待办数据管理",
                CodeSnippetTitle = "project.yaml (待办工作台布局与导航声明)",
                CodeLanguage = "yaml",
                CodeSnippet = @"name: todo-app
displayName: ""Todo App""
version: ""1.0.0""
description: ""NetYamlForge 轻量级待办应用""

database:
  type: sqlite
  path: database/todo-app.db

features:
  multiLanguage: true
  userAuthentication: true
  dashboard: true
  pages: true
  api: true

layout:
  theme: synthwave
  dashboardTheme: workspace
  landingPageByRole:
    viewer: /todo-app/Page/HomePage
    editor: /todo-app/Page/HomePage
    admin:  /todo-app/Page/HomePage",
                IsBlueprintOnly = false
            },

            // 2. 标准博客系统与内容发布平台 (Modern Blog & Publisher)
            new()
            {
                Id = "blog-app",
                Title = "📰 Modern Blog (标准博客与发布平台)",
                Category = "content",
                CategoryLabel = "内容与发布",
                Badge = "Live App",
                BadgeClass = "badge-secondary",
                Icon = "📰",
                Tagline = "标准一览流、Markdown 详情沉浸阅读、分类聚合与独立发布系统",
                Description = "遵循现代人机交互规范重塑的标准博客系统。提供优雅的文章一览卡片流、单篇独立沉浸阅读详情页、智能字数预估、多标签分类聚合以及全生命周期文章草稿与发布管控。",
                TargetAudience = "技术博主、独立创作者、产品布道师、内容运营团队",
                ArchitectInsight = "实现标准博客系统的经典动线解耦：一览流（Archive Index）负责高密度信息检索与分类导航，单篇阅读页（Post Detail）负责无干扰沉浸阅读。内置安全 Markdown HTML 消毒防护，坚决杜绝富文本 XSS 漏洞。",
                Highlights = new List<string>
                {
                    "双动线人机交互：优雅的文章一览流与单篇沉浸式独立阅读详情页完全解耦",
                    "沉浸式阅读体验：智能字数统计、预计阅读分钟数、作者签名与发布时间戳",
                    "分类与标签智能索引：支持多分类胶囊切换与草稿/已发布状态可视化标识",
                    "内生安全消毒：Markdown 渲染管线内置 HTML 净化，杜绝富文本 XSS 注入风险"
                },
                TechTags = new List<string> { "Blog App", "Markdown Reader", "Content Publisher", "XSS Sanitized", "Responsive" },
                PrimaryActionUrl = "/blog-app/Page/HomePage",
                PrimaryActionLabel = "浏览博客主页",
                SecondaryActionUrl = "/blog-app/DynamicEntity/Index?entity=blog_post",
                SecondaryActionLabel = "✍️ 文章内容运维",
                CodeSnippetTitle = "project.yaml (博客发布门户与分类实体定义)",
                CodeLanguage = "yaml",
                CodeSnippet = @"name: blog-app
displayName: ""Blog App""
version: ""1.0.0""
description: ""NetYamlForge 独立博客与内容发布平台""

database:
  type: sqlite
  path: database/blog-app.db

features:
  multiLanguage: true
  userAuthentication: true
  dashboard: true
  pages: true
  api: true

layout:
  theme: synthwave
  dashboardTheme: workspace
  landingPageByRole:
    viewer: /blog-app/Page/HomePage
    editor: /blog-app/Page/HomePage
    admin:  /blog-app/Page/HomePage",
                IsBlueprintOnly = false
            },

            // 3. 知识库协作中心 (Wiki Hub)
            new()
            {
                Id = "wiki-app",
                Title = "📚 Wiki Hub (全声明式知识库协作门户)",
                Category = "knowledge",
                CategoryLabel = "知识与文档",
                Badge = "Zero-Razor Live",
                BadgeClass = "badge-accent",
                Icon = "📚",
                Tagline = "零手写 Razor、全 YAML 积木装配、沉浸式架构文档流与实时指标看板",
                Description = "NetYamlForge 声明式积木生态扩展的里程碑应用。不编写任何一行 C# 或 Razor 视图代码，完全依赖通用声明式组件（_SectionMarkdown、_SectionKanban、stat_cards）与元数据定义拼装而成的企业级知识库门户。",
                TargetAudience = "企业架构师、技术文档工程师、研发管理人员、开源项目维护者",
                ArchitectInsight = "体现自举演进引擎的核心哲学：通过将可复用 UI 积木沉淀至框架内核，将 AI 生成代码的表面积压缩至极限。零 Razor 视图意味着零编译风险与零注入通道，达成真正意义上的 Safe by Construction。",
                Highlights = new List<string>
                {
                    "零手写 Razor 视图：100% 由 YAML 元数据与组件积木拼装驱动，无编译包袱",
                    "通用 Markdown 积木：自动绑定 titleField、dateField 与 contentField 正文解析",
                    "全景指标看板：实时聚合呈现全部文档数、已发布量与架构最佳实践热度",
                    "自举沙箱交付：由 AppManifestationService 引擎全自动安全落盘与热挂载"
                },
                TechTags = new List<string> { "Wiki Hub", "Zero-Razor", "Declarative Blocks", "Markdown Pipeline", "Safe by Construction" },
                PrimaryActionUrl = "/wiki-app/Page/HomePage",
                PrimaryActionLabel = "查阅知识库主页",
                SecondaryActionUrl = "/wiki-app/DynamicEntity/Index?entity=wiki_document",
                SecondaryActionLabel = "📑 知识文档库",
                CodeSnippetTitle = "pages/HomePage.yaml (全声明式多区块知识库装配规格)",
                CodeLanguage = "yaml",
                CodeSnippet = @"page:
  title: ""NetYamlForge 架构设计与知识库协作中心""
  description: ""沉浸式阅读架构演进规范、最佳实践与多智能体协同蓝图""
  layout: ""default""

sections:
  - id: stats
    type: stat_cards
    title: ""知识库全景指标""
  - id: intro_doc
    type: markdown
    title: ""架构全景与核心规范""
  - id: articles_overview
    type: card_list
    title: ""架构文章与规范速查""",
                IsBlueprintOnly = false
            },

            // 4. 企业级表单审批流中心 (Approval Flow)
            new()
            {
                Id = "approval-flow",
                Title = "📋 Approval Flow (企业表单审批流中心)",
                Category = "enterprise",
                CategoryLabel = "企业级应用",
                Badge = "Enterprise Live",
                BadgeClass = "badge-warning",
                Icon = "📋",
                Tagline = "行政差旅/财务报销/设备采购多级审批流、状态流转与历史流转审计",
                Description = "基于 Agent Studio 自举孵化的企业级多级流程审批工作台。涵盖费用报销、设备采购、带薪休假等场景，提供完整的状态机流转（待审批/已通过/驳回/撤回）、审计历史追踪与自定义 Hook 动作闭环。",
                TargetAudience = "企业行政财务人员、部门主管、中大型团队协同管理者",
                ArchitectInsight = "将企业级复杂工作流建模为'声明式表单 + 自定义动作处理器（ICustomActionHandler）+ 审计日志'三位一体。动作处理器在事务内严格原子执行状态跃迁与操作流水回写，天然防脏读与重复决断。",
                Highlights = new List<string>
                {
                    "多级业务流转闭环：审批通过、驳回、自主撤回完整动作处理器物理实装",
                    "流转历史审计日志：自动向 approval_log 回写操作人、决断动作与审计意见",
                    "声明式效能大屏：一览展示待我审批、已通过、已驳回及紧急单据实时指标",
                    "高优紧急程度预警：支持 normal / high / urgent 多维权重排序与警示凸显"
                },
                TechTags = new List<string> { "Approval Flow", "Custom Action Handlers", "Audit Trail", "Workflow", "Enterprise" },
                PrimaryActionUrl = "/approval-flow/Page/HomePage",
                PrimaryActionLabel = "进入审批流中心",
                SecondaryActionUrl = "/approval-flow/DynamicEntity/Index?entity=approval_request",
                SecondaryActionLabel = "📑 审批单据管理",
                CodeSnippetTitle = "entities/approval_request.yml (审批实体与动作处理器声明)",
                CodeLanguage = "yaml",
                CodeSnippet = @"entities:
  approval_request:
    table: approval_request
    key: id
    displayName: ""审批申请单""
    actions:
      approve_request:
        label: ""审批通过""
        handler: approve_request_handler
      reject_request:
        label: ""驳回申请""
        handler: reject_request_handler
      withdraw_request:
        label: ""撤回单据""
        handler: withdraw_request_handler",
                IsBlueprintOnly = false
            },

            // 5. 企业经营指标与决策看板 (Analytics Dashboard)
            new()
            {
                Id = "analytics-dashboard",
                Title = "📊 Analytics Dashboard (经营指标决策看板)",
                Category = "enterprise",
                CategoryLabel = "企业级应用",
                Badge = "Analytics Live",
                BadgeClass = "badge-info",
                Icon = "📊",
                Tagline = "经营指标实时聚合、商业营收漏斗、渠道 ROI 与平台基础设施大屏",
                Description = "为企业核心决策层打造的全景数据监控与洞察工作台。无缝聚合季度 GMV、活跃客户规模、多渠道营收贡献与高 ROI 增长引擎，支持纯声明式卡片流与全量明细矩阵联动分析。",
                TargetAudience = "管理层、业务负责人、运营数据分析师、投资人",
                ArchitectInsight = "结合 NetYamlForge 声明式积木的 stat_cards 与 card_list，通过 SQL 动态聚合实现亚毫秒级的经营大屏渲染。数据结构灵活可扩展，无需硬编码图表组件即可迅速洞察关键商业指标。",
                Highlights = new List<string>
                {
                    "全景商业指标大屏：季度 GMV、租户增长率、付费转化率及平台响应延迟",
                    "主力业务线与增长引擎：渠道营收规模、客户转化单数与 ROI 投资回报倍数对比",
                    "纯声明式积木驱动：通过 SQL 统计聚合与 card_list 组合，开发交付零延迟",
                    "经营明细数据表格：无缝下钻至业务渠道与核心指标完整生命周期"
                },
                TechTags = new List<string> { "Analytics Dashboard", "BI Metrics", "Declarative Cards", "ROI Funnel", "Enterprise" },
                PrimaryActionUrl = "/analytics-dashboard/Page/HomePage",
                PrimaryActionLabel = "查看经营大屏",
                SecondaryActionUrl = "/analytics-dashboard/DynamicEntity/Index?entity=kpi_metric",
                SecondaryActionLabel = "📈 经营指标明细",
                CodeSnippetTitle = "pages/HomePage.yaml (经营看板声明式积木装配)",
                CodeLanguage = "yaml",
                CodeSnippet = @"title: ""企业经营分析与决策看板""
sections:
  - id: gmv_overview
    title: ""核心战略指标""
    source: ""SELECT metric_name as label, current_value as value, icon FROM kpi_metric""
    component: stat_cards
  - id: channel_cards
    title: ""主力业务线与增长引擎""
    source: ""SELECT id, channel_name, category, revenue, roi_multiple FROM business_channel""
    component: card_list",
                IsBlueprintOnly = false
            }
        };
    }

    private static string GeneratePromotionalCopy()
    {
        return @"### 🚀 NetYamlForge 全场景自举演进应用矩阵

> **「声明式 YAML + 内生安全护栏 + 异构多智能体协同」—— 专为 AI 时代设计的现代 .NET 应用运行时**

1. **🤖 Agent Studio (智能体自举演进工作台)**
   - 3 角色（Architect / Developer / Tester）闭环协作！基于 roles.yaml 声明式定义与 MCP 标准端点，打造透明可控的 AI 自主编程工作台。
   - 访问地址：`/agent-studio/Page/HomePage`

2. **⚡ Todo Workspace (现代高效待办工作台)**
   - 业务前台与底层元数据双轨解耦！四维指标看板、行内极速创建卡片、逾期动态预警与毫秒级模糊检索。
   - 访问地址：`/todo-app/Page/HomePage`

3. **📰 Modern Blog (标准博客与发布平台)**
   - 优雅一览流 + 沉浸式阅读详情页双动线解耦，智能字数统计与阅读时长预估，内置 Markdown HTML 消毒安全防线。
   - 访问地址：`/blog-app/Page/HomePage`

4. **📚 Wiki Hub (全声明式知识库协作门户)**
   - 零手写 Razor 视图，100% 声明式 YAML 积木装配（SectionKanban / SectionMarkdown / StatCards），天生内生安全（Safe by Construction）。
   - 访问地址：`/wiki-app/Page/HomePage`

5. **📋 Approval Flow (企业表单审批流中心)**
   - 行政差旅/财务报销/设备采购多级审批工作台，完整实现 approve / reject / withdraw 动作处理器与审计流水。
   - 访问地址：`/approval-flow/Page/HomePage`

6. **📊 Analytics Dashboard (经营指标决策看板)**
   - 全景商业指标大屏，涵盖季度 GMV、增长率、漏斗转化与多渠道 ROI，纯声明式秒级敏捷交付。
   - 访问地址：`/analytics-dashboard/Page/HomePage`

🎪 全场景样板间体验馆：https://nyf.0101.click/showcase
";
    }
}
