// ファイル概要: プロジェクト固有フックのコードテンプレートを生成するスキャフォールダー。
// --scaffold-hook CLI コマンドから呼ばれ、Hook実装クラスとテストファイルを同時生成する。

using System.Text;
using System.Text.RegularExpressions;
using NetYamlForge.Services.Cli;

namespace NetYamlForge.Services;

/// <summary>
/// 責務: IEntityHook 実装クラスとそのユニットテストを一対で生成する。
/// 生成されたコードはテンプレートであり、業務ロジックは開発者が埋める。
/// </summary>
public static class HookScaffolder
{
    private static readonly Regex ValidNamePattern =
        new(@"^[A-Za-z][A-Za-z0-9]*$", RegexOptions.Compiled);

    public static int Run(
        string currentDir,
        string? projectName,
        string? hookName,
        bool withTests,
        CliScaffoldResult? result = null)
    {
        // ─── 引数検証 ───────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(projectName))
        {
            Console.Error.WriteLine("エラー: --project=<name> を指定してください。");
            PrintUsage();
            return 1;
        }

        if (string.IsNullOrWhiteSpace(hookName))
        {
            Console.Error.WriteLine("エラー: --name=<HookName> を指定してください（例: ValidateInventory）。");
            PrintUsage();
            return 1;
        }

        // PascalCase チェック（クラス名として有効かどうか）
        if (!ValidNamePattern.IsMatch(hookName))
        {
            Console.Error.WriteLine($"エラー: フック名 '{hookName}' はアルファベット・数字のみ使用できます（例: ValidateInventory）。");
            return 1;
        }

        // ─── パス解決 ────────────────────────────────────────────
        string contentRoot;
        try
        {
            contentRoot = ResolveContentRoot(currentDir);
        }
        catch (DirectoryNotFoundException ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return 1;
        }

        var projectDir = Path.Combine(contentRoot, "projects", projectName);
        if (!Directory.Exists(projectDir))
        {
            Console.Error.WriteLine($"エラー: プロジェクト '{projectName}' が見つかりません: {projectDir}");
            return 1;
        }

        if (result != null) result.Project = projectName;

        var hooksDir = Path.Combine(projectDir, "Hooks");
        Directory.CreateDirectory(hooksDir);

        // テストプロジェクトは contentRoot の兄弟ディレクトリ
        var repoRoot = Path.GetDirectoryName(contentRoot)!;
        var testsDir = Path.Combine(repoRoot, "NetYamlForge.Tests", "Hooks");

        // ─── 名前の導出 ──────────────────────────────────────────
        var pascalProject = ToPascalCase(projectName);  // "blog" → "Blog"
        var hookClassName = hookName.EndsWith("Hook", StringComparison.OrdinalIgnoreCase)
            ? hookName
            : $"{hookName}Hook";                         // "ValidateInventory" → "ValidateInventoryHook"
        var hookYamlName = ToSnakeCase(hookName);        // "ValidateInventory" → "validate_inventory"

        // ─── Hook ファイル生成 ─────────────────────────────────
        var hookFilePath = Path.Combine(hooksDir, $"{hookClassName}.cs");
        if (File.Exists(hookFilePath))
        {
            Console.WriteLine($"スキップ: ファイルが既に存在します: {hookFilePath}");
            result?.SkippedFiles.Add(hookFilePath);
        }
        else
        {
            var hookContent = GenerateHookClass(pascalProject, hookClassName, hookYamlName);
            File.WriteAllText(hookFilePath, hookContent, new UTF8Encoding(false));
            Console.WriteLine($"生成: {hookFilePath}");
            result?.GeneratedFiles.Add(hookFilePath);
        }

        // ─── テストファイル生成 ────────────────────────────────
        if (withTests)
        {
            Directory.CreateDirectory(testsDir);
            var testFilePath = Path.Combine(testsDir, $"{hookClassName}Tests.cs");
            if (File.Exists(testFilePath))
            {
                Console.WriteLine($"スキップ: ファイルが既に存在します: {testFilePath}");
                result?.SkippedFiles.Add(testFilePath);
            }
            else
            {
                var testContent = GenerateTestClass(pascalProject, hookClassName, hookYamlName);
                File.WriteAllText(testFilePath, testContent, new UTF8Encoding(false));
                Console.WriteLine($"生成: {testFilePath}");
                result?.GeneratedFiles.Add(testFilePath);
            }
        }

        // ─── 次のステップを表示 ─────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("─── 次のステップ ────────────────────────────────────────");
        Console.WriteLine();
        Console.WriteLine($"1. フック実装クラスのビジネスロジックを埋める:");
        Console.WriteLine($"   {hookFilePath}");
        Console.WriteLine();
        Console.WriteLine($"   ※ DI への手動登録は不要です。");
        Console.WriteLine($"     ProjectHookLoader が起動時に Hooks/ ディレクトリを自動スキャンして登録します。");
        Console.WriteLine();
        Console.WriteLine($"2. エンティティ YAML にフック名を追加（projects/{projectName}/entities/<entity>.yml）:");
        Console.WriteLine($"   hooks:");
        Console.WriteLine($"     beforeCreate: [{hookYamlName}]");
        Console.WriteLine($"     beforeUpdate: [{hookYamlName}]");
        Console.WriteLine();
        if (withTests)
        {
            Console.WriteLine($"3. テストを追加・実行:");
            Console.WriteLine($"   dotnet test --filter \"{hookClassName}Tests\"");
            Console.WriteLine();
        }
        Console.WriteLine($"4. アプリを再起動してフックが正しく読み込まれるか確認:");
        Console.WriteLine($"   dotnet run");
        Console.WriteLine();

        result?.NextSteps.Add($"entities/<entity>.yml の hooks.beforeCreate / hooks.beforeUpdate に '{hookYamlName}' を追加");
        result?.NextSteps.Add($"※ DI 手動登録不要: ProjectHookLoader が Hooks/ を自動スキャンして登録します");
        if (withTests) result?.NextSteps.Add($"dotnet test --filter \"{hookClassName}Tests\"");

        return 0;
    }

