// ファイル概要: リクエストスコープ内で現在のプロジェクト情報を保持するスコープオブジェクトです。
// ProjectMiddleware が Set() を呼び出し、各 Scoped サービスが Current を参照します。

namespace NetYamlForge.Services;

/// <summary>
/// リクエストスコープで現在アクティブな ProjectInfo を保持します。
/// Scoped として登録し、ProjectMiddleware で初期化してください。
/// </summary>
public class ProjectScope
{
    private ProjectInfo? _current;

    public ProjectInfo Current => _current
        ?? throw new InvalidOperationException("ProjectScope が初期化されていません。ProjectMiddleware が正しく設定されているか確認してください。");

    internal void Set(ProjectInfo project) => _current = project;

    public bool IsSet => _current != null;
}
