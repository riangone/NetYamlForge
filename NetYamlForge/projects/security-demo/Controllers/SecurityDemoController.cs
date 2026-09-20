// Security demo controller showcasing guardrail interception & interactive security challenge
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Auth;

namespace NetYamlForge.Projects.SecurityDemo.Controllers;

/// <summary>
/// 安全演示控制器：提供红蓝对抗演练沙箱与交互式安全护栏测试接口。
/// 开发者可在此实时体验 NetYamlForge 在配置解析期与运行期对恶意注入与越权行为的拦截能力。
/// </summary>
[AllowAnonymous]
[Route("{project}/[controller]/[action]")]
public class SecurityDemoController : Controller
{
    private readonly IUserAuthService _auth;
    private readonly IWebHostEnvironment _env;
    private readonly ProjectScope _projectScope;
    private readonly IEntityMetadataProvider _entityMetadataProvider;
    private readonly ILogger<SecurityDemoController> _logger;

    public SecurityDemoController(
        IUserAuthService auth,
        IWebHostEnvironment env,
        ProjectScope projectScope,
        IEntityMetadataProvider entityMetadataProvider,
        ILogger<SecurityDemoController> logger)
    {
        _auth = auth;
        _env = env;
        _projectScope = projectScope;
        _entityMetadataProvider = entityMetadataProvider;
        _logger = logger;
    }

    /// <summary>
    /// 红蓝对抗演练沙箱首页
    /// 支持 /{project}/SecurityDemo/Index 以及 /{project}/challenge
    /// </summary>
    [HttpGet]
    [Route("/{project}/challenge")]
    [Route("/{project}/[controller]")]
    [Route("/{project}/[controller]/[action]")]
    public IActionResult Index()
    {
        ViewData["Title"] = "红蓝对抗 · AI安全护栏沙箱 (Interactive Security Challenge)";
        return View();
    }

    /// <summary>
    /// 演练 1: YAML 元数据安全与 SQL 注入校验
    /// </summary>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public IActionResult ValidateYaml([FromBody] YamlValidateRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.YamlContent))
        {
            return Json(new SecurityCheckResult
            {
                Success = false,
                Blocked = false,
                Stage = "参数校验",
                Message = "YAML 内容不能为空。"
            });
        }