    // ─────────────────────────────────────────────────────────────
    // テンプレート生成
    // ─────────────────────────────────────────────────────────────

    private static string GenerateHookClass(
        string pascalProject,
        string hookClassName,
        string hookYamlName)
    {
        return $$"""
            // 責務: {{pascalProject}} プロジェクト固有の「{{hookYamlName}}」フックを実装する。
            // entities.yml の hooks.beforeCreate / hooks.beforeUpdate に「{{hookYamlName}}」と記述して使用。

            using System.Data;
            using System.Threading.Tasks;
            using NetYamlForge.Services.Hooks;
            using Microsoft.Extensions.Logging;

            namespace NetYamlForge.Projects.{{pascalProject}}.Hooks;

            /// <summary>
            /// {{pascalProject}} 固有フック: {{hookYamlName}}
            /// <para>entities.yml での使用例:</para>
            /// <code>
            ///   hooks:
            ///     beforeCreate: [{{hookYamlName}}]
            ///     beforeUpdate: [{{hookYamlName}}]
            /// </code>
            /// </summary>
            public sealed class {{hookClassName}} : IEntityHook
            {
                private readonly ILogger<{{hookClassName}}> _logger;

                public string Name => "{{hookYamlName}}";

                public {{hookClassName}}(ILogger<{{hookClassName}}> logger)
                {
                    _logger = logger;
                }

                /// <inheritdoc />
                /// <remarks>
                /// ctx.Values   : フォームフィールド値 Dictionary&lt;string, object?&gt;
                /// ctx.Operation: CrudOperation.Create / Update / Delete
                /// ctx.Entity   : エンティティ名
                /// </remarks>
                public Task<HookResult> BeforeAsync(
                    EntityHookContext ctx,
                    IDbConnection db,
                    IDbTransaction? tx)
                {
                    // TODO: バリデーション・変換ロジックを実装する
                    //
                    // 例: フィールド値を取得
                    // if (!ctx.Values.TryGetValue("fieldName", out var raw))
                    //     return Task.FromResult(HookResult.Continue());
                    //
                    // 例: バリデーション失敗時
                    // return Task.FromResult(HookResult.Abort("エラーメッセージ"));
                    //
                    // 例: DB 参照（同一トランザクション内）
                    // var count = await db.ExecuteScalarAsync<int>(
                    //     "SELECT COUNT(*) FROM table WHERE col = @val",
                    //     new { val = raw }, tx);

                    return Task.FromResult(HookResult.Continue());
                }

                /// <inheritdoc />
                public Task AfterAsync(
                    EntityHookContext ctx,
                    IDbConnection db,
                    IDbTransaction? tx)
                {
                    // TODO: 書き込み成功後の後処理（通知・連携等）を実装する（任意）
                    return Task.CompletedTask;
                }
            }
            """;
    }

