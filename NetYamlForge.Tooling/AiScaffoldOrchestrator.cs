// ファイル概要: --ai-scaffold コマンドの本体。
//
// パイプライン全体の設計思想（過去の会話で合意した方針をそのままコード化したもの）:
//   1. AI に「コード」を書かせない。書かせるのは entities/hooks/batchJobs だけを列挙した
//      構造化 Spec（YAML）で、DB 列の型・PK・FK 参照まで含めて機械的に静的検証できる形にする。
//   2. Spec → DB スキーマ → entities YAML、の順で「決定的に」変換する。
//      entities YAML は EntityYamlScaffolder が実 DB スキーマから逆生成するので、
//      YAML とスキーマがズレるバグ（今までの26子プロジェクトで頻発していたクラス）が構造的に起きない。
//   3. 各ステップの後に強制ゲートを置く（spec 検証 → validate-project 静的検証 → 受け入れ基準の機械照合）。
//      どこかのゲートで失敗したら即座に非ゼロ終了し、後続ステップは実行しない。
//   4. AI による自己レビューは「あれば良い」おまけ（--ai-review 指定時のみ、失敗しても非致命）。
//      判定の根拠をAIの自己申告だけに置かない。
#pragma warning disable DCS003

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.Connection;

namespace NetYamlForge.Services.Cli;

