using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.Project.Loading;

public class HookAssemblyCompiler
{
    private readonly ILogger<HookAssemblyCompiler> _logger;
    private readonly HookMetadataReferenceCache _referenceCache;
    private readonly CollectibleAssemblyManager _assemblyManager;

    public HookAssemblyCompiler(
        ILogger<HookAssemblyCompiler> logger,
        HookMetadataReferenceCache referenceCache,
        CollectibleAssemblyManager assemblyManager)
    {
        _logger = logger;
        _referenceCache = referenceCache;
        _assemblyManager = assemblyManager;
    }

    public string CalculateSourceHash(IEnumerable<string> sourceFiles)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var sortedFiles = sourceFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        var combinedBytes = new List<byte>();

        foreach (var file in sortedFiles)
        {
            var relativePath = Path.GetFileName(file);
            var content = File.ReadAllText(file);
            combinedBytes.AddRange(System.Text.Encoding.UTF8.GetBytes(relativePath));
            combinedBytes.AddRange(System.Text.Encoding.UTF8.GetBytes(content));
        }

        var hashBytes = sha256.ComputeHash(combinedBytes.ToArray());
        return Convert.ToHexString(hashBytes);
    }

    public async Task<Assembly?> CompileHooksAsync(string projectName, IEnumerable<string> sourceFiles)
    {
        var hash = CalculateSourceHash(sourceFiles);
        var cacheDir = Path.Combine(Directory.GetCurrentDirectory(), "cache", "ProjectHooks");
        if (!Directory.Exists(cacheDir))
        {
            Directory.CreateDirectory(cacheDir);
        }
        var dllPath = Path.Combine(cacheDir, $"{projectName}_{hash}.dll");
        var pdbPath = Path.Combine(cacheDir, $"{projectName}_{hash}.pdb");

        if (File.Exists(dllPath))
        {
            _logger.LogInformation("プロジェクト '{Project}' のキャッシュされたフックアセンブリを使用します (Hash: {Hash})", projectName, hash);
            try
            {
                _assemblyManager.UnloadAlcInternal(projectName);

                var cachedAlc = _assemblyManager.GetOrCreate(projectName);

                using var dllStream = File.OpenRead(dllPath);
                if (File.Exists(pdbPath))
                {
                    using var pdbStream = File.OpenRead(pdbPath);
                    return cachedAlc.LoadFromStream(dllStream, pdbStream);
                }
                return cachedAlc.LoadFromStream(dllStream);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "キャッシュされたアセンブリのロードに失敗しました。再コンパイルします：{Project}", projectName);
                try { File.Delete(dllPath); File.Delete(pdbPath); }
                catch (Exception deleteEx)
                {
                    _logger.LogDebug(deleteEx, "Failed to delete corrupted cached assembly files for project: {Project}", projectName);
                }
            }
        }

        var sourceEntries = sourceFiles
            .Select(filePath => new
            {
                FilePath = filePath,
                SourceText = Microsoft.CodeAnalysis.Text.SourceText.From(File.ReadAllText(filePath), System.Text.Encoding.UTF8)
            })
            .ToList();

        var references = _referenceCache.GetMetadataReferences();

        var syntaxTrees = sourceEntries
            .Select(entry => CSharpSyntaxTree.ParseText(entry.SourceText, path: entry.FilePath))
            .ToList();

        var compilation = CSharpCompilation.Create(
            $"ProjectHooks_{projectName}",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithAllowUnsafe(false));

        var securityValidator = new HookSecurityValidator();
        var violations = securityValidator.Validate(compilation);

        if (violations.Count > 0)
        {
            _logger.LogError(
                "[{ErrorCode}] プロジェクト '{Project}' のフックセキュリティ検証に失敗しました：{Violations}",
                "HOOK_SECURITY_VIOLATION", projectName, string.Join("; ", violations));
            return null;
        }

        using var ms = new MemoryStream();
        using var pdbMs = new MemoryStream();
        var result = compilation.Emit(ms, pdbMs);

        if (!result.Success)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d =>
                {
                    var lineSpan = d.Location.GetLineSpan();
                    var fileName = Path.GetFileName(lineSpan.Path);
                    var line = lineSpan.StartLinePosition.Line + 1;
                    var column = lineSpan.StartLinePosition.Character + 1;
                    var hint = HookCompileDiagnostics.Hint(d.Id);
                    return $"{fileName}({line},{column}) {d.Id}: {d.GetMessage()}{(string.IsNullOrEmpty(hint) ? string.Empty : $" | Hint: {hint}")}";
                })
                .ToList();

            _logger.LogError(
                "[{ErrorCode}] プロジェクト '{Project}' のフックコンパイルエラー：{Errors}",
                "HOOK_COMPILE_DIAGNOSTICS", projectName, string.Join(Environment.NewLine, errors));
            return null;
        }

        ms.Seek(0, SeekOrigin.Begin);
        pdbMs.Seek(0, SeekOrigin.Begin);

        try
        {
            await File.WriteAllBytesAsync(dllPath, ms.ToArray());
            await File.WriteAllBytesAsync(pdbPath, pdbMs.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "フックアセンブリのキャッシュ保存に失敗しました：{Project}", projectName);
        }

        ms.Seek(0, SeekOrigin.Begin);
        pdbMs.Seek(0, SeekOrigin.Begin);

        _assemblyManager.UnloadAlcInternal(projectName);

        var alc = _assemblyManager.GetOrCreate(projectName);

        return alc.LoadFromStream(ms, pdbMs);
    }
}
