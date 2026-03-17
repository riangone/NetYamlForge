// ファイル概要: リクエストスコープのプロジェクト名を AsyncLocal<T> で保持する静的コンテキストクラスです。
// ProjectMiddleware がリクエスト開始時に設定し、YamlKeyLocalizer がローカライズ解決時に参照します。
// AsyncLocal を使用するため、スレッド・タスク切り替え後も正しく伝播します。

using System.Threading;

namespace NetYamlForge.Localization;

public static class LocalizationProjectContext
{
    private static readonly AsyncLocal<string?> ProjectNameHolder = new();

    public static string? CurrentProjectName
    {
        get => ProjectNameHolder.Value;
        set => ProjectNameHolder.Value = value;
    }
}
