// ファイル概要: パス・トラバーサルを防止するための安全なパス解決・検証ユーティリティ。

using System;
using System.IO;

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
}