public static class AiScaffoldOrchestrator
{
    public static int Run(string currentDir, string? specPath, bool enableAiReview, CliScaffoldResult result)
    {
        if (string.IsNullOrWhiteSpace(specPath))
        {
            return Fail(result, "--spec=<spec.yaml> を指定してください。");
        }

        // ── gate #1: spec のロード + 純粋な構造検証（DB/ファイルシステム未接触） ──
        AiScaffoldSpec spec;
        try
        {
            spec = AiScaffoldSpec.Load(specPath);
        }
        catch (Exception ex)
        {
            return Fail(result, $"spec 読み込みに失敗しました: {ex.Message}");
        }

        var specErrors = spec.Validate();
        if (specErrors.Count > 0)
        {
            foreach (var e in specErrors) result.Errors.Add($"[gate1:spec] {e}");
            result.Success = false;
            result.ExitCode = 1;
            Console.Error.WriteLine($"❌ [gate1] spec 静的検証で {specErrors.Count} 件のエラーが見つかりました。生成は行いません。");
            foreach (var e in specErrors) Console.Error.WriteLine($"   - {e}");
            return 1;
        }

        result.Project = spec.Project;
        result.Messages.Add(
            $"[gate1] spec 静的検証 OK（entities={spec.Entities.Count}, hooks={spec.Hooks.Count}, batchJobs={spec.BatchJobs.Count}）");

        string contentRoot;
        try
        {
            contentRoot = ResolveContentRoot(currentDir);
        }
        catch (DirectoryNotFoundException ex)
        {
            return Fail(result, ex.Message);
        }

        var projectDir = Path.Combine(contentRoot, "projects", spec.Project);
        var isNewProject = !Directory.Exists(projectDir);

        // ── step 1: プロジェクト雛形の作成（新規のみ）。
        // この時点では DB はまだ空だが、autoScaffold は敢えて有効のままにする。
        // ProjectTemplateScaffolder は autoScaffold=true のときだけ
        // pages/StarterOverview.yaml・views/StarterOverview.cshtml・dashboard.yml の
        // entity 一覧・i18n.yml を書き出す。home-page.yml の「Starter Page」導線は
        // autoScaffold の有無に関わらず常に /Page/StarterOverview を指すため、
        // ここを無効化するとリンク切れ（validate-project gate3 で検出される）を自ら作り込むことになる。
        // 0 テーブルの時点で EntityYamlScaffolder は "[skip] no tables" で正常終了するだけなので、
        // ここでは無害。実スキーマ反映は gate #2 の再実行で行う。
        if (isNewProject)
        {
            var initResult = new CliScaffoldResult { Command = "ai-scaffold:init-project" };
            var initCode = ProjectTemplateScaffolder.Run(
                contentRoot,
                spec.Project,
                spec.DisplayName,
                forceOverwrite: false,
                dbType: spec.DbType,
                dbPath: null,
                dbConnectionString: null,
                autoScaffold: true,
                i18nFallbackMode: null,
                result: initResult);
            if (initCode != 0)
            {
                return Fail(result, "init-project に失敗しました: " + string.Join("; ", initResult.Errors));
            }
            result.GeneratedFiles.AddRange(initResult.GeneratedFiles);
            result.Messages.Add("[step1] init-project でプロジェクト雛形を作成しました");
        }
        else
        {
            result.Messages.Add("[step1] 既存プロジェクトを再利用します（雛形の再生成はスキップ）");
        }

        // ── step 2: spec → DB スキーマ を決定的に投入する（AI にも手書きコードにも頼らない） ──
        try
        {
            var dbPath = Path.Combine(projectDir, "database", $"{spec.Project}.db");
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            MaterializeSchema(dbPath, spec);
            result.Messages.Add($"[step2] DB スキーマを投入しました（{spec.Entities.Count} テーブル）: {Path.Combine("projects", spec.Project, "database", $"{spec.Project}.db")}");
        }
        catch (Exception ex)
        {
            return Fail(result, $"DB スキーマの投入に失敗しました: {ex.Message}");
        }

        // ── gate #2: entities YAML を実 DB スキーマから逆生成する（YAML とスキーマのズレを構造的に排除） ──
        var scaffoldResult = new CliScaffoldResult { Command = "ai-scaffold:scaffold-entities" };
        var scaffoldCode = EntityYamlScaffolder.Run(contentRoot, spec.Project, true, "entities", true, scaffoldResult);
        if (scaffoldCode != 0)
        {
            return Fail(result, "entities YAML の生成に失敗しました: " + string.Join("; ", scaffoldResult.Errors));
        }
        result.GeneratedFiles.AddRange(scaffoldResult.GeneratedFiles);
        result.Messages.Add($"[gate2] entities YAML を DB スキーマから逆生成しました（{scaffoldResult.GeneratedFiles.Count} ファイル）");

        // ── step 3: hook 雛形（spec で明示指定されたもの） ──
        foreach (var hook in spec.Hooks)
        {
            var hookResult = new CliScaffoldResult { Command = "ai-scaffold:scaffold-hook" };
            var hookCode = HookScaffolder.Run(contentRoot, spec.Project, hook.Name, true, hookResult);
            if (hookCode != 0)
            {
                return Fail(result, $"hook '{hook.Name}' の生成に失敗しました: " + string.Join("; ", hookResult.Errors));
            }
            result.GeneratedFiles.AddRange(hookResult.GeneratedFiles);
        }
        if (spec.Hooks.Count > 0)
        {
            result.Messages.Add($"[step3] hook を {spec.Hooks.Count} 件生成しました");
        }

        // ── step 4: CRUD hook の抜け漏れを自動補完（entities に対応する hook が無ければ雛形を追加） ──
        var missingHookResult = new CliScaffoldResult { Command = "ai-scaffold:scaffold-missing-hooks" };
        MissingHookScaffolder.Run(contentRoot, spec.Project, true, missingHookResult);
        result.GeneratedFiles.AddRange(missingHookResult.GeneratedFiles);
        result.Messages.Add($"[step4] 未実装 CRUD hook の自動補完を実行しました（追加 {missingHookResult.GeneratedFiles.Count} 件）");

        // ── step 5: batch job 雛形 ──
        foreach (var job in spec.BatchJobs)
        {
            var jobResult = new CliScaffoldResult { Command = "ai-scaffold:scaffold-batch-job" };
            var jobCode = BatchJobScaffolder.Run(contentRoot, spec.Project, job.Name, jobResult);
            if (jobCode != 0)
            {
                return Fail(result, $"batchJob '{job.Name}' の生成に失敗しました: " + string.Join("; ", jobResult.Errors));
            }
            result.GeneratedFiles.AddRange(jobResult.GeneratedFiles);
        }
        if (spec.BatchJobs.Count > 0)
        {
            result.Messages.Add($"[step5] batchJob を {spec.BatchJobs.Count} 件生成しました");
        }

        // ── step 6: E2E CRUD 統合テスト ──
        if (spec.Entities.Count > 0)
        {
            var e2eResult = new CliScaffoldResult { Command = "ai-scaffold:scaffold-e2e-tests" };
            var e2eCode = E2ETestScaffolder.Run(contentRoot, spec.Project, spec, e2eResult);
            if (e2eCode != 0)
            {
                return Fail(result, "E2E テストの生成に失敗しました: " + string.Join("; ", e2eResult.Errors));
            }
            result.GeneratedFiles.AddRange(e2eResult.GeneratedFiles);
            result.Messages.Add($"[step6] E2E CRUD テストを生成しました（{spec.Entities.Count} エンティティ）");
        }

        // ── gate #3: 静的検証（nav リンク・seed データ・ホームページ）。人手のレビュー待ちにしない。 ──
        var validateResult = new CliScaffoldResult { Command = "ai-scaffold:validate-project" };
        var validateCode = ProjectValidator.Run(contentRoot, spec.Project, validateResult);
        if (validateCode != 0)
        {
            foreach (var e in validateResult.Errors) result.Errors.Add($"[gate3:validate-project] {e}");
            result.Success = false;
            result.ExitCode = 1;
            Console.Error.WriteLine("❌ [gate3] validate-project に失敗しました。");
            return 1;
        }
        result.Messages.Add("[gate3] validate-project OK（nav リンク / seed データ / ホームページ）");

        // ── gate #4: 受け入れ基準チェックリストの機械照合（AI の自己申告だけに頼らない） ──
        foreach (var line in EvaluateAcceptanceCriteria(spec, result))
        {
            result.Messages.Add(line);
        }

        // ── optional: AI によるセルフレビュー（--ai-review 指定時のみ。失敗しても非致命、ゲートを止めない） ──
        if (enableAiReview)
        {
            var review = TryRunAiSelfReview(spec, result);
            result.Messages.Add(!string.IsNullOrWhiteSpace(review)
                ? $"[ai-review] {review}"
                : "[ai-review] 利用可能な CLI チェーンが無かったため、AIセルフレビューはスキップされました（非致命）。");
        }

        result.NextSteps.Add("dotnet build NetYamlForge.slnx");
        result.NextSteps.Add($"dotnet run --project NetYamlForge.Tooling -- --validate-project --project={spec.Project}");
        result.NextSteps.Add($"dotnet run --project NetYamlForge -- （起動後 /{spec.Project} を目視確認してから人手で最終承認する）");

        result.Success = true;
        result.ExitCode = 0;
        Console.WriteLine("✅ ai-scaffold 完了: すべてのゲートを通過しました（entities/hooks/batchJobs は実DBスキーマと同期済み）。");
        return 0;
    }

