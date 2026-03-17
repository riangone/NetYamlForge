// ファイル概要: プロジェクト別ページ行検証実装を管理するレジストリ。
// PageRowMutationService が projectName をキーに検証実装を取得するために使用します。

namespace NetYamlForge.Services.Hooks;

/// <summary>
/// プロジェクト別の IProjectPageMutationValidator を管理するレジストリ。
/// </summary>
public interface IProjectPageMutationValidatorRegistry
{
    /// <summary>
    /// 指定プロジェクトの検証実装を返します。
    /// 登録されていない場合は null。
    /// </summary>
    IProjectPageMutationValidator? Get(string projectName);
}

public class ProjectPageMutationValidatorRegistry : IProjectPageMutationValidatorRegistry
{
    private readonly Dictionary<string, IProjectPageMutationValidator> _validators;
    private readonly ILogger<ProjectPageMutationValidatorRegistry> _logger;

    public ProjectPageMutationValidatorRegistry(
        IEnumerable<IProjectPageMutationValidator> validators,
        ILogger<ProjectPageMutationValidatorRegistry> logger)
    {
        _logger = logger;
        _validators = new Dictionary<string, IProjectPageMutationValidator>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in validators)
        {
            _validators[v.ProjectName] = v;
            _logger.LogInformation("ページ行検証を登録：{ProjectName}", v.ProjectName);
        }
    }

    public IProjectPageMutationValidator? Get(string projectName)
    {
        _validators.TryGetValue(projectName, out var v);
        return v;
    }
}
