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
}

/// <summary>
/// プロジェクト固有フックの動的ローダー実装。
/// Roslyn を使用して実行時にフッククラスをコンパイルします。
/// </summary>
public class ProjectHookLoader : IProjectHookLoader
{
    private readonly ILogger<ProjectHookLoader> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, Assembly> _loadedAssemblies;

    public ProjectHookLoader(
        ILogger<ProjectHookLoader> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _loadedAssemblies = new ConcurrentDictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task LoadProjectHooksAsync(string projectName, string projectDir, IProjectHookRegistry registry)
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
                    // DI コンテナから依存関係を解決してインスタンス化
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

    public async Task LoadProjectBusinessLogicAsync(string projectName, string projectDir, IProjectBusinessLogicRegistry registry)
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
            // アセンブリが既に読み込まれている場合は再利用
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

            // ビジネスロジックの読み込み
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

            // バリデーションの読み込み
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

            // データ変換の読み込み
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

    private async Task<Assembly?> CompileHooksAsync(string projectName, IEnumerable<string> sourceFiles)
    {
        var sourceEntries = sourceFiles
            .Select(filePath => new
            {
                FilePath = filePath,
                Source = File.ReadAllText(filePath)
            })
            .ToList();

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IEntityHook).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.ILogger<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.ActivatorUtilities).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Concurrent.ConcurrentDictionary<,>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.TaskExtensions).Assembly.Location),
        };

        // 参照アセンブリを追加
        var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        references.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Collections.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Runtime.Extensions.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Linq.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Text.RegularExpressions.dll")));

        // Dapper の参照を追加
        try
        {
            // アプリケーションベースディレクトリから Dapper.dll を検索
            var appBase = AppContext.BaseDirectory;
            var dapperPath = Path.Combine(appBase, "Dapper.dll");
            if (File.Exists(dapperPath))
            {
                references.Add(MetadataReference.CreateFromFile(dapperPath));
                _logger.LogDebug("Dapper.dll を発見：{Path}", dapperPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Dapper.dll の読み込み中にエラーが発生しました：{Message}", ex.Message);
        }

        // Microsoft.CSharp の参照を追加（dynamic 型に必要）
        try
        {
            var csharpPath = Path.Combine(assemblyPath, "Microsoft.CSharp.dll");
            if (File.Exists(csharpPath))
            {
                references.Add(MetadataReference.CreateFromFile(csharpPath));
            }
        }
        catch { }

        // Microsoft.Extensions.Options の参照を追加（IOptions<> に必要）
        try
        {
            var optionsAssembly = typeof(Microsoft.Extensions.Options.IOptions<>).Assembly;
            if (!string.IsNullOrEmpty(optionsAssembly.Location))
            {
                references.Add(MetadataReference.CreateFromFile(optionsAssembly.Location));
                _logger.LogDebug("Microsoft.Extensions.Options.dll をロード：{Location}", optionsAssembly.Location);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Microsoft.Extensions.Options のロード中にエラーが発生しました：{Message}", ex.Message);
        }

        // System.Console の参照を追加（Console.WriteLine に必要）
        try
        {
            var consoleAssembly = typeof(System.Console).Assembly;
            if (!string.IsNullOrEmpty(consoleAssembly.Location))
            {
                references.Add(MetadataReference.CreateFromFile(consoleAssembly.Location));
                _logger.LogDebug("System.Console.dll をロード：{Location}", consoleAssembly.Location);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("System.Console のロード中にエラーが発生しました：{Message}", ex.Message);
        }

        // Microsoft.AspNetCore.Identity の参照を追加（PasswordHasher<T> に必要）
        try
        {
            // PasswordHasher<T> 型からアセンブリを取得
            var identityAssembly = typeof(Microsoft.AspNetCore.Identity.PasswordHasher<>).Assembly;
            if (!string.IsNullOrEmpty(identityAssembly.Location))
            {
                references.Add(MetadataReference.CreateFromFile(identityAssembly.Location));
                _logger.LogDebug("Microsoft.AspNetCore.Identity.dll をロード：{Location}", identityAssembly.Location);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Microsoft.AspNetCore.Identity のロード中にエラーが発生しました：{Message}", ex.Message);
        }

        var compilation = CSharpCompilation.Create(
            $"ProjectHooks_{projectName}",
            sourceEntries.Select(entry => CSharpSyntaxTree.ParseText(entry.Source, path: entry.FilePath)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithAllowUnsafe(true));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

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
        return Assembly.Load(ms.ToArray());
    }

    public async Task LoadProjectActionHandlersAsync(string projectName, string projectDir, IProjectActionRegistry registry)
    {
        var hooksDir = Path.Combine(projectDir, "Hooks");
        if (!Directory.Exists(hooksDir))
            return;

        var csFiles = Directory.GetFiles(hooksDir, "*.cs", SearchOption.AllDirectories);
        if (csFiles.Length == 0)
            return;

        try
        {
            // アセンブリが既に読み込まれている場合は再利用
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

    private static string GetHookCompileHint(string diagnosticId)
    {
        return diagnosticId switch
        {
            "CS0246" => "型/名前空間が見つかりません。using 追加または参照アセンブリを確認してください。",
            "CS0103" => "名前が現在のコンテキストに存在しません。変数名・スコープを確認してください。",
            "CS1061" => "メンバーが見つかりません。型定義と拡張メソッド using を確認してください。",
            "CS1503" => "引数の型が一致していません。メソッド定義と渡す値の型を確認してください。",
            _ => string.Empty
        };
    }
}
