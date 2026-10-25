using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services;
using NetYamlForge.Services.Connection;

namespace NetYamlForge.Services.AI.AgentStudio.Manifestation;

/// <summary>
/// 物理交付引擎实现：负责从智能体协同推演产物中提取规格、安全落地至磁盘、初始化数据库、
/// 自动补全脚手架、执行编译与静态诊断自愈，并在无法通过时触发原子回滚，最后热重载至运行时
/// </summary>
public class AppManifestationService : IAppManifestationService
{
    private readonly IConnectionManager _connectionManager;
    private readonly ProjectManager _projectManager;
    private readonly IWebHostEnvironment _env;
    private readonly IManifestationSelfHealingService _selfHealingService;
    private readonly ILogger<AppManifestationService> _logger;

    private static readonly Regex FenceWithPathRegex = new(
        @"(?:```|~~~)(?:[a-zA-Z0-9_\-]+)?[:\s]+([a-zA-Z0-9_\-/\\]+\.[a-zA-Z0-9]+)\r?\n([\s\S]*?)(?:```|~~~)",
        RegexOptions.Compiled);

    private static readonly Regex HeaderFileRegex = new(
        @"(?:###|\*\*|##|#)?\s*(?:File|Path|文件|路径|パス):\s*`?([a-zA-Z0-9_\-/\\]+\.[a-zA-Z0-9]+)`?\s*\r?\n\s*(?:```|~~~)(?:[a-zA-Z0-9_\-]+)?\r?\n([\s\S]*?)(?:```|~~~)",
        RegexOptions.Compiled);

    private static readonly Regex InsideCommentFileRegex = new(
        @"(?:```|~~~)(?:[a-zA-Z0-9_\-]+)?\r?\n\s*(?://|#)\s*(?:File|Path|文件|路径|パス):\s*([a-zA-Z0-9_\-/\\]+\.[a-zA-Z0-9]+)\r?\n([\s\S]*?)(?:```|~~~)",
        RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".yaml", ".yml", ".json", ".sql", ".cs", ".cshtml", ".md", ".txt", ".csv"
    };

    private static readonly string[] AllowedSubdirectories =
    {
        "entities/", "pages/", "config/", "database/", "Hooks/", "Views/"
    };

