// ファイル概要：プロジェクトの実行時情報を保持する不変オブジェクトです。
// ProjectManager が生成し、ProjectScope 経由でリクエストスコープ内に提供します。

using NetYamlForge.Models;

namespace NetYamlForge.Services;

public class ProjectInfo
{
    public string Name { get; init; } = default!;
    public string DisplayName { get; init; } = default!;
    public string ProjectDir { get; init; } = default!;
    public string DatabaseType { get; init; } = "sqlite";
    public string ConnectionString { get; init; } = default!;
    public IEntityMetadataProvider EntityMetadata { get; init; } = default!;
    public IDashboardConfigProvider DashboardConfig { get; init; } = default!;
    public IPageMetadataProvider PageMetadata { get; init; } = NullPageMetadataProvider.Instance;
    /// <summary>
    /// プロジェクト固有のレイアウト設定（project.yaml から読み込み）。
    /// </summary>
    public ProjectLayoutConfig? Layout { get; init; }
    public ProjectCalendarConfig? Calendar { get; init; }
    /// <summary>プロジェクト固有のメール設定。null の場合はグローバル設定を使用。</summary>
    public ProjectEmailConfig? Email { get; init; }
}
