// ファイル概要：プロジェクト固有の Hooks/ ディレクトリからフッククラスを動的に読み込みます。
// 各プロジェクトは Hooks/ サブディレクトリに独自のフッククラスを配置できます。

using System.Collections.Concurrent;
using System.Reflection;
using NetYamlForge.Services.Hooks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace NetYamlForge.Services;

/// <summary>
/// プロジェクト固有のフックを動的に読み込むローダー。
/// プロジェクトの Hooks/ ディレクトリ配下の .cs ファイルをコンパイルし、
/// IEntityHook 実装を自動的に登録します。
/// </summary>
public interface IProjectHookLoader
{
    /// <summary>
    /// 指定プロジェクトのフックを読み込んで登録します。
    /// </summary>
    Task LoadProjectHooksAsync(string projectName, string projectDir, IProjectHookRegistry registry);

    /// <summary>
    /// 指定プロジェクトのビジネスロジックを読み込んで登録します。
    /// </summary>
    Task LoadProjectBusinessLogicAsync(string projectName, string projectDir, IProjectBusinessLogicRegistry registry);

    /// <summary>
    /// 指定プロジェクトのカスタムアクションハンドラーを読み込んで登録します。
    /// </summary>
    Task LoadProjectActionHandlersAsync(string projectName, string projectDir, IProjectActionRegistry registry);

    /// <summary>
    /// 指定プロジェクトのロード済みアセンブリをアンロードし、レジストリ登録を解除します。
    /// </summary>
    void UnloadProjectAssembly(string projectName);
}

/// <summary>
/// プロジェクト固有フックの動的ローダー実装。
/// Roslyn を使用して実行時にフッククラスをコンパイルします。
/// </summary>
public class ProjectHookLoader : IProjectHookLoader
{
    private readonly ILogger<ProjectHookLoader> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProjectHookRegistry _hookRegistry;
    private readonly IProjectBusinessLogicRegistry _bizRegistry;
    private readonly IProjectActionRegistry _actionRegistry;
    private readonly ConcurrentDictionary<string, CollectibleAssemblyLoadContext> _assemblyContexts;
    private readonly ConcurrentDictionary<string, Assembly> _loadedAssemblies;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _projectLocks;
    private static List<MetadataReference>? _cachedReferences;
    private static readonly object _refLock = new();

