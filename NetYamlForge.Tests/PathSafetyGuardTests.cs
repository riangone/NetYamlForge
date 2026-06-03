using System;
using System.IO;
using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

public class PathSafetyGuardTests
{
    private readonly string _baseDir;

    public PathSafetyGuardTests()
    {
        // 测试时使用一个隔离的临时目录作为基准路径
        _baseDir = Path.Combine(Path.GetTempPath(), "NetYamlForge_Test_Base_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDir);
    }

    [Fact]
    public void NormalizeAndValidatePath_ValidRelativePath_ReturnsFullPath()
    {
        var relativePath = "database/app.db";
        var expected = Path.GetFullPath(Path.Combine(_baseDir, relativePath));

        var result = PathSafetyGuard.NormalizeAndValidatePath(relativePath, _baseDir, "Test");

        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeAndValidatePath_ValidFullPath_ReturnsFullPath()
    {
        var targetPath = Path.Combine(_baseDir, "exports", "report.csv");
        
        var result = PathSafetyGuard.NormalizeAndValidatePath(targetPath, _baseDir, "Test");

        Assert.Equal(Path.GetFullPath(targetPath), result);
    }

    [Fact]
    public void NormalizeAndValidatePath_PathEqualsBaseDir_ReturnsFullPath()
    {
        var result = PathSafetyGuard.NormalizeAndValidatePath(_baseDir, _baseDir, "Test");
        Assert.Equal(Path.GetFullPath(_baseDir), result);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("../../etc/passwd")]
    [InlineData("database/../../outside.txt")]
    public void NormalizeAndValidatePath_PathTraversalRelative_ThrowsUnauthorizedAccess(string rawPath)
    {
        Assert.Throws<UnauthorizedAccessException>(() =>
            PathSafetyGuard.NormalizeAndValidatePath(rawPath, _baseDir, "Test"));
    }

    [Fact]
    public void NormalizeAndValidatePath_PathTraversalAbsoluteOutside_ThrowsUnauthorizedAccess()
    {
        var outsideAbsolute = Path.Combine(Path.GetTempPath(), "some_other_folder", "hack.txt");

        Assert.Throws<UnauthorizedAccessException>(() =>
            PathSafetyGuard.NormalizeAndValidatePath(outsideAbsolute, _baseDir, "Test"));
    }

    [Fact]
    public void NormalizeAndValidatePath_SamePrefixFolderDeception_ThrowsUnauthorizedAccess()
    {
        // 比如基准目录为 /tmp/project-todo
        // 试图访问 /tmp/project-todo-backup/file.txt (共享前缀 project-todo，但不是子目录)
        var baseDirWithPrefix = Path.Combine(Path.GetTempPath(), "project-todo");
        var deceivedPath = Path.Combine(Path.GetTempPath(), "project-todo-backup", "file.txt");

        // 确保物理目录存在
        Directory.CreateDirectory(baseDirWithPrefix);
        Directory.CreateDirectory(Path.GetDirectoryName(deceivedPath)!);

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() =>
                PathSafetyGuard.NormalizeAndValidatePath(deceivedPath, baseDirWithPrefix, "Test"));
        }
        finally
        {
            // 清理
            try { Directory.Delete(baseDirWithPrefix, true); } catch {}
            try { Directory.Delete(Path.GetDirectoryName(deceivedPath)!, true); } catch {}
        }
    }

    [Fact]
    public void NormalizeAndValidatePath_EmptyPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            PathSafetyGuard.NormalizeAndValidatePath("", _baseDir, "Test"));
        
        Assert.Throws<ArgumentException>(() =>
            PathSafetyGuard.NormalizeAndValidatePath(null, _baseDir, "Test"));
    }
}
