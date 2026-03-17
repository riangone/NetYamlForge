// ファイル概要：プロジェクト固有のビジネスロジックを登録・管理するレジストリです。

using System.Collections.Concurrent;

namespace NetYamlForge.Services.Hooks;

/// <summary>
/// プロジェクト固有のビジネスロジックを管理するレジストリ。
/// </summary>
public interface IProjectBusinessLogicRegistry
{
    /// <summary>
    /// 指定プロジェクトのビジネスロジックを取得します。
    /// </summary>
    IProjectBusinessLogic? Get(string projectName);

    /// <summary>
    /// 指定プロジェクトのバリデーションを取得します。
    /// </summary>
    IProjectValidator? GetValidator(string projectName);

    /// <summary>
    /// 指定プロジェクトのデータ変換を取得します。
    /// </summary>
    IProjectDataTransformer? GetDataTransformer(string projectName);

    /// <summary>
    /// プロジェクトにビジネスロジックを登録します。
    /// </summary>
    void Register(string projectName, IProjectBusinessLogic logic);

    /// <summary>
    /// プロジェクトにバリデーションを登録します。
    /// </summary>
    void RegisterValidator(string projectName, IProjectValidator validator);

    /// <summary>
    /// プロジェクトにデータ変換を登録します。
    /// </summary>
    void RegisterDataTransformer(string projectName, IProjectDataTransformer transformer);
}

/// <summary>
/// IProjectBusinessLogicRegistry のデフォルト実装。
/// </summary>
public class ProjectBusinessLogicRegistry : IProjectBusinessLogicRegistry
{
    private readonly ConcurrentDictionary<string, IProjectBusinessLogic> _businessLogics;
    private readonly ConcurrentDictionary<string, IProjectValidator> _validators;
    private readonly ConcurrentDictionary<string, IProjectDataTransformer> _transformers;
    private readonly ILogger<ProjectBusinessLogicRegistry> _logger;

    public ProjectBusinessLogicRegistry(ILogger<ProjectBusinessLogicRegistry> logger)
    {
        _logger = logger;
        _businessLogics = new ConcurrentDictionary<string, IProjectBusinessLogic>(StringComparer.OrdinalIgnoreCase);
        _validators = new ConcurrentDictionary<string, IProjectValidator>(StringComparer.OrdinalIgnoreCase);
        _transformers = new ConcurrentDictionary<string, IProjectDataTransformer>(StringComparer.OrdinalIgnoreCase);
    }

    public IProjectBusinessLogic? Get(string projectName)
    {
        _businessLogics.TryGetValue(projectName, out var logic);
        return logic;
    }

    public IProjectValidator? GetValidator(string projectName)
    {
        _validators.TryGetValue(projectName, out var validator);
        return validator;
    }

    public IProjectDataTransformer? GetDataTransformer(string projectName)
    {
        _transformers.TryGetValue(projectName, out var transformer);
        return transformer;
    }

    public void Register(string projectName, IProjectBusinessLogic logic)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new ArgumentException("プロジェクト名を指定してください。", nameof(projectName));
        }

        _businessLogics[projectName] = logic;
        _logger.LogDebug("プロジェクト '{Project}' にビジネスロジック '{Type}' を登録しました",
            projectName, logic.GetType().Name);
    }

    public void RegisterValidator(string projectName, IProjectValidator validator)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new ArgumentException("プロジェクト名を指定してください。", nameof(projectName));
        }

        _validators[projectName] = validator;
        _logger.LogDebug("プロジェクト '{Project}' にバリデーション '{Type}' を登録しました",
            projectName, validator.GetType().Name);
    }

    public void RegisterDataTransformer(string projectName, IProjectDataTransformer transformer)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new ArgumentException("プロジェクト名を指定してください。", nameof(projectName));
        }

        _transformers[projectName] = transformer;
        _logger.LogDebug("プロジェクト '{Project}' にデータ変換 '{Type}' を登録しました",
            projectName, transformer.GetType().Name);
    }
}
