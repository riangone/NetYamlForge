// ファイル概要: パス・トラバーサルを防止するための安全なパス解決・検証ユーティリティ。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NetYamlForge.Services;

/// <summary>
/// 多テナント環境でのファイルシステム操作に対するセキュリティガード（パス・トラバーサル防止）
/// </summary>
public static class PathSafetyGuard
{
    /// <summary>
    /// 対象パスを正規化し、指定されたベースディレクトリ配下に存在することを確認します。
    /// 存在しない、またはベースディレクトリ外へのアクセス（../ など）が検出された場合は例外をスローします。
    /// </summary>
    /// <param name="rawPath">ユーザーまたは設定から入力された検証対象パス</param>
    /// <param name="baseDirectory">許可される最上位のルートディレクトリ（プロジェクトルートなど）</param>
    /// <param name="context">ログや例外メッセージで使用するコンテキスト表記</param>
    /// <returns>解決された絶対パス</returns>
    /// <exception cref="ArgumentException">パスが空の場合</exception>
    /// <exception cref="UnauthorizedAccessException">パス・トラバーサルが検出された場合</exception>
    public static string NormalizeAndValidatePath(string? rawPath, string baseDirectory, string context)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new ArgumentException($"Path cannot be empty in context '{context}'", nameof(rawPath));
        }

        // ベースディレクトリを絶対パスに正規化
        var fullBase = Path.GetFullPath(baseDirectory);

        // 対象パスが絶対パスでない場合はベースディレクトリと結合
        var resolvedPath = rawPath;
        if (!Path.IsPathRooted(resolvedPath))
        {
            resolvedPath = Path.Combine(fullBase, resolvedPath);
        }

        // 対象パスを絶対パスに正規化してドット（.. や .）を解決
        var fullTarget = Path.GetFullPath(resolvedPath);

        // 指定ベースディレクトリ配下で開始しているか検証 (前方一致確認)
        // OS 固有のディレクトリ区切り文字を考慮するため、末尾に区切り文字を付与して比較
        var checkBase = fullBase.EndsWith(Path.DirectorySeparatorChar) 
            ? fullBase 
            : fullBase + Path.DirectorySeparatorChar;

        var checkTarget = fullTarget.EndsWith(Path.DirectorySeparatorChar)
            ? fullTarget
            : fullTarget + Path.DirectorySeparatorChar;

        // 一致しない場合はエラー（ディレクトリ名同名前綴りの欺瞞を防止）
        if (!checkTarget.StartsWith(checkBase, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"[PathSafetyGuard] Path traversal detected in '{context}'. " +
                $"Target path '{rawPath}' (resolved: '{fullTarget}') is outside of base directory '{fullBase}'.");
        }

        return fullTarget;
    }

    /// <summary>
    /// projectName が指定されていないシステムレベルの BatchJob 用の、安全なベースディレクトリを返します。
    /// </summary>
    /// <remarks>
    /// <c>ContentRootPath</c> をそのまま baseDir として使うと、<c>projects/</c> がその直下にあるため
    /// （<see cref="ProjectManager"/> 参照）、"projects/&lt;他テナント&gt;/data.db" のような相対パスが
    /// ".." を一切使わずに <see cref="NormalizeAndValidatePath"/> の検証を通過してしまい、
    /// テナント境界を越えたファイルアクセスが可能になってしまう。
    /// この専用ディレクトリは <c>projects/</c> と兄弟関係（その祖先ではない）にあるため、
    /// システムジョブから他テナントのディレクトリへ到達できない。
    /// </remarks>
    public static string GetSystemJobBaseDir(string contentRootPath)
    {
        var dir = Path.Combine(contentRootPath, "_system_jobs");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 対象パスを正規化し、許可されたルートディレクトリの一覧のいずれか配下に
    /// 存在することを確認します（一致するルートが 1 つも無い場合は例外）。
    /// </summary>
    /// <remarks>
    /// バッチジョブ設定（例: フォトインポートの「サーバーディレクトリ指定」機能）のように、
    /// テナントのプロジェクトディレクトリ外にある絶対パスへの読み取りアクセスを、
    /// 意図的に許可する必要があるユースケース向け。
    /// <paramref name="allowedRoots"/> が空/未設定の場合はセキュアデフォルトとして
    /// 常に拒否する（「設定していない = 全許可」を避けるため）。
    /// </remarks>
    /// <param name="rawPath">ユーザー入力の検証対象パス（絶対パス想定）</param>
    /// <param name="allowedRoots">管理者が明示的に許可したルートディレクトリの一覧</param>
    /// <param name="context">ログや例外メッセージで使用するコンテキスト表記</param>
    /// <returns>解決された絶対パス</returns>
    /// <exception cref="ArgumentException">パスが空の場合</exception>
    /// <exception cref="UnauthorizedAccessException">
    /// 許可ルートが未設定、またはどの許可ルート配下にも一致しない場合
    /// </exception>
    public static string ValidateAgainstAllowList(string? rawPath, IEnumerable<string>? allowedRoots, string context)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new ArgumentException($"Path cannot be empty in context '{context}'", nameof(rawPath));
        }

        var roots = (allowedRoots ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToList();

        if (roots.Count == 0)
        {
            throw new UnauthorizedAccessException(
                $"[PathSafetyGuard] No allowed roots configured for context '{context}'. " +
                "Refusing arbitrary filesystem access by default; an administrator must explicitly allow-list root directories.");
        }

        var fullTarget = Path.GetFullPath(rawPath);

        foreach (var root in roots)
        {
            var fullRoot = Path.GetFullPath(root);
            var checkRoot = fullRoot.EndsWith(Path.DirectorySeparatorChar)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;

            // ルート自体との完全一致、またはルート配下のサブディレクトリであることを許可
            if (string.Equals(fullTarget, fullRoot, StringComparison.OrdinalIgnoreCase) ||
                (fullTarget + Path.DirectorySeparatorChar).StartsWith(checkRoot, StringComparison.OrdinalIgnoreCase))
            {
                return fullTarget;
            }
        }

        throw new UnauthorizedAccessException(
            $"[PathSafetyGuard] Path traversal / unauthorized directory access detected in '{context}'. " +
            $"Target path '{rawPath}' (resolved: '{fullTarget}') is outside of all allowed roots.");
    }
}
