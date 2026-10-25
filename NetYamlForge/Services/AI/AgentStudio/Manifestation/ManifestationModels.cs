using System.Collections.Generic;

namespace NetYamlForge.Services.AI.AgentStudio.Manifestation;

/// <summary>
/// 物理交付请求参数
/// </summary>
public record ManifestationRequest(
    int TaskId,
    string? TargetProjectName = null,
    bool ForceOverwrite = false,
    bool AutoScaffoldHooks = true,
    bool ReloadInMemory = true,
    bool EnableSelfHealing = true,
    int MaxHealingAttempts = 2
);

/// <summary>
/// 诊断问题严重度
/// </summary>
public enum DiagnosticSeverityLevel
{
    Info,
    Warning,
    Error
}

/// <summary>
/// 项目交付诊断问题
/// </summary>
public record DiagnosticIssue(
    string RelativePath,
    int? Line,
    string Message,
    DiagnosticSeverityLevel Severity = DiagnosticSeverityLevel.Error,
    string? RuleId = null
);

/// <summary>
/// 交付验证诊断报告
/// </summary>
public record ManifestationDiagnosticReport(
    bool IsValid,
    IReadOnlyList<DiagnosticIssue> Issues
);

/// <summary>
/// 自愈修补尝试记录
/// </summary>
public record HealingAttemptRecord(
    int Attempt,
    string Strategy,
    bool Succeeded,
    string Summary,
    IReadOnlyList<string> RepairedFiles
);

/// <summary>
/// 单个物理落盘文件的状态信息
/// </summary>
public record ManifestedFileInfo(
    string RelativePath,
    int ByteCount,
    string Status
);

/// <summary>
/// 物理交付与自举挂载结果
/// </summary>
public record ManifestationResult(
    bool Success,
    string ProjectName,
    string ProjectDir,
    IReadOnlyList<ManifestedFileInfo> Files,
    bool DatabaseInitialized,
    bool HooksScaffolded,
    bool DynamicReloadSucceeded,
    string? Message = null,
    string? Error = null,
    bool SelfHealingTriggered = false,
    bool SelfHealingSucceeded = false,
    IReadOnlyList<HealingAttemptRecord>? HealingHistory = null,
    bool RolledBack = false,
    string? RollbackReason = null
);