    public ProjectHookLoader(
        ILogger<ProjectHookLoader> logger,
        IServiceScopeFactory scopeFactory,
        IProjectHookRegistry hookRegistry,
        IProjectBusinessLogicRegistry bizRegistry,
        IProjectActionRegistry actionRegistry)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _hookRegistry = hookRegistry;
        _bizRegistry = bizRegistry;
        _actionRegistry = actionRegistry;
        _assemblyContexts = new ConcurrentDictionary<string, CollectibleAssemblyLoadContext>(StringComparer.OrdinalIgnoreCase);
        _loadedAssemblies = new ConcurrentDictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        _projectLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
    }

    private SemaphoreSlim GetProjectLock(string projectName)
    {
        return _projectLocks.GetOrAdd(projectName, _ => new SemaphoreSlim(1, 1));
    }

    public async Task LoadProjectHooksAsync(string projectName, string projectDir, IProjectHookRegistry registry)
    {
        var @lock = GetProjectLock(projectName);
        await @lock.WaitAsync();
        try
        {
            var hooksDir = Path.Combine(projectDir, "Hooks");
            if (!Directory.Exists(hooksDir))
            {
                _logger.LogDebug("プロジェクト '{Project}' に Hooks/ ディレクトリが存在しません", projectName);
                return;
            }

            var csFiles = Directory.GetFiles(hooksDir, "*.cs", SearchOption.AllDirectories);
            if (csFiles.Length == 0)
            {
                _logger.LogDebug("プロジェクト '{Project}' の Hooks/ ディレクトリに .cs ファイルがありません", projectName);
                return;
            }

            try
            {
                var assembly = await CompileHooksAsync(projectName, csFiles);
                if (assembly == null)
                {
                    _logger.LogError("[{ErrorCode}] プロジェクト '{Project}' のフックコンパイルに失敗しました",
                        "HOOK_COMPILE_FAILED", projectName);
                    return;
                }

                _loadedAssemblies[projectName] = assembly;

                using var scope = _scopeFactory.CreateScope();
                var serviceProvider = scope.ServiceProvider;

                var hookTypes = assembly.GetTypes()
                    .Where(t => typeof(IEntityHook).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                var registeredCount = 0;
                foreach (var hookType in hookTypes)
                {
                    try
                    {
                        var hook = ActivatorUtilities.CreateInstance(serviceProvider, hookType);
                        if (hook is IEntityHook hookInstance)
                        {
                            registry.Register(projectName, hookInstance);
                            registeredCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[{ErrorCode}] プロジェクト '{Project}' のフック '{HookType}' の初期化に失敗しました",
                            "HOOK_INIT_FAILED", projectName, hookType.FullName);
                    }
                }

                _logger.LogInformation(
                    "プロジェクト '{Project}' のフックを読み込みました：{Count} 件 ({Files} ファイル)",
                    projectName, registeredCount, csFiles.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{ErrorCode}] プロジェクト '{Project}' のフック読み込み中にエラーが発生しました",
                    "HOOK_LOAD_FAILED", projectName);
            }
        }
        finally
        {
            @lock.Release();
        }
    }

    public async Task LoadProjectBusinessLogicAsync(string projectName, string projectDir, IProjectBusinessLogicRegistry registry)
    {
        var @lock = GetProjectLock(projectName);
        await @lock.WaitAsync();
        try
        {
            var hooksDir = Path.Combine(projectDir, "Hooks");
            if (!Directory.Exists(hooksDir))
            {
                return;
            }

            var csFiles = Directory.GetFiles(hooksDir, "*.cs", SearchOption.AllDirectories);
            if (csFiles.Length == 0)
            {
                return;
            }

            try
            {
                if (!_loadedAssemblies.TryGetValue(projectName, out var assembly))
                {
                    assembly = await CompileHooksAsync(projectName, csFiles);
                    if (assembly == null)
                    {
                        return;
                    }
                    _loadedAssemblies[projectName] = assembly;
                }

                using var scope = _scopeFactory.CreateScope();
                var serviceProvider = scope.ServiceProvider;

                var businessLogicTypes = assembly.GetTypes()
                    .Where(t => typeof(IProjectBusinessLogic).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var logicType in businessLogicTypes)
                {
                    try
                    {
                        var logic = ActivatorUtilities.CreateInstance(serviceProvider, logicType);
                        if (logic is IProjectBusinessLogic logicInstance)
                        {
                            registry.Register(projectName, logicInstance);
                            await logicInstance.InitializeAsync();
                            _logger.LogInformation(
                                "プロジェクト '{Project}' のビジネスロジック '{Type}' を初期化しました",
                                projectName, logicType.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[{ErrorCode}] プロジェクト '{Project}' のビジネスロジック '{Type}' の初期化に失敗しました",
                            "BIZLOGIC_INIT_FAILED", projectName, logicType.FullName);
                    }
                }

                var validatorTypes = assembly.GetTypes()
                    .Where(t => typeof(IProjectValidator).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var validatorType in validatorTypes)
                {
                    try
                    {
                        var validator = ActivatorUtilities.CreateInstance(serviceProvider, validatorType);
                        if (validator is IProjectValidator validatorInstance)
                        {
                            registry.RegisterValidator(projectName, validatorInstance);
                            _logger.LogInformation(
                                "プロジェクト '{Project}' のバリデーション '{Type}' を登録しました",
                                projectName, validatorType.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[{ErrorCode}] プロジェクト '{Project}' のバリデーション '{Type}' の登録に失敗しました",
                            "VALIDATOR_REGISTER_FAILED", projectName, validatorType.FullName);
                    }
                }

                var transformerTypes = assembly.GetTypes()
                    .Where(t => typeof(IProjectDataTransformer).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var transformerType in transformerTypes)
                {
                    try
                    {
                        var transformer = ActivatorUtilities.CreateInstance(serviceProvider, transformerType);
                        if (transformer is IProjectDataTransformer transformerInstance)
                        {
                            registry.RegisterDataTransformer(projectName, transformerInstance);
                            _logger.LogInformation(
                                "プロジェクト '{Project}' のデータ変換 '{Type}' を登録しました",
                                projectName, transformerType.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[{ErrorCode}] プロジェクト '{Project}' のデータ変換 '{Type}' の登録に失敗しました",
                            "TRANSFORMER_REGISTER_FAILED", projectName, transformerType.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{ErrorCode}] プロジェクト '{Project}' のビジネスロジック読み込み中にエラーが発生しました",
                    "BIZLOGIC_LOAD_FAILED", projectName);
            }
        }
        finally
        {
            @lock.Release();
        }
    }

    private string CalculateSourceHash(IEnumerable<string> sourceFiles)
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

    private List<MetadataReference> GetMetadataReferences()
    {
        lock (_refLock)
        {
            if (_cachedReferences != null)
            {
                return _cachedReferences.ToList();
            }

            var references = new List<MetadataReference>();
            var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                    {
                        if (addedPaths.Add(assembly.Location))
                        {
                            references.Add(MetadataReference.CreateFromFile(assembly.Location));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("アセンブリ参照の追加中にエラー（スキップ）：{Name}, {Message}", assembly.FullName, ex.Message);
                }
            }

            try
            {
                var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
                var stdLibs = new[] { "System.Runtime.dll", "System.Collections.dll", "System.Runtime.Extensions.dll", "System.Linq.dll", "System.Text.RegularExpressions.dll", "Microsoft.CSharp.dll", "System.IO.Compression.dll", "System.IO.Compression.ZipFile.dll", "System.Text.Json.dll" };
                foreach (var lib in stdLibs)
                {
                    var fullPath = Path.Combine(assemblyPath, lib);
                    if (File.Exists(fullPath) && addedPaths.Add(fullPath))
                    {
                        references.Add(MetadataReference.CreateFromFile(fullPath));
                    }
                }
            }
            catch { }

            try
            {
                var identityAssembly = typeof(Microsoft.AspNetCore.Identity.PasswordHasher<>).Assembly;
                if (!string.IsNullOrEmpty(identityAssembly.Location) && addedPaths.Add(identityAssembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(identityAssembly.Location));
                }
            }
            catch { }

            _cachedReferences = references;
            return _cachedReferences.ToList();
        }
    }

    private async Task<Assembly?> CompileHooksAsync(string projectName, IEnumerable<string> sourceFiles)
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
                UnloadAlcInternal(projectName);

                var cachedContextName = $"ProjectContext_{projectName}_{Guid.NewGuid():N}";
                var cachedAlc = new CollectibleAssemblyLoadContext(cachedContextName);
                _assemblyContexts[projectName] = cachedAlc;

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
                try { File.Delete(dllPath); File.Delete(pdbPath); } catch {}
            }
        }

        var sourceEntries = sourceFiles
            .Select(filePath => new
            {
                FilePath = filePath,
                SourceText = Microsoft.CodeAnalysis.Text.SourceText.From(File.ReadAllText(filePath), System.Text.Encoding.UTF8)
            })
            .ToList();

        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            $"ProjectHooks_{projectName}",
            sourceEntries.Select(entry => CSharpSyntaxTree.ParseText(entry.SourceText, path: entry.FilePath)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithAllowUnsafe(true));

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
                    var hint = GetHookCompileHint(d.Id);
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

        UnloadAlcInternal(projectName);

        var contextName = $"ProjectContext_{projectName}_{Guid.NewGuid():N}";
        var alc = new CollectibleAssemblyLoadContext(contextName);
        _assemblyContexts[projectName] = alc;

        return alc.LoadFromStream(ms, pdbMs);
    }

    private void UnloadAlcInternal(string projectName)
    {
        if (_assemblyContexts.TryRemove(projectName, out var oldAlc))
        {
            try
            {
                oldAlc.Unload();
                TrackUnload(projectName, oldAlc);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "古い AssemblyLoadContext のアンロードに失敗：{Project}", projectName);
            }
        }
    }

    private void TrackUnload(string projectName, System.Runtime.Loader.AssemblyLoadContext alc)
    {
        var weakRef = new WeakReference(alc);
        Task.Run(async () =>
        {
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(1000);
            }

            if (weakRef.IsAlive)
            {
                _logger.LogWarning("警告: プロジェクト '{Project}' の AssemblyLoadContext (Collectible) がアンロード後に GC によって回収されませんでした。メモリリークの可能性があります。", projectName);
            }
            else
            {
                _logger.LogInformation("プロジェクト '{Project}' の AssemblyLoadContext が正常に GC によって回収されました。", projectName);
            }
        });
    }

    public void UnloadProjectAssembly(string projectName)
    {
        var @lock = GetProjectLock(projectName);
        @lock.Wait();
        try
        {
            _logger.LogInformation("アンロード中: プロジェクト '{Project}' の Assembly と Registry 登録", projectName);

            _hookRegistry.Clear(projectName);
            _bizRegistry.Clear(projectName);
            _actionRegistry.Clear(projectName);

            UnloadAlcInternal(projectName);

            _loadedAssemblies.TryRemove(projectName, out _);

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            @lock.Release();
        }
    }

    private class CollectibleAssemblyLoadContext : System.Runtime.Loader.AssemblyLoadContext
    {
        public CollectibleAssemblyLoadContext(string name) : base(name, isCollectible: true)
        {
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            return null;
        }
    }

    public async Task LoadProjectActionHandlersAsync(string projectName, string projectDir, IProjectActionRegistry registry)
    {
        var @lock = GetProjectLock(projectName);
        await @lock.WaitAsync();
        try
        {
            var hooksDir = Path.Combine(projectDir, "Hooks");
            if (!Directory.Exists(hooksDir))
                return;

            var csFiles = Directory.GetFiles(hooksDir, "*.cs", SearchOption.AllDirectories);
            if (csFiles.Length == 0)
                return;

            try
            {
                if (!_loadedAssemblies.TryGetValue(projectName, out var assembly))
                {
                    assembly = await CompileHooksAsync(projectName, csFiles);
                    if (assembly == null)
                        return;
                    _loadedAssemblies[projectName] = assembly;
                }

                using var scope = _scopeFactory.CreateScope();
                var serviceProvider = scope.ServiceProvider;

                var handlerTypes = assembly.GetTypes()
                    .Where(t => typeof(ICustomActionHandler).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var handlerType in handlerTypes)
                {
                    try
                    {
                        var handler = ActivatorUtilities.CreateInstance(serviceProvider, handlerType);
                        if (handler is ICustomActionHandler h)
                        {
                            registry.Register(projectName, h);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "[{ErrorCode}] プロジェクト '{Project}' のアクションハンドラー '{Type}' の初期化に失敗しました",
                            "ACTION_HANDLER_INIT_FAILED", projectName, handlerType.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[{ErrorCode}] プロジェクト '{Project}' のアクションハンドラー読み込み中にエラーが発生しました",
                    "ACTION_HANDLER_LOAD_FAILED", projectName);
            }
        }
        finally
        {
            @lock.Release();
        }
    }

    private static string GetHookCompileHint(string diagnosticId)
    {
        return diagnosticId switch
        {
            "CS0246" => "型/名前空間が見つかりません。using 追加または参照アセンブリを確認してください。",
            "CS0103" => "名前が現在のコンテキストに存在しません。変数名・スコープを確認してください。",
            "CS1061" => "メンバーが見つかりません。型定義と拡張メソッド using を確認してください。",
            "CS1503" => "引数の型が一致していません。メソッド定義と渡す値 of を確認してください。",
            _ => string.Empty
        };
    }
}
