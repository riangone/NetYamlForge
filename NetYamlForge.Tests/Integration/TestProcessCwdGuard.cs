// ファイル概要: テスト用 WebApplicationFactory 群が共有する、プロセス全体の
// カレントディレクトリ(CWD)汚染対策ヘルパーです。
//
// 背景: Program.cs は起動時に Directory.SetCurrentDirectory(ContentRootPath) を実行します
// (system.db 多重化や CWD ドリフトによるホットリロード不具合を防ぐための本番向け仕様)。
// WebApplicationFactory<Program> はテストごとに Program.Main を実際に呼び出すため、
// 各テストフィクスチャが使い捨ての一時ディレクトリを ContentRoot として渡すと、
// プロセスの CWD がその一時ディレクトリに書き換わります。
// フィクスチャの Dispose で一時ディレクトリを削除すると、CWD は「もう存在しないディレクトリ」
// を指したままになり、以降どのテストが WebApplication.CreateBuilder() や
// Directory.GetCurrentDirectory() を呼んでも getcwd() が ENOENT で
// FileNotFoundException を投げて落ちます(実行順序に依存して失敗数が変動する原因)。
//
// 対策: 一時ディレクトリを削除する前に、プロセスの存続期間中ずっと消えない
// AppContext.BaseDirectory (テストアセンブリの出力先) へ CWD を退避させる。
using System;
using System.IO;

namespace NetYamlForge.Tests.Integration;

internal static class TestProcessCwdGuard
{
    /// <summary>
    /// 一時 ContentRoot を削除する直前に呼び出し、プロセス CWD を安全な場所へ戻す。
    /// </summary>
    public static void RestoreSafeCwd()
    {
        try
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        }
        catch
        {
            // CWD 復元に失敗しても後続の削除処理は継続する(ベストエフォート)。
        }
    }
}
