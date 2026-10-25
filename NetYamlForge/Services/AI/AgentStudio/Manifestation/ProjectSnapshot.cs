using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.AI.AgentStudio.Manifestation;

/// <summary>
/// 项目物理快照与原子回滚器：负责在交付前捕获项目快照，并在验证失败或自愈用尽时执行原子回滚
/// </summary>
public sealed class ProjectSnapshot
{
    public string ProjectDir { get; }
    public bool ExistedPrior { get; }
    public IReadOnlyDictionary<string, byte[]> OriginalFiles { get; }

    public ProjectSnapshot(string projectDir, bool existedPrior, Dictionary<string, byte[]> originalFiles)
    {
        ProjectDir = projectDir;
        ExistedPrior = existedPrior;
        OriginalFiles = originalFiles;
    }

    /// <summary>
    /// 对目标工程目录拍摄快照
    /// </summary>
    public static ProjectSnapshot Capture(string projectDir)
    {
        var existed = Directory.Exists(projectDir);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        if (existed)
        {
            var fullDir = Path.GetFullPath(projectDir);
            foreach (var filePath in Directory.GetFiles(fullDir, "*", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(fullDir, filePath).Replace('\\', '/');
                try
                {
                    files[relPath] = File.ReadAllBytes(filePath);
                }
                catch
                {
                    // 忽略可能锁定的只读/临时日志文件
                }
            }
        }

        return new ProjectSnapshot(projectDir, existed, files);
    }

    /// <summary>
    /// 执行原子回滚：删除本次新增的脏文件，将修改的文件还原为原始状态
    /// </summary>
    public void Rollback(ILogger? logger = null)
    {
        try
        {
            logger?.LogWarning("[ManifestationEngine] Initiating atomic rollback for project directory: '{ProjectDir}' (ExistedPrior: {Existed})",
                ProjectDir, ExistedPrior);

            if (!Directory.Exists(ProjectDir))
            {
                return;
            }

            if (!ExistedPrior)
            {
                // 新建项目直接整体物理删除，消除任何脏残留
                Directory.Delete(ProjectDir, recursive: true);
                logger?.LogInformation("[ManifestationEngine] Cleanly rolled back newly created project directory: {ProjectDir}", ProjectDir);
                return;
            }

            var fullDir = Path.GetFullPath(ProjectDir);

            // 1. 删除本次新增的文件
            var currentFiles = Directory.GetFiles(fullDir, "*", SearchOption.AllDirectories);
            foreach (var currentFile in currentFiles)
            {
                var relPath = Path.GetRelativePath(fullDir, currentFile).Replace('\\', '/');
                if (!OriginalFiles.ContainsKey(relPath))
                {
                    try
                    {
                        File.Delete(currentFile);
                        logger?.LogInformation("[ManifestationEngine] Rollback deleted newly added file: {RelPath}", relPath);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "[ManifestationEngine] Failed to delete file during rollback: {RelPath}", relPath);
                    }
                }
            }

            // 2. 还原被修改或删除的文件
            foreach (var (relPath, originalBytes) in OriginalFiles)
            {
                var targetFile = Path.Combine(fullDir, relPath);
                var targetParent = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(targetParent) && !Directory.Exists(targetParent))
                {
                    Directory.CreateDirectory(targetParent);
                }

                try
                {
                    var needsRestore = true;
                    if (File.Exists(targetFile))
                    {
                        var currentBytes = File.ReadAllBytes(targetFile);
                        if (currentBytes.AsSpan().SequenceEqual(originalBytes))
                        {
                            needsRestore = false;
                        }
                    }

                    if (needsRestore)
                    {
                        File.WriteAllBytes(targetFile, originalBytes);
                        logger?.LogInformation("[ManifestationEngine] Rollback restored original content: {RelPath}", relPath);
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "[ManifestationEngine] Failed to restore file during rollback: {RelPath}", relPath);
                }
            }

            // 3. 清理可能生成的空目录
            CleanEmptyDirectories(fullDir);

            logger?.LogInformation("[ManifestationEngine] Atomic rollback completed successfully for: {ProjectDir}", ProjectDir);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[ManifestationEngine] Error occurred during atomic rollback for {ProjectDir}", ProjectDir);
        }
    }

    private static void CleanEmptyDirectories(string startLocation)
    {
        foreach (var directory in Directory.GetDirectories(startLocation))
        {
            CleanEmptyDirectories(directory);
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                try { Directory.Delete(directory, false); }
                catch { }
            }
        }
    }
}