        try
        {
            var parser = new EntityMetadataParser();
            var root = parser.Parse(request.YamlContent, "interactive-challenge.yml");

            if (root?.Entities == null || root.Entities.Count == 0)
            {
                return Json(new SecurityCheckResult
                {
                    Success = false,
                    Blocked = true,
                    Guard = "EntityMetadataParser",
                    Stage = "语法解析阶段 (Parse Phase)",
                    ViolationType = "MissingEntityDefinition",
                    Message = "YAML 格式不符合规范：未找到有效的 entities 根定义。",
                    AttackExplanation = "传入的 YAML 未包含合法的实体结构，框架在语法反序列化阶段阻断。",
                    DefenseExplanation = "NetYamlForge 采用强类型 DSL 结构，任何格式偏差都会被反序列化引擎直接拒绝。"
                });
            }

            var entityName = !string.IsNullOrWhiteSpace(request.EntityName) && root.Entities.ContainsKey(request.EntityName)
                ? request.EntityName
                : root.Entities.Keys.First();

            var def = root.Entities[entityName];
            parser.Normalize(def);

            // 执行核心元数据安全校验
            DynamicCrudMetadataValidator.ValidateMetadata(def, entityName);

            return Json(new SecurityCheckResult
            {
                Success = true,
                Blocked = false,
                Guard = "DynamicCrudMetadataValidator & SqlSafetyGuard",
                Stage = "编译与加载期通过 (Compile-Time Validated)",
                Message = $"✅ 实体 '{entityName}' 元数据安全校验通过！未发现恶意 SQL 注入、非法 Join 或未授权标识符。",
                DefenseExplanation = "此配置符合 NetYamlForge 严格的白名单机制，已安全载入元数据仓库，可放心暴露为动态 CRUD 及 MCP 工具。"
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("拦截到不安全的 YAML 配置: {Message}", ex.Message);

            var violation = "MaliciousMetadataOrSqlInjection";
            var attackExpl = "AI 生成的代码或恶意用户试图在 YAML 配置中夹带 SQL 注入、越权 Join 或非法标识符。";

            if (ex.Message.Contains("joinType", StringComparison.OrdinalIgnoreCase))
            {
                violation = "UnsafeJoinType";
                attackExpl = "试图使用非白名单的外键连接方式（如 full outer join 等）。框架仅允许经安全验证的 left, inner, right 连接。";
            }
            else if (ex.Message.Contains("Unsafe fk.query", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("expression", StringComparison.OrdinalIgnoreCase))
            {
                violation = "SqlExpressionInjection";
                attackExpl = "试图在表达式或外键过滤条件中夹带恶意 SQL 代码（例如分号中断、DROP TABLE、联合查询注入等）。";
            }
            else if (ex.Message.Contains("SQL keywords are not allowed", StringComparison.OrdinalIgnoreCase))
            {
                violation = "SqlKeywordIdentifierInjection";
                attackExpl = "试图使用 SQL 命令关键字（如 DROP, DELETE, EXEC 等）作为表名或字段标识符。";
            }

            return Json(new SecurityCheckResult
            {
                Success = false,
                Blocked = true,
                Guard = "DynamicCrudMetadataValidator / SqlSafetyGuard",
                Stage = "元数据加载与解析期 (Compile-Time / Parse Guard)",
                ViolationType = violation,
                Message = $"🛡️ 【阻断成功】{ex.Message}",
                AttackExplanation = attackExpl,
                DefenseExplanation = "NetYamlForge 在实体加载的第一道关卡即执行静态 AST 与词法白名单分析，攻击载荷根本无法进入 SQL 构建器或接触底层数据库连接池！"
            });
        }
        catch (Exception ex)
        {
            return Json(new SecurityCheckResult
            {
                Success = false,
                Blocked = true,
                Guard = "EntityMetadataParser",
                Stage = "反序列化阶段 (Deserialization Guard)",
                ViolationType = "YamlParseError",
                Message = $"🛡️ 【阻断成功】YAML 解析失败: {ex.Message}",
                AttackExplanation = "畸形 YAML 结构或尝试利用 YAML 锚点炸弹（Billion Laughs）等反序列化载荷。",
                DefenseExplanation = "YamlDotNet 安全配置阻止了未知类型实例化与嵌套解析溢出。"
            });
        }
    }

    /// <summary>
    /// 演练 2: 运行时 Hook 路径穿越（Path Traversal）防御校验
    /// </summary>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public IActionResult ValidatePath([FromBody] PathValidateRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.TargetPath))
        {
            return Json(new SecurityCheckResult
            {
                Success = false,
                Blocked = false,
                Stage = "参数校验",
                Message = "测试路径不能为空。"
            });
        }

        // 基准测试沙箱目录：隔离在当前项目的 attachments 安全根目录
        var baseDir = Path.Combine(_env.ContentRootPath, "projects", "security-demo", "attachments-vault");

        try
        {
            var resolved = PathSafetyGuard.NormalizeAndValidatePath(
                request.TargetPath,
                baseDir,
                "security-demo.action.download");

            return Json(new SecurityCheckResult
            {
                Success = true,
                Blocked = false,
                Guard = "PathSafetyGuard",
                Stage = "运行时路径校验通过 (Runtime Safe)",
                ResolvedPath = resolved,
                Message = $"✅ 路径合规：目标路径已规范化并确认严格处于安全沙箱 '{baseDir}' 内。",
                DefenseExplanation = "路径未越界，允许后续受控的文件读取或附件下载操作。"
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("拦截到越权路径穿越攻击: {Message}", ex.Message);
            return Json(new SecurityCheckResult
            {
                Success = false,
                Blocked = true,
                Guard = "PathSafetyGuard",
                Stage = "运行时文件操作边界防护 (Runtime Guard)",
                ViolationType = "PathTraversalEscape",
                Message = $"🛡️ 【阻断成功】{ex.Message}",
                AttackExplanation = "AI 常见的附件下载 Hook 直接使用了简单的 Path.Combine，导致攻击者可通过 '../../' 向上逃逸读取系统敏感文件（如 /etc/passwd、数据库文件等）或使用绝对路径直接读取。",
                DefenseExplanation = "PathSafetyGuard 自动解析全部相对路径与符号，并在操作系统目录分隔符边界进行强制前缀匹配。任何越界操作都会立即抛出 UnauthorizedAccessException 并打上审计日志。"
            });
        }
        catch (Exception ex)
        {
            return Json(new SecurityCheckResult
            {
                Success = false,
                Blocked = true,
                Guard = "PathSafetyGuard",
                Stage = "参数检查",
                ViolationType = "InvalidPathArgument",
                Message = $"🛡️ 【阻断成功】非法路径参数: {ex.Message}",
                AttackExplanation = "畸形路径格式或空路径探测。",
                DefenseExplanation = "参数有效性检查拦截了非法的路径格式。"
            });
        }
    }

    /// <summary>
    /// 演练 3: Agent / API 默认拒绝（Default-Deny）访问探测
    /// </summary>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public IActionResult CheckApiAccess([FromBody] ApiCheckRequest request)
    {
        var entityName = request?.EntityName?.Trim().ToLowerInvariant() ?? "attachment";
        var entities = _entityMetadataProvider.GetAll();

        if (!entities.TryGetValue(entityName, out var def))
        {
            return Json(new SecurityCheckResult
            {
                Success = false,
                Blocked = true,
                Guard = "EntityAccessController",
                Stage = "实体存在性与授权校验",
                ViolationType = "UnknownEntity",
                Message = $"实体 '{entityName}' 不存在。"
            });
        }

        var isApiEnabled = string.Equals(def.Api, "enabled", StringComparison.OrdinalIgnoreCase);

        if (!isApiEnabled)
        {
            return Json(new SecurityCheckResult
            {
                Success = false,
                Blocked = true,
                Guard = "DynamicApiPipeline & McpServerGuard",
                Stage = "API 路由与 MCP 协议发现层 (Default-Deny Guard)",
                ViolationType = "ApiDisabledByDefault",
                Message = $"🛡️ 【阻断成功】实体 '{entityName}' 的 API 状态为 '{def.Api}'（默认禁用）。REST API 路由和 MCP Agent 工具集均对其不可见。",
                AttackExplanation = "在没有安全护栏的系统中，数据库中所有生成的实体可能被全量自动暴露给外部 REST API 或 LLM Agent，导致内联数据、凭证附件或内部审计日志被无差别检索。",
                DefenseExplanation = "NetYamlForge 遵循「默认安全 / Default-Deny」准则。除非实体 YAML 中显式声明了 `api: enabled`（如 note 实体），否则任何外部 API、Swagger 或 MCP 工具均完全隐身并直接返回 403/Forbidden，从源头杜绝未授权暴露。"
            });
        }

        return Json(new SecurityCheckResult
        {
            Success = true,
            Blocked = false,
            Guard = "DynamicApiPipeline & McpServerGuard",
            Stage = "API 授权开放 (Explicitly Enabled)",
            Message = $"✅ 实体 '{entityName}' 已显式配置 `api: enabled`，允许受控的 REST API 访问与 AI Agent MCP 工具调用。",
            DefenseExplanation = "该实体已接入声明式速率限制、身份认证以及字段级脱敏保护。"
        });
    }

    /// <summary>
    /// 获取官方预设的攻击样本与安全对照基线
    /// </summary>
    [HttpGet]
    public IActionResult GetPresets()
    {
        var presets = new
        {
            yamlPresets = new[]
            {
                new {
                    id = "unsafe-join",
                    title = "😈 恶意外键 Join (非白名单连接类型)",
                    description = "AI 代码生成器试图生成 'full' (Full Outer Join) 导致不受控关联",
                    yaml = @"imports: []
entities:
  note:
    table: Note
    key: Id
    displayName: Note
    api: enabled
    joins:
      - type: full
        table: Attachment
        alias: a
        on: Note.Id = a.NoteId
    columns:
      Id:
        type: int
        identity: true
        label: ID
      Title:
        type: string
        label: Title"
                },
                new {
                    id = "raw-sql-injection",
                    title = "😈 伪装在 YAML 中的 SQL 注入 (DROP TABLE)",
                    description = "AI 将用户的过滤表达式原样带入 filter.expression，试图执行分号注入",
                    yaml = @"imports: []
entities:
  note:
    table: Note
    key: Id
    displayName: Note
    api: enabled
    columns:
      Id:
        type: int
        identity: true
        label: ID
      Title:
        type: string
        label: Title
    filters:
      AdminRawFilter:
        type: like
        label: ""Raw filter (unsafe)""
        expression: ""1=1; DROP TABLE Note; --"""
                },
                new {
                    id = "keyword-injection",
                    title = "😈 SQL 命令关键字注入 (表名恶意命名)",
                    description = "利用 SQL 关键字 DROP/EXEC 作为表名注入 AST",
                    yaml = @"imports: []
entities:
  exploit:
    table: DROP
    key: Id
    displayName: Exploit
    columns:
      Id:
        type: int
        identity: true
        label: ID"
                },
                new {
                    id = "safe-note",
                    title = "🛡️ 标准安全 Note 实体 (官方参考实现)",
                    description = "符合 NetYamlForge 白名单规范的安全声明式定义",
                    yaml = @"imports: []
entities:
  note:
    table: Note
    key: Id
    displayName: Note
    api: enabled
    columns:
      Id:
        type: int
        identity: true
        label: ID
      Title:
        type: string
        label: Title
      Content:
        type: string
        label: Content
      Status:
        type: string
        label: Status
    filters:
      Status:
        type: like
        label: Status"
                }
            },
            pathPresets = new[]
            {
                new {
                    id = "passwd-escape",
                    title = "😈 ../../../../etc/passwd (系统根逃逸)",
                    path = "../../../../etc/passwd"
                },
                new {
                    id = "tenant-escape",
                    title = "😈 ../../other-tenant/database/tenant.db (跨租户数据窥探)",
                    path = "../../other-tenant/database/tenant.db"
                },
                new {
                    id = "absolute-path",
                    title = "😈 /etc/shadow (绝对路径越权绕过)",
                    path = "/etc/shadow"
                },
                new {
                    id = "safe-attachment",
                    title = "🛡️ notes/1/attachment-report.pdf (合规沙箱内路径)",
                    path = "notes/1/attachment-report.pdf"
                }
            }
        };

        return Json(presets);
    }
}

public class YamlValidateRequest
{
    public string? YamlContent { get; set; }
    public string? EntityName { get; set; }
}

public class PathValidateRequest
{
    public string? TargetPath { get; set; }
}

public class ApiCheckRequest
{
    public string? EntityName { get; set; }
}

public class SecurityCheckResult
{
    public bool Success { get; set; }
    public bool Blocked { get; set; }
    public string? Guard { get; set; }
    public string? Stage { get; set; }
    public string? ViolationType { get; set; }
    public string? Message { get; set; }
    public string? ResolvedPath { get; set; }
    public string? AttackExplanation { get; set; }
    public string? DefenseExplanation { get; set; }
}
