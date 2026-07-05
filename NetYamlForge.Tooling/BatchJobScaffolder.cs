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

        result.GeneratedFiles.Add(yamlFile);
        result.GeneratedFiles.Add(sqlFile);

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
jobs:
  {{jobName}}:
    displayName: {{displayName}}
    description: "{{displayName}}を実行します"
    enabled: true

    schedule:
      cron: "0 2 * * *"        # 毎日 02:00
      timezone: "Asia/Tokyo"

    # タイプ: sql_to_csv（SQL 結果を CSV 出力）または sql_command（SQL 実行のみ）
    type: sql_to_csv
    settings:
      sqlFile: jobs/sql/{{jobName}}.sql
      outputFile: "jobs/output/{{jobName}}_{date:yyyyMMdd}.csv"
      includeHeader: true
      delimiter: ","

    onFailure:
      retryCount: 3
      retryInterval: 300       # 5 分後にリトライ
      logError: true

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