    // ─────────────────────────────────────────────────────────────
    // step 2 の実体: spec の列定義から CREATE TABLE を機械的に組み立てて実行する。
    // AI にも人間にも SQL を書かせない = 一番バグりやすい層を消す。
    // ─────────────────────────────────────────────────────────────

    private static void MaterializeSchema(string dbFilePath, AiScaffoldSpec spec)
    {
        using var conn = new SqliteConnection($"Data Source={dbFilePath}");
        conn.Open();
        SqliteConnectionHardening.Apply(conn);

        foreach (var entity in spec.Entities)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = BuildCreateTableSql(entity);
            cmd.ExecuteNonQuery();
        }

        // FK 制約を有効化した状態で整合性を確認しておく（壊れた FK 定義を早期に検知する）。
        using var fkCheckCmd = conn.CreateCommand();
        fkCheckCmd.CommandText = "PRAGMA foreign_key_check;";
        using var reader = fkCheckCmd.ExecuteReader();
        if (reader.Read())
        {
            throw new InvalidOperationException("foreign_key_check で整合性エラーが検出されました。spec の foreignKey 定義を見直してください。");
        }
    }

    internal static string BuildCreateTableSql(SpecEntity entity)
    {
        var primaryKeyCols = entity.Columns.Where(c => c.PrimaryKey).ToList();
        var singleAutoPk = primaryKeyCols.Count == 1 && primaryKeyCols[0].Identity;

        var colDefs = entity.Columns.Select(c =>
        {
            var sqlType = MapSpecTypeToSqlite(c.Type);
            var parts = new List<string> { $"[{c.Name}]", sqlType };

            if (singleAutoPk && c.PrimaryKey)
            {
                parts.Add("PRIMARY KEY AUTOINCREMENT");
            }
            else if (c.NotNull)
            {
                parts.Add("NOT NULL");
            }

            if (!string.IsNullOrWhiteSpace(c.Default))
            {
                parts.Add($"DEFAULT {c.Default}");
            }

            return string.Join(" ", parts);
        }).ToList();

        var tableConstraints = new List<string>();
        if (!singleAutoPk && primaryKeyCols.Count > 0)
        {
            tableConstraints.Add($"PRIMARY KEY ({string.Join(", ", primaryKeyCols.Select(c => $"[{c.Name}]"))})");
        }

        foreach (var c in entity.Columns.Where(c => c.ForeignKey != null))
        {
            tableConstraints.Add($"FOREIGN KEY ([{c.Name}]) REFERENCES [{c.ForeignKey!.Table}]([{c.ForeignKey.Column}])");
        }

        var allDefs = colDefs.Concat(tableConstraints);
        return $"CREATE TABLE IF NOT EXISTS [{entity.Table}] (\n  {string.Join(",\n  ", allDefs)}\n);";
    }

    private static string MapSpecTypeToSqlite(string specType) => specType.ToLowerInvariant() switch
    {
        "integer" => "INTEGER",
        "boolean" => "INTEGER",
        "real" => "REAL",
        "numeric" => "NUMERIC",
        "blob" => "BLOB",
        "datetime" => "TEXT",
        _ => "TEXT"
    };

    // ─────────────────────────────────────────────────────────────
    // gate #4: acceptanceCriteria の機械照合。
    // 「entity 名 / hook 名 / batchJob 名」がすでに生成物として存在するかを文字列一致で判定するだけの
    // シンプルなヒューリスティックだが、AI の「できました」という自己申告よりはるかに信頼できる。
    // ─────────────────────────────────────────────────────────────

    private static List<string> EvaluateAcceptanceCriteria(AiScaffoldSpec spec, CliScaffoldResult result)
    {
        var lines = new List<string>();
        if (spec.AcceptanceCriteria.Count == 0)
        {
            lines.Add("[gate4] acceptanceCriteria が spec に定義されていません（推奨: 最低限のチェックリストを spec に書くこと）");
            return lines;
        }

        var haystack = string.Join("\n", result.GeneratedFiles).ToLowerInvariant();
        var knownNames = spec.Entities.Select(e => e.Table)
            .Concat(spec.Hooks.Select(h => h.Name))
            .Concat(spec.BatchJobs.Select(j => j.Name))
            .Select(n => n.ToLowerInvariant())
            .ToList();

        foreach (var criterion in spec.AcceptanceCriteria)
        {
            var matched = knownNames.Any(n => criterion.ToLowerInvariant().Contains(n) && haystack.Contains(n));
            lines.Add(matched
                ? $"[gate4] ✅ {criterion}"
                : $"[gate4] ⚠️  自動照合できず、人手確認が必要: {criterion}");
        }

        return lines;
    }

    // ─────────────────────────────────────────────────────────────
    // optional: AI セルフレビュー（CliChainService 経由でローカルにインストール済みの CLI へ委譲）。
    // 失敗しても例外を外に漏らさない = パイプライン全体を止めない「おまけ」ステップとして扱う。
    // ─────────────────────────────────────────────────────────────

    private static string? TryRunAiSelfReview(AiScaffoldSpec spec, CliScaffoldResult result)
    {
        try
        {
            using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var logger = loggerFactory.CreateLogger<CliChainService>();
            var cliChain = new CliChainService(logger);

            var prompt =
                "以下はYAML駆動フレームワーク向けに自動生成されたサブプロジェクトです。" +
                "コードは書かず、抜け漏れの指摘のみ短く（3行以内）日本語で答えてください。\n" +
                $"project: {spec.Project}\n" +
                $"entities: {string.Join(", ", spec.Entities.Select(e => e.Table))}\n" +
                $"hooks: {string.Join(", ", spec.Hooks.Select(h => h.Name))}\n" +
                $"batchJobs: {string.Join(", ", spec.BatchJobs.Select(j => j.Name))}\n" +
                $"acceptanceCriteria:\n- {string.Join("\n- ", spec.AcceptanceCriteria)}\n" +
                $"生成ファイル数: {result.GeneratedFiles.Count}";

            var reviewResult = cliChain.PromptAsync(prompt, projectName: spec.Project)
                .GetAwaiter().GetResult();

            return reviewResult.Success
                ? reviewResult.Text?.Trim()
                : $"スキップ（利用可能な CLI が無い/失敗: {reviewResult.Error}）";
        }
        catch (Exception ex)
        {
            return $"スキップ（例外、非致命的）: {ex.Message}";
        }
    }

    private static int Fail(CliScaffoldResult result, string message)
    {
        result.Success = false;
        result.ExitCode = 1;
        result.Errors.Add(message);
        Console.Error.WriteLine($"❌ {message}");
        return 1;
    }

    private static string ResolveContentRoot(string currentDir)
    {
        if (Directory.Exists(Path.Combine(currentDir, "projects")))
            return currentDir;

        var sub = Path.Combine(currentDir, "NetYamlForge");
        if (Directory.Exists(Path.Combine(sub, "projects")))
            return sub;

        throw new DirectoryNotFoundException($"content root を解決できません: {currentDir}");
    }
}
