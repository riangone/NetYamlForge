// ファイル概要: ProjectScope の現在プロジェクトの IDashboardConfigProvider に委譲するプロキシです。
// Scoped として登録し、リクエストごとに正しいプロジェクトのダッシュボード設定を提供します。

using NetYamlForge.Models;

namespace NetYamlForge.Services;

public class ProjectAwareDashboardConfigProvider : IDashboardConfigProvider
{
    private readonly IDashboardConfigProvider _inner;

    public ProjectAwareDashboardConfigProvider(ProjectScope scope)
    {
        _inner = scope.Current.DashboardConfig;
    }

    public DashboardConfig GetConfig() => _inner.GetConfig();
}