    private static string GenerateTestClass(
        string pascalProject,
        string hookClassName,
        string hookYamlName)
    {
        return $$"""
            // 責務: {{hookClassName}} のユニットテスト。
            // 正常系・異常系・フィールド欠損の3パターンを最低限テストする。

            using NetYamlForge.Services.Hooks;
            using NetYamlForge.Projects.{{pascalProject}}.Hooks;
            using Xunit;

            namespace NetYamlForge.Tests.Hooks;

            public class {{hookClassName}}Tests
            {
                // ─── ヘルパー ───────────────────────────────────────────
                private static EntityHookContext MakeCtx(
                    Dictionary<string, object?> values,
                    CrudOperation op = CrudOperation.Create)
                    => new() { Entity = "entity", Operation = op, Values = values };

                // ─── 正常系 ─────────────────────────────────────────────

                [Fact]
                public async Task BeforeAsync_ValidInput_ReturnsContinue()
                {
                    var sut = new {{hookClassName}}(
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<{{hookClassName}}>.Instance);
                    var ctx = MakeCtx(new() { ["fieldName"] = "valid_value" });

                    var result = await sut.BeforeAsync(ctx, null!, null);

                    Assert.False(result.Cancel);
                }

                // ─── 異常系 ─────────────────────────────────────────────

                [Fact]
                public async Task BeforeAsync_InvalidInput_ReturnsAbort()
                {
                    var sut = new {{hookClassName}}(
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<{{hookClassName}}>.Instance);
                    var ctx = MakeCtx(new() { ["fieldName"] = "invalid_value" });

                    // TODO: 不正値を入力してフックが Abort を返すことを確認
                    var result = await sut.BeforeAsync(ctx, null!, null);

                    // TODO: 実装に合わせてアサートを修正
                    // Assert.True(result.Cancel);
                    // Assert.Contains("期待するエラーメッセージ", result.CancelMessage);
                    Assert.False(result.Cancel); // ← 実装後に適切な検証に変更
                }

                // ─── フィールド欠損（スキップ確認）──────────────────────

                [Fact]
                public async Task BeforeAsync_MissingField_ReturnsContinue()
                {
                    var sut = new {{hookClassName}}(
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<{{hookClassName}}>.Instance);
                    var ctx = MakeCtx(new()); // フィールドなし

                    var result = await sut.BeforeAsync(ctx, null!, null);

                    Assert.False(result.Cancel); // フィールドがなければスキップする
                }

                // ─── Hook 名の確認 ──────────────────────────────────────

                [Fact]
                public void Name_IsCorrectYamlKey()
                {
                    var sut = new {{hookClassName}}(
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<{{hookClassName}}>.Instance);
                    Assert.Equal("{{hookYamlName}}", sut.Name);
                }
            }
            """;
    }

    // ─────────────────────────────────────────────────────────────
    // ユーティリティ
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// kebab-case または snake_case の文字列を PascalCase に変換する。
    /// 例: "salesforce-crm" → "SalesforceCrm", "my_project" → "MyProject"
    /// </summary>
    internal static string ToPascalCase(string name)
    {
        var sb = new StringBuilder();
        var capitalizeNext = true;
        foreach (var ch in name)
        {
            if (ch is '-' or '_')
            {
                capitalizeNext = true;
            }
            else
            {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(ch) : ch);
                capitalizeNext = false;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// PascalCase の文字列を snake_case に変換する。
    /// 例: "ValidateInventory" → "validate_inventory"
    /// </summary>
    internal static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (char.IsUpper(ch) && i > 0)
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
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

    private static void PrintUsage()
    {
        Console.WriteLine();
        Console.WriteLine("使い方:");
        Console.WriteLine("  dotnet run -- --scaffold-hook --name=<HookName> --project=<project> [--with-tests]");
        Console.WriteLine();
        Console.WriteLine("例:");
        Console.WriteLine("  dotnet run -- --scaffold-hook --name=ValidateInventory --project=myproject --with-tests");
    }
}