    private static readonly HashSet<string> AllowedRootFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "project.yaml", "project.yml", "layout.yml", "tenant.yaml", "README.md"
    };

    private static readonly string[] DangerousCodeTokens =
    {
        "Process.Start", "ProcessStartInfo", "Assembly.Load", "DllImport", "Marshal.",
        "Socket(", "TcpClient", "BinaryFormatter"
    };

    /// <summary>
    /// 零信任沙箱路径校验：验证相对路径是否在允许的目录与扩展名白名单内，杜绝路径逃逸、越权覆盖与危险可执行文件
    /// </summary>
    public static bool ValidateManifestPath(string cleanRelPath, out string? reason)
    {
        if (string.IsNullOrWhiteSpace(cleanRelPath))
        {
            reason = "路径不能为空";
            return false;
        }

        if (cleanRelPath.Contains("..") || Path.IsPathRooted(cleanRelPath))
        {
            reason = "禁止父级目录回溯 (..) 或绝对路径";
            return false;
        }

        var ext = Path.GetExtension(cleanRelPath);
        if (!AllowedExtensions.Contains(ext))
        {
            reason = $"禁止的文件扩展名 '{ext}'。仅允许声明式规范与安全代码。";
            return false;
        }

        var fileName = Path.GetFileName(cleanRelPath);
        if (fileName.StartsWith('.'))
        {
            reason = $"禁止创建隐藏文件 '{fileName}'";
            return false;
        }

        if (cleanRelPath.Contains('/'))
        {
            var isAllowed = AllowedSubdirectories.Any(prefix => cleanRelPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (!isAllowed)
            {
                reason = $"未授权的目标子目录: '{cleanRelPath}'";
                return false;
            }
        }
        else
        {
            if (!AllowedRootFiles.Contains(cleanRelPath))
            {
                reason = $"未授权的项目根目录文件: '{cleanRelPath}'";
                return false;
            }
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// C# 钩子静态安全网闸检查：拦截违规的危险系统调用与越权反射
    /// </summary>
    public static bool CheckHookCodeSafety(string csCode, out string? violation)
    {
        foreach (var token in DangerousCodeTokens)
        {
            if (csCode.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                violation = $"检测到高危代码或禁止的 API 调用: '{token}'";
                return false;
            }
        }
        violation = null;
        return true;
    }

    public AppManifestationService(
        IConnectionManager connectionManager,
        ProjectManager projectManager,
        IWebHostEnvironment env,
        ILogger<AppManifestationService> logger,
        IManifestationSelfHealingService? selfHealingService = null)
    {
        _connectionManager = connectionManager;
        _projectManager = projectManager;
        _env = env;
        _logger = logger;
        _selfHealingService = selfHealingService ?? new ManifestationSelfHealingService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ManifestationSelfHealingService>.Instance);
    }

    private sealed class TaskRecord
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string? DeveloperOutput { get; set; }
        public string? ArchitectOutput { get; set; }
        public string? Status { get; set; }
    }

    public async Task<ManifestationResult> ManifestAppAsync(
        ManifestationRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[ManifestationEngine] Starting manifestation for Task {TaskId}", request.TaskId);

        // 1. 获取任务详情
        TaskRecord? task;
        using (var db = await _connectionManager.GetConnectionAsync("agent-studio", cancellationToken))
        {
            task = await db.QueryFirstOrDefaultAsync<TaskRecord>(
                "SELECT id AS Id, title AS Title, description AS Description, " +
                "developer_output AS DeveloperOutput, architect_output AS ArchitectOutput, status AS Status " +
                "FROM agent_task WHERE id = @Id",
                new { Id = request.TaskId });
        }

        if (task == null)
        {
            return new ManifestationResult(
                Success: false,
                ProjectName: "",
                ProjectDir: "",
                Files: Array.Empty<ManifestedFileInfo>(),
                DatabaseInitialized: false,
                HooksScaffolded: false,
                DynamicReloadSucceeded: false,
                Error: $"未找到 ID 为 {request.TaskId} 的演进任务。");
        }

        // 2. 提取并结构化产物文件
        var combinedText = (task.DeveloperOutput ?? "") + "\n\n" + (task.ArchitectOutput ?? "");
        var extractedFiles = ExtractFilesFromMarkdown(combinedText);

        // 3. 确定目标项目标识
        var projectName = ResolveProjectName(request.TargetProjectName, task.Title, extractedFiles);
        var projectsRoot = ResolveProjectsRoot();
        var targetProjectDir = Path.Combine(projectsRoot, projectName);

        _logger.LogInformation("[ManifestationEngine] Target project: '{ProjectName}' at '{Dir}'. Extracted {Count} files.",
            projectName, targetProjectDir, extractedFiles.Count);

        // 4. 在物理变更前拍摄快照（用于自愈失败或异常时的原子回滚）
        var snapshot = ProjectSnapshot.Capture(targetProjectDir);
        var fileInfos = new List<ManifestedFileInfo>();

        try
        {
            Directory.CreateDirectory(targetProjectDir);

            // 5. 安全沙箱检查与文件物理落盘
            foreach (var (rawRelPath, rawContent) in extractedFiles)
            {
                var cleanRelPath = NormalizeRelativePath(rawRelPath, projectName);
                var content = rawContent;
                if (string.IsNullOrWhiteSpace(cleanRelPath)) continue;

                // 防御性规范化：若 entities/*.yml 缺少 entities 根键，自动为其规范化包裹
                if (cleanRelPath.StartsWith("entities/", StringComparison.OrdinalIgnoreCase) &&
                    (cleanRelPath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || cleanRelPath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!content.Contains("entities:"))
                    {
                        var entityKey = Path.GetFileNameWithoutExtension(cleanRelPath);
                        var indented = string.Join("\n", content.Split('\n').Select(l => "    " + l));
                        content = $"entities:\n  {entityKey}:\n{indented}\n";
                    }
                }

                // 零信任沙箱安全路径与文件扩展名验证
                if (!ValidateManifestPath(cleanRelPath, out var rejectionReason))
                {
                    _logger.LogWarning("[ManifestationEngine] Rejected unsafe path '{Path}': {Reason}", cleanRelPath, rejectionReason);
                    fileInfos.Add(new ManifestedFileInfo(cleanRelPath, Encoding.UTF8.GetByteCount(content), $"Rejected ({rejectionReason})"));
                    continue;
                }

                // C# 钩子静态安全网闸检查
                if (cleanRelPath.StartsWith("Hooks/", StringComparison.OrdinalIgnoreCase) && cleanRelPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    if (!CheckHookCodeSafety(content, out var violation))
                    {
                        _logger.LogError("[ManifestationEngine] Rejected unsafe C# hook '{Path}': {Violation}", cleanRelPath, violation);
                        fileInfos.Add(new ManifestedFileInfo(cleanRelPath, Encoding.UTF8.GetByteCount(content), $"Rejected ({violation})"));
                        continue;
                    }
                }

                var fullPath = Path.GetFullPath(Path.Combine(targetProjectDir, cleanRelPath));
                if (!fullPath.StartsWith(Path.GetFullPath(targetProjectDir), StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("[ManifestationEngine] Path traversal blocked: {Path}", fullPath);
                    fileInfos.Add(new ManifestedFileInfo(cleanRelPath, Encoding.UTF8.GetByteCount(content), "Rejected (Path traversal)"));
                    continue;
                }

                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var exists = File.Exists(fullPath);
                if (!exists || request.ForceOverwrite || fullPath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || fullPath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                {
                    await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8, cancellationToken);
                    fileInfos.Add(new ManifestedFileInfo(cleanRelPath, Encoding.UTF8.GetByteCount(content), exists ? "Updated" : "Created"));
                }
                else
                {
                    fileInfos.Add(new ManifestedFileInfo(cleanRelPath, (int)new FileInfo(fullPath).Length, "Skipped (Exists)"));
                }
            }

            // 若完全未提取出 project.yaml 且该项目原本也不存在，生成基础骨架以确保项目可用
            var projectYamlPath = Path.Combine(targetProjectDir, "project.yaml");
            if (!File.Exists(projectYamlPath))
            {
                var fallbackYaml = GenerateFallbackProjectYaml(projectName, task.Title);
                await File.WriteAllTextAsync(projectYamlPath, fallbackYaml, Encoding.UTF8, cancellationToken);
                fileInfos.Add(new ManifestedFileInfo("project.yaml", Encoding.UTF8.GetByteCount(fallbackYaml), "Created (Scaffolded)"));
            }
            else
            {
                // 确保已存在的或提取出的 project.yaml 具备必需的 database 定义，防御 AI 生成遗漏
                var currentYaml = await File.ReadAllTextAsync(projectYamlPath, cancellationToken);
                if (!currentYaml.Contains("database:"))
                {
                    var enrichedYaml = currentYaml.TrimEnd() + $"\n\ndatabase:\n  type: sqlite\n  path: database/{projectName}.db\n";
                    await File.WriteAllTextAsync(projectYamlPath, enrichedYaml, Encoding.UTF8, cancellationToken);
                }
            }

            // 6. 数据库物理初始化
            var dbInitialized = false;
            var schemaSqlPath = Path.Combine(targetProjectDir, "database", "init_schema.sql");
            var sqliteDbPath = Path.Combine(targetProjectDir, "database", $"{projectName}.db");

            if (File.Exists(schemaSqlPath) && (!File.Exists(sqliteDbPath) || request.ForceOverwrite))
            {
                Directory.CreateDirectory(Path.Combine(targetProjectDir, "database"));
                var sql = await File.ReadAllTextAsync(schemaSqlPath, cancellationToken);
                if (!string.IsNullOrWhiteSpace(sql))
                {
                    // 零信任 SQL 语法网闸校验（阻断 ATTACH/DETACH 与危险 PRAGMA 逃逸）
                    SqlSafetyGuard.EnsureSafeSchemaSql(sql, $"init_schema.sql for project '{projectName}'");

#pragma warning disable DCS003 // 抑制理由: 新規プロジェクトの物理スキーマ初期化時はプロジェクト未登録のため直接接続する
                    using var sqliteConn = new SqliteConnection($"Data Source={sqliteDbPath};Pooling=False");
                    await sqliteConn.OpenAsync(cancellationToken);
                    using var cmd = sqliteConn.CreateCommand();
                    cmd.CommandText = sql;
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
#pragma warning restore DCS003
                    dbInitialized = true;
                    _logger.LogInformation("[ManifestationEngine] Initialized SQLite database from init_schema.sql: {DbPath}", sqliteDbPath);
                }
            }

            // 7. 脚手架一致性与缺失 Hook 自动防空定义补全
            var hooksScaffolded = false;
            if (request.AutoScaffoldHooks)
            {
                hooksScaffolded = ScaffoldMissingStubsIfAny(targetProjectDir, projectName);
            }

            // 8. 静态编译与完整性诊断网闸
            var diagnosticReport = ManifestationValidator.ValidateProject(targetProjectDir, projectName);
            var healingHistory = new List<HealingAttemptRecord>();
            var selfHealingTriggered = false;
            var selfHealingSucceeded = false;

            // 9. 自愈闭环执行（若验证未通过且开启了自愈）
            if (!diagnosticReport.IsValid && request.EnableSelfHealing)
            {
                selfHealingTriggered = true;
                var maxAttempts = Math.Max(1, request.MaxHealingAttempts);
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    var healRecord = await _selfHealingService.AttemptRepairAsync(
                        targetProjectDir, projectName, diagnosticReport, attempt, cancellationToken);
                    healingHistory.Add(healRecord);

                    // 重新执行诊断验证
                    diagnosticReport = ManifestationValidator.ValidateProject(targetProjectDir, projectName);
                    if (diagnosticReport.IsValid)
                    {
                        selfHealingSucceeded = true;
                        _logger.LogInformation("[ManifestationEngine] Self-healing succeeded on attempt {Attempt} for '{Project}'", attempt, projectName);
                        break;
                    }
                }
            }

            // 10. 诊断门闩判定：若自愈后仍存在严重错误，执行原子回滚！
            if (!diagnosticReport.IsValid)
            {
                _logger.LogWarning("[ManifestationEngine] Diagnostic validation failed for '{Project}' after self-healing attempts. Triggering atomic rollback.", projectName);
                snapshot.Rollback(_logger);

                var errorDetails = string.Join("\n", diagnosticReport.Issues
                    .Where(i => i.Severity == DiagnosticSeverityLevel.Error)
                    .Select(i => $"- [{i.RuleId}] {i.RelativePath}:{i.Line} -> {i.Message}"));

                var rollbackReason = $"静态诊断未通过且自愈未能消除致命错误，已执行原子回滚保证系统干净状态:\n{errorDetails}";

                await RecordFailureInTaskLogAsync(request.TaskId, rollbackReason, cancellationToken);

                return new ManifestationResult(
                    Success: false,
                    ProjectName: projectName,
                    ProjectDir: targetProjectDir,
                    Files: fileInfos,
                    DatabaseInitialized: dbInitialized,
                    HooksScaffolded: hooksScaffolded,
                    DynamicReloadSucceeded: false,
                    Error: rollbackReason,
                    SelfHealingTriggered: selfHealingTriggered,
                    SelfHealingSucceeded: selfHealingSucceeded,
                    HealingHistory: healingHistory,
                    RolledBack: true,
                    RollbackReason: rollbackReason);
            }

            // 11. 动态无感热重载
            var reloadSucceeded = false;
            if (request.ReloadInMemory)
            {
                reloadSucceeded = await _projectManager.ReloadProjectAsync(projectName);
            }

            // 12. 登记成功状态与执行日志
            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            var summaryReport = BuildManifestationSummary(
                projectName, fileInfos, dbInitialized, hooksScaffolded, reloadSucceeded,
                selfHealingTriggered, selfHealingSucceeded, healingHistory);

            using (var db = await _connectionManager.GetConnectionAsync("agent-studio", cancellationToken))
            {
                await SqliteWriteGate.RunAsync(db, async () =>
                {
                    using var tx = db.BeginTransaction();
                    try
                    {
                        await db.ExecuteAsync(
                            "UPDATE agent_task SET status = 'Manifested (Active)', updated_at = @Now WHERE id = @Id",
                            new { Id = request.TaskId, Now = nowStr }, tx);

                        await db.ExecuteAsync(@"
                            INSERT INTO agent_task_log (task_id, stage, role, provider, success, log_content, error_message, created_at)
                            VALUES (@TaskId, 'Manifestation', 'Manifestation Engine', 'NetYamlForge Kernel', 1, @LogContent, NULL, @CreatedAt)",
                            new
                            {
                                TaskId = request.TaskId,
                                LogContent = summaryReport,
                                CreatedAt = nowStr
                            }, tx);

                        tx.Commit();
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                    return true;
                });
            }

            _logger.LogInformation("[ManifestationEngine] Manifestation completed successfully for '{Project}'", projectName);

            return new ManifestationResult(
                Success: true,
                ProjectName: projectName,
                ProjectDir: targetProjectDir,
                Files: fileInfos,
                DatabaseInitialized: dbInitialized,
                HooksScaffolded: hooksScaffolded,
                DynamicReloadSucceeded: reloadSucceeded,
                Message: $"物理交付完成！应用 '{projectName}' 已成功落地并挂载至运行时。",
                SelfHealingTriggered: selfHealingTriggered,
                SelfHealingSucceeded: selfHealingSucceeded,
                HealingHistory: healingHistory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ManifestationEngine] Manifestation failed for Task {TaskId}. Triggering rollback.", request.TaskId);
            snapshot.Rollback(_logger);

            await RecordFailureInTaskLogAsync(request.TaskId, $"[交付异常回滚]\n{ex.Message}", cancellationToken);

            return new ManifestationResult(
                Success: false,
                ProjectName: projectName,
                ProjectDir: targetProjectDir,
                Files: fileInfos,
                DatabaseInitialized: false,
                HooksScaffolded: false,
                DynamicReloadSucceeded: false,
                Error: ex.Message,
                RolledBack: true,
                RollbackReason: ex.Message);
        }
    }

    private async Task RecordFailureInTaskLogAsync(int taskId, string errorMessage, CancellationToken ct)
    {
        try
        {
            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            using var db = await _connectionManager.GetConnectionAsync("agent-studio", ct);
            await SqliteWriteGate.RunAsync(db, async () =>
            {
                using var tx = db.BeginTransaction();
                await db.ExecuteAsync(
                    "UPDATE agent_task SET status = 'Failed (Rolled back)', updated_at = @Now WHERE id = @Id",
                    new { Id = taskId, Now = nowStr }, tx);

                await db.ExecuteAsync(@"
                    INSERT INTO agent_task_log (task_id, stage, role, provider, success, log_content, error_message, created_at)
                    VALUES (@TaskId, 'Manifestation', 'Manifestation Engine', 'NetYamlForge Kernel', 0, @LogContent, @Error, @CreatedAt)",
                    new
                    {
                        TaskId = taskId,
                        LogContent = errorMessage,
                        Error = errorMessage,
                        CreatedAt = nowStr
                    }, tx);
                tx.Commit();
                return true;
            });
        }
        catch { }
    }

    private string ResolveProjectsRoot()
    {
        var contentRoot = _env.ContentRootPath;
        var candidate1 = Path.Combine(contentRoot, "projects");
        if (Directory.Exists(candidate1)) return candidate1;

        var candidate2 = Path.Combine(Directory.GetCurrentDirectory(), "NetYamlForge", "projects");
        if (Directory.Exists(candidate2)) return candidate2;

        var candidate3 = Path.Combine(Directory.GetCurrentDirectory(), "projects");
        if (Directory.Exists(candidate3)) return candidate3;

        return candidate1;
    }

    private static Dictionary<string, string> ExtractFilesFromMarkdown(string markdown)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void AddIfValid(string path, string content)
        {
            path = path.Trim().Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(path) && path.Contains('.'))
            {
                result[path] = content.TrimEnd();
            }
        }

        foreach (Match match in FenceWithPathRegex.Matches(markdown))
        {
            AddIfValid(match.Groups[1].Value, match.Groups[2].Value);
        }

        foreach (Match match in HeaderFileRegex.Matches(markdown))
        {
            AddIfValid(match.Groups[1].Value, match.Groups[2].Value);
        }

        foreach (Match match in InsideCommentFileRegex.Matches(markdown))
        {
            AddIfValid(match.Groups[1].Value, match.Groups[2].Value);
        }

        return result;
    }

    private static string ResolveProjectName(string? overrideName, string taskTitle, Dictionary<string, string> files)
    {
        if (!string.IsNullOrWhiteSpace(overrideName)) return overrideName.Trim().ToLowerInvariant();

        foreach (var (path, content) in files)
        {
            if (path.EndsWith("project.yaml", StringComparison.OrdinalIgnoreCase) || path.EndsWith("project.yml", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(content, @"(?:^|\n)\s*name:\s*['""]?([a-zA-Z0-9_\-]+)['""]?", RegexOptions.Multiline);
                if (match.Success) return match.Groups[1].Value.Trim().ToLowerInvariant();
            }
        }

        foreach (var path in files.Keys)
        {
            var match = Regex.Match(path, @"projects/([a-zA-Z0-9_\-]+)/", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value.Trim().ToLowerInvariant();
        }

        var titleMatch = Regex.Match(taskTitle, @"\(([a-zA-Z0-9_\-\s]+)\)");
        if (titleMatch.Success)
        {
            var raw = titleMatch.Groups[1].Value.Trim().ToLowerInvariant().Replace(' ', '-');
            if (!string.IsNullOrWhiteSpace(raw)) return raw;
        }

        return "generated-app";
    }

    private static string NormalizeRelativePath(string rawPath, string projectName)
    {
        var path = rawPath.Replace('\\', '/').TrimStart('/');
        if (path.StartsWith("NetYamlForge/", StringComparison.OrdinalIgnoreCase))
        {
            path = path["NetYamlForge/".Length..];
        }
        if (path.StartsWith("projects/", StringComparison.OrdinalIgnoreCase))
        {
            path = path["projects/".Length..];
        }
        if (path.StartsWith($"{projectName}/", StringComparison.OrdinalIgnoreCase))
        {
            path = path[(projectName.Length + 1)..];
        }
        return path;
    }

    private static string GenerateFallbackProjectYaml(string projectName, string title)
    {
        return $@"name: {projectName}
displayName: ""{title}""
database:
  type: sqlite
  path: database/{projectName}.db
layout:
  title: ""{title}""
  landingPageByRole:
    admin: ""/{projectName}/Page/HomePage""
    user: ""/{projectName}/Page/HomePage""
features:
  pages: true
  dashboard: true
";
    }

    private bool ScaffoldMissingStubsIfAny(string targetProjectDir, string projectName)
    {
        try
        {
            var entitiesDir = Path.Combine(targetProjectDir, "entities");
            if (!Directory.Exists(entitiesDir)) return false;

            var hooksDir = Path.Combine(targetProjectDir, "Hooks");
            Directory.CreateDirectory(hooksDir);

            var existingCsFiles = Directory.GetFiles(hooksDir, "*.cs", SearchOption.AllDirectories);
            var existingCode = string.Join("\n", existingCsFiles.Select(File.ReadAllText));

            var scaffoldedAny = false;
            var cleanProjectNamespace = Regex.Replace(projectName, @"[^a-zA-Z0-9]", "");

            foreach (var yml in Directory.GetFiles(entitiesDir, "*.yml").Concat(Directory.GetFiles(entitiesDir, "*.yaml")))
            {
                var content = File.ReadAllText(yml);

                var handlerMatches = Regex.Matches(content, @"handler:\s*['""]?([a-zA-Z0-9_]+)['""]?");
                foreach (Match match in handlerMatches)
                {
                    var handlerName = match.Groups[1].Value.Trim();
                    if (string.IsNullOrWhiteSpace(handlerName)) continue;

                    if (!Regex.IsMatch(existingCode, $@"Name\s*=>\s*""{Regex.Escape(handlerName)}"""))
                    {
                        var className = ToPascalCase(handlerName) + "Handler";
                        var stubCode = $@"using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.{cleanProjectNamespace}.Hooks;

/// <summary>
/// 自动生成的动作处理器桩实现 ({handlerName})
/// </summary>
public sealed class {className} : ICustomActionHandler
{{
    public string Name => ""{handlerName}"";

    public Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {{
        return Task.FromResult(ActionHandlerResult.Success());
    }}
}}
";
                        var stubPath = Path.Combine(hooksDir, $"{className}.cs");
                        if (!File.Exists(stubPath))
                        {
                            File.WriteAllText(stubPath, stubCode, Encoding.UTF8);
                            existingCode += "\n" + stubCode;
                            scaffoldedAny = true;
                            _logger.LogInformation("[ManifestationEngine] Auto-scaffolded missing action handler stub: {StubPath}", stubPath);
                        }
                    }
                }
            }

            return scaffoldedAny;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ManifestationEngine] Failed during automatic hook scaffolding");
            return false;
        }
    }

    private static string ToPascalCase(string text)
    {
        var parts = text.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var p in parts)
        {
            if (p.Length > 0)
            {
                sb.Append(char.ToUpperInvariant(p[0]));
                if (p.Length > 1) sb.Append(p[1..]);
            }
        }
        return sb.Length > 0 ? sb.ToString() : "Custom";
    }

    private static string BuildManifestationSummary(
        string projectName,
        List<ManifestedFileInfo> files,
        bool dbInit,
        bool hooksScaffolded,
        bool reloadOk,
        bool selfHealingTriggered,
        bool selfHealingSucceeded,
        IReadOnlyList<HealingAttemptRecord> healingHistory)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 🚀 物理自举交付报告 (App Manifestation Report)");
        sb.AppendLine();
        sb.AppendLine($"- **目标应用工程**: `projects/{projectName}/`");
        sb.AppendLine($"- **交付物理文件数**: {files.Count} 个");
        sb.AppendLine($"- **数据库物理初始化**: {(dbInit ? "✅ 成功初始化 SQLite 结构" : "ℹ️ 沿用现有数据库")}");
        sb.AppendLine($"- **脚手架与 Hook 门闩**: {(hooksScaffolded ? "✅ 已自动补全缺失桩代码" : "✅ 全部符合规范")}");
        sb.AppendLine($"- **编译与诊断自愈**: {(selfHealingTriggered ? (selfHealingSucceeded ? $"✅ 触发自愈并成功修复 ({healingHistory.Count} 次尝试)" : "❌ 自愈未通过") : "✅ 语法与契约诊断一次性通过")}");
        sb.AppendLine($"- **动态运行时热挂载**: {(reloadOk ? "✅ 成功热加载注册" : "⚠️ 需构建服务生效")}");
        sb.AppendLine();

        if (healingHistory.Count > 0)
        {
            sb.AppendLine("### 🛠️ 自愈修复轨迹：");
            foreach (var h in healingHistory)
            {
                sb.AppendLine($"- **尝试 #{h.Attempt}** [{h.Strategy}]: {(h.Succeeded ? "成功" : "部分/未解决")}");
                sb.AppendLine($"  - 修复文件: {string.Join(", ", h.RepairedFiles)}");
                sb.AppendLine($"  - 说明: {h.Summary.Replace("\n", " ")}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("### 物理交付文件一览：");
        foreach (var f in files)
        {
            sb.AppendLine($"- `{f.RelativePath}` ({f.ByteCount} bytes) — `{f.Status}`");
        }
        sb.AppendLine();
        sb.AppendLine("### 运行时访问入口：");
        sb.AppendLine($"- 业务前台门户: [/{projectName}/Page/HomePage](http://localhost:5001/{projectName}/Page/HomePage)");
        sb.AppendLine($"- 管理运维后台: [/{projectName}/DynamicEntity/Index](http://localhost:5001/{projectName}/DynamicEntity/Index)");
        return sb.ToString();
    }
}
