using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace NetYamlForge.Services.BatchJob.Sdk;

/// <summary>
/// プロジェクト直下の <c>.env</c> ファイルを読み込む共通ヘルパー。
/// 各 BatchExecutor が個別に実装していた <c>.env</c> パースロジックを一元化します。
/// インラインコメント（<c>#</c>）除去と前後クォート除去に対応します。
/// </summary>
public static class ProjectEnvLoader
{
    /// <summary>
    /// 指定パスの <c>.env</c> ファイルをパースして Key/Value 辞書を返します。
    /// ファイルが存在しない場合は空の辞書を返します。
    /// </summary>
    public static Dictionary<string, string> Load(string filePath)
    {
        var env = new Dictionary<string, string>();
        if (!File.Exists(filePath)) return env;

        foreach (var line in File.ReadAllLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0) continue;

            var key   = trimmed[..eqIndex].Trim();
            var value = trimmed[(eqIndex + 1)..].Trim();

            var commentIdx = value.IndexOf('#');
            if (commentIdx >= 0) value = value[..commentIdx].Trim();

            if (value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2)
                value = value[1..^1];

            env[key] = value;
        }
        return env;
    }

    /// <summary>
    /// <c>projects/{projectName}/.env</c> を読み込みます。
    /// </summary>
    public static Dictionary<string, string> LoadForProject(string? projectName)
        => Load(Path.Combine(Directory.GetCurrentDirectory(), "projects", projectName ?? "", ".env"));

    /// <summary>
    /// 単一キーの値を解決します。優先順位は OS 環境変数 &gt; プロジェクト <c>.env</c> &gt; IConfiguration。
    /// </summary>
    public static string? GetValue(string key, string? projectName, IConfiguration? configuration = null)
    {
        var val = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrEmpty(val)) return val;

        if (!string.IsNullOrEmpty(projectName))
        {
            var env = LoadForProject(projectName);
            if (env.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
        }

        return configuration?[key];
    }
}
