// ファイル概要：バッチジョブのスケルトンファイルを生成する CLI スキャフォールダーです。

using System.IO;

namespace NetYamlForge.Services.Cli;

/// <summary>
/// バッチジョブスキャフォールダー
/// </summary>
public static class BatchJobScaffolder
{
    /// <summary>
    /// バッチジョブのスケルトンを生成する
    /// </summary>
    public static int Run(string rootDir, string? projectName, string jobName, CliScaffoldResult result)
    {
        // rootDir がソリューションディレクトリかアプリケーションディレクトリかを確認
        var projectsDir = Path.Combine(rootDir, "projects");
        if (!Directory.Exists(projectsDir))
        {
            projectsDir = Path.Combine(rootDir, "NetYamlForge", "projects");
        }
        
        if (string.IsNullOrEmpty(projectName))
        {
            // プロジェクト一覧を取得
            var projects = Directory.GetDirectories(projectsDir)
                .Select(Path.GetFileName)
                .Where(p => !string.IsNullOrEmpty(p) && !p.StartsWith("_"))
                .ToList();

            if (projects.Count == 0)
            {
                Console.Error.WriteLine("プロジェクトが見つかりません。");
                return 1;
            }

            Console.WriteLine("プロジェクトを選択してください:");
            for (int i = 0; i < projects.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {projects[i]}");
            }
            Console.Write("> ");
            var input = Console.ReadLine();
            if (int.TryParse(input, out var idx) && idx > 0 && idx <= projects.Count)
            {
                projectName = projects[idx - 1];
            }
            else
            {
                Console.Error.WriteLine("無効な選択です。");
                return 1;
            }
        }

        var projectPath = Path.Combine(projectsDir, projectName!);
        if (!Directory.Exists(projectPath))
        {
            Console.Error.WriteLine($"プロジェクト '{projectName}' が見つかりません。");
            return 1;
        }

        // jobs ディレクトリ作成
        var jobsDir = Path.Combine(projectPath, "jobs");
        if (!Directory.Exists(jobsDir))
        {
            Directory.CreateDirectory(jobsDir);
            Console.WriteLine($"ジョブディレクトリを作成しました：{jobsDir}");
        }

        // SQL ディレクトリ作成
        var sqlDir = Path.Combine(jobsDir, "sql");
        if (!Directory.Exists(sqlDir))
        {
            Directory.CreateDirectory(sqlDir);
        }

        // YAML ファイル生成
        var yamlFile = Path.Combine(jobsDir, $"{jobName}.yml");
        var yamlContent = GenerateJobYaml(jobName);
        File.WriteAllText(yamlFile, yamlContent);
        Console.WriteLine($"ジョブ定義を生成しました：{yamlFile}");

        // SQL ファイル生成
        var sqlFile = Path.Combine(sqlDir, $"{jobName}.sql");
        if (!File.Exists(sqlFile))
        {
            var sqlContent = GenerateSampleSql(jobName);
            File.WriteAllText(sqlFile, sqlContent);
            Console.WriteLine($"SQL テンプレートを生成しました：{sqlFile}");
        }

        // フックテンプレート生成
        var hookDir = Path.Combine(projectPath, "Hooks");
        if (!Directory.Exists(hookDir))
        {
            Directory.CreateDirectory(hookDir);
        }

        var beforeHookFile = Path.Combine(hookDir, $"{jobName}_BeforeHook.cs");
        if (!File.Exists(beforeHookFile))
        {
            var hookContent = GenerateBeforeHook(jobName);
            File.WriteAllText(beforeHookFile, hookContent);
            Console.WriteLine($"Before フックテンプレートを生成しました：{beforeHookFile}");
        }

        result.GeneratedFiles.Add(yamlFile);
        result.GeneratedFiles.Add(sqlFile);
        result.GeneratedFiles.Add(beforeHookFile);

        Console.WriteLine();
        Console.WriteLine("次のステップ:");
        Console.WriteLine($"  1. {yamlFile} を編集してスケジュールと設定をカスタマイズ");
        Console.WriteLine($"  2. {sqlFile} を編集してクエリを記述");
        Console.WriteLine($"  3. アプリケーションを再起動してジョブを有効化");

        return 0;
    }

    private static string GenerateJobYaml(string jobName)
    {
        var displayName = ToDisplayName(jobName);
        
        return $$"""
# {{displayName}} - バッチジョブ定義
# 詳細：docs/guides/batch-jobs.md

jobs:
  {{jobName}}:
    displayName: {{displayName}}
    description: "{{displayName}}を実行します"
    enabled: true
    
    # スケジュール設定
    schedule:
      cron: "0 2 * * *"  # 毎日 2:00 UTC
      timezone: "Asia/Tokyo"
      # intervalSeconds: 3600  # Cron の代わりに間隔で指定も可能
    
    # ジョブタイプ：sql_to_csv, sql_command, stored_procedure
    type: sql_to_csv
    
    # ジョブ設定
    settings:
      sqlFile: jobs/sql/{{jobName}}.sql
      outputFile: jobs/output/stats_{date:yyyyMMdd}.csv
      includeHeader: true
      delimiter: ","
      outputFormat: csv
      # notifyEmails: "admin@example.com"
    
    # フック
    beforeRun:
      - {{jobName}}_check
    
    afterRun:
      - {{jobName}}_notify
    
    # 失敗時ポリシー
    onFailure:
      retryCount: 3
      retryInterval: 300  # 5 分
      logError: true
      notify:
        - admin

""";
    }

    private static string GenerateSampleSql(string jobName)
    {
        return $$"""
-- {{ToDisplayName(jobName)}} - サンプルクエリ
-- このファイルはバッチジョブスキャフォールダーによって生成されました

-- 例：日次統計データの集計
SELECT 
    DATE('now') AS stat_date,
    COUNT(*) AS total_count,
    SUM(amount) AS total_amount
FROM orders
WHERE order_date >= DATE('now', '-1 day')
  AND order_date < DATE('now')
GROUP BY DATE('now');

-- 出力結果は CSV ファイルに書き出されます

""";
    }

    private static string GenerateBeforeHook(string jobName)
    {
        var className = $"{ToPascalCase(jobName)}CheckHook";
        
        return $$"""
using System.Data;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.Hooks;

/// <summary>
/// {{ToDisplayName(jobName)}} の実行前チェックフック
/// </summary>
public class {{className}} : IEntityHook
{
    public Task<HookResult> BeforeAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        // 実行前のチェックロジック
        // 例：データが準備されているか確認
        
        // var count = db.ExecuteScalar<int>("SELECT COUNT(*) FROM orders WHERE processed = 0", transaction: tx);
        // if (count == 0)
        // {
        //     return Task.FromResult(HookResult.Cancel("処理対象データがありません"));
        // }
        
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        // このメソッドは使用されません（Before フック専用）
        return Task.CompletedTask;
    }
}

""";
    }

    private static string ToDisplayName(string name)
    {
        // ケバブケースを日本語風に変換（簡易版）
        var parts = name.Replace("-", " ").Replace("_", " ")
            .Split(' ')
            .Select(s => char.ToUpper(s[0]) + s.Substring(1));
        return string.Join(" ", parts);
    }

    private static string ToPascalCase(string name)
    {
        return string.Concat(name
            .Split('-', '_')
            .Select(s => char.ToUpper(s[0]) + s.Substring(1).ToLower()));
    }
}
