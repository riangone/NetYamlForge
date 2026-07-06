// ファイル概要：プロジェクト固有の Hooks/ ディレクトリからフッククラスを動的に読み込みます。
// 各プロジェクトは Hooks/ サブディレクトリに独自のフッククラスを配置できます。

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.Hooks;
using NetYamlForge.Services.Page;
using NetYamlForge.Services.BatchJob;
using NetYamlForge.Services.Project.Loading;

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
    Task UnloadProjectAssemblyAsync(string projectName);
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
    private readonly BatchStepHandlerRegistry _batchRegistry;
    private readonly IPageActionDispatcher _pageActionDispatcher;

    private readonly HookAssemblyCompiler _compiler;
    private readonly CollectibleAssemblyManager _assemblyManager;
    private readonly ProjectLoadLockRegistry _lockRegistry;

    public ProjectHookLoader(
        ILogger<ProjectHookLoader> logger,
        IServiceScopeFactory scopeFactory,
        IProjectHookRegistry hookRegistry,
        IProjectBusinessLogicRegistry bizRegistry,
        IProjectActionRegistry actionRegistry,
        BatchStepHandlerRegistry batchRegistry,
        IPageActionDispatcher pageActionDispatcher,
        HookAssemblyCompiler compiler,
        CollectibleAssemblyManager assemblyManager,
        ProjectLoadLockRegistry lockRegistry)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _hookRegistry = hookRegistry;
        _bizRegistry = bizRegistry;
        _actionRegistry = actionRegistry;
        _batchRegistry = batchRegistry;
        _pageActionDispatcher = pageActionDispatcher;
        _compiler = compiler;
        _assemblyManager = assemblyManager;
        _lockRegistry = lockRegistry;
    }

    public async Task LoadProjectHooksAsync(string projectName, string projectDir, IProjectHookRegistry registry)
    {
        var @lock = _lockRegistry.For(projectName);
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
                var assembly = await _compiler.CompileHooksAsync(projectName, csFiles);
                if (assembly == null)
                {
                    _logger.LogError("[{ErrorCode}] プロジェクト '{Project}' のフックコンパイルに失敗しました",
                        "HOOK_COMPILE_FAILED", projectName);
                    return;
                }

                _assemblyManager.Register(projectName, assembly);

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

                // 动态注册项目自定义的 BatchStepHandler
                var batchStepHandlerTypes = assembly.GetTypes()
                    .Where(t => typeof(IBatchStepHandler).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var handlerType in batchStepHandlerTypes)
                {
                    try
                    {
                        var handlerInstance = (IBatchStepHandler)ActivatorUtilities.CreateInstance(serviceProvider, handlerType);
                        _batchRegistry.Register(handlerInstance.StepType, handlerType);
                        _logger.LogInformation(
                            "プロジェクト '{Project}' のバッチステップハンドラー '{HandlerType}' (Type: {StepType}) を登録しました",
                            projectName, handlerType.FullName, handlerInstance.StepType);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "プロジェクト '{Project}' のバッチステップハンドラー '{HandlerType}' の登録に失敗しました",
                            projectName, handlerType.FullName);
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
        var @lock = _lockRegistry.For(projectName);
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
                if (!_assemblyManager.TryGetLoadedAssembly(projectName, out var assembly) || assembly == null)
                {
                    assembly = await _compiler.CompileHooksAsync(projectName, csFiles);
                    if (assembly == null)
                    {
                        return;
                    }
                    _assemblyManager.Register(projectName, assembly);
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

                var rlsTypes = assembly.GetTypes()
                    .Where(t => typeof(IProjectRlsContextEvaluator).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var rlsType in rlsTypes)
                {
                    try
                    {
                        var evaluator = ActivatorUtilities.CreateInstance(serviceProvider, rlsType);
                        if (evaluator is IProjectRlsContextEvaluator evaluatorInstance)
                        {
                            registry.RegisterRlsContextEvaluator(projectName, evaluatorInstance);
                            _logger.LogInformation(
                                "プロジェクト '{Project}' の RLS エバリュエーター '{Type}' を登録しました",
                                projectName, rlsType.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "プロジェクト '{Project}' の RLS エバリュエーター '{Type}' の登録に失敗しました",
                            projectName, rlsType.FullName);
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

    public async Task UnloadProjectAssemblyAsync(string projectName)
    {
        var @lock = _lockRegistry.For(projectName);
        await @lock.WaitAsync();
        try
        {
            _logger.LogInformation("アンロード中: プロジェクト '{Project}' の Assembly と Registry 登録", projectName);

            _hookRegistry.Clear(projectName);
            _bizRegistry.Clear(projectName);
            _actionRegistry.Clear(projectName);
            _pageActionDispatcher.Clear(projectName);

            _assemblyManager.UnloadAlcInternal(projectName);

            _assemblyManager.RemoveLoadedAssembly(projectName);

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            @lock.Release();
        }
    }

    public async Task LoadProjectActionHandlersAsync(string projectName, string projectDir, IProjectActionRegistry registry)
    {
        var @lock = _lockRegistry.For(projectName);
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
                if (!_assemblyManager.TryGetLoadedAssembly(projectName, out var assembly) || assembly == null)
                {
                    assembly = await _compiler.CompileHooksAsync(projectName, csFiles);
                    if (assembly == null)
                        return;
                    _assemblyManager.Register(projectName, assembly);
                }

                using var scope = _scopeFactory.CreateScope();
                var serviceProvider = scope.ServiceProvider;

                var handlerTypes = assembly.GetTypes()
                    .Where(t => typeof(ICustomActionHandler).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                    .ToList();

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
                            "[{ErrorCode}] プロジェクト '{Project}' のアクションハンドラー '{Type}' の初期化に失败しました",
                            "ACTION_HANDLER_INIT_FAILED", projectName, handlerType.FullName);
                    }
                }

                var pageHandlerTypes = assembly.GetTypes()
                    .Where(t => typeof(IPageActionHandler).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                    .ToList();

                foreach (var handlerType in pageHandlerTypes)
                {
                    try
                    {
                        var handler = ActivatorUtilities.CreateInstance(serviceProvider, handlerType);
                        if (handler is IPageActionHandler h)
                        {
                            _pageActionDispatcher.Register(projectName, h);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "プロジェクト '{Project}' のページアクションハンドラー '{Type}' の初期化に失败しました",
                            projectName, handlerType.FullName);
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
}
