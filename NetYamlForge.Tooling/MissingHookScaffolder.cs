// ファイル概要: YAML 定義を走査し、未実装のフック・アクションハンドラーのスタブを一括生成する CLI ツール。
// --scaffold-missing-hooks --project=<name> [--with-tests] から呼ばれます。

using System.Text;
using System.Text.RegularExpressions;
using NetYamlForge.Models;
using NetYamlForge.Services.Cli;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services;

/// <summary>
/// YAML に定義されたフック名・ハンドラー名のうち、Hooks/ ディレクトリに実装が存在しないものを
/// 一括でスタブ生成します。既存ファイルは上書きしません。
/// </summary>
public static class MissingHookScaffolder
{
    // Hooks/ 内 .cs ファイルで Name プロパティを検出するパターン
    private static readonly Regex NamePropertyPattern =
        new(@"Name\s*=>\s*""([^""]+)""", RegexOptions.Compiled | RegexOptions.Multiline);

    public static int Run(
        string currentDir,
        string? projectName,
        bool withTests,
        CliScaffoldResult? result = null)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            Console.Error.WriteLine("エラー: --project=<name> を指定してください。");
            PrintUsage();
            return 1;
        }

        string contentRoot;
        try { contentRoot = ResolveContentRoot(currentDir); }
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

        // project.yaml から DB タイプを読み込む
        var dbType = ReadDbType(projectDir);

        // エンティティメタデータをロード
        EntityMetadataProvider entityMetadata;
        try { entityMetadata = new EntityMetadataProvider(projectDir, dbType); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: エンティティYAML読み込みに失敗しました: {ex.Message}");
            return 1;
        }

        var entities = entityMetadata.GetAll();

        // YAML から全フック名・ハンドラー名を収集
        var hookNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var handlerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var hooksService = new EntityHooksService();

        foreach (var (entityName, def) in entities)
        {
            if (def.Hooks != null)
            {
                var selectors = new System.Func<EntityHooksDefinition, object?>[]
                {
                    h => h.BeforeCreate, h => h.BeforeUpdate, h => h.BeforeDelete,
                    h => h.AfterCreate,  h => h.AfterUpdate,  h => h.AfterDelete
                };
                foreach (var selector in selectors)
                {
                    var list = hooksService.GetHookList(def.Hooks, selector);
                    if (list == null) continue;
                    foreach (var name in list)
                    {
                        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('@')) continue;
                        hookNames.Add(name.Split(':', 2)[0].Trim());
                    }
                }
            }

            foreach (var (_, action) in def.Actions)
            {
                if (!string.IsNullOrWhiteSpace(action.Handler))
                    handlerNames.Add(action.Handler.Trim());
            }
        }

        if (hookNames.Count == 0 && handlerNames.Count == 0)
        {
            Console.WriteLine($"プロジェクト '{projectName}' のエンティティYAMLにフック・ハンドラー定義が見つかりません。");
            return 0;
        }

        // Hooks/ ディレクトリ内の実装済み名前を収集
        var hooksDir = Path.Combine(projectDir, "Hooks");
        var implementedNames = CollectImplementedNames(hooksDir);

        var missingHooks = hookNames
            .Where(n => !implementedNames.Contains(n))
            .OrderBy(n => n)
            .ToList();

        var missingHandlers = handlerNames
            .Where(n => !implementedNames.Contains(n))
            .OrderBy(n => n)
            .ToList();

        if (missingHooks.Count == 0 && missingHandlers.Count == 0)
        {
            Console.WriteLine($"✓ プロジェクト '{projectName}' の全フック・ハンドラーは実装済みです。");
            return 0;
        }

        Console.WriteLine($"未実装を検出: フック {missingHooks.Count} 件、ハンドラー {missingHandlers.Count} 件");
        Console.WriteLine();

        Directory.CreateDirectory(hooksDir);
        var pascalProject = HookScaffolder.ToPascalCase(projectName);
        var repoRoot = Path.GetDirectoryName(contentRoot)!;
        var testsDir = Path.Combine(repoRoot, "NetYamlForge.Tests", "Hooks");
        var exitCode = 0;

        // IEntityHook スタブ生成
        if (missingHooks.Count > 0)
        {
            var hookFilePath = Path.Combine(hooksDir, $"{pascalProject}MissingHooks.cs");
            if (File.Exists(hookFilePath))
            {
                Console.WriteLine($"スキップ (既存): {hookFilePath}");
                Console.WriteLine($"  以下のフックを手動で追加してください: {string.Join(", ", missingHooks)}");
                result?.SkippedFiles.Add(hookFilePath);
            }
            else
            {
                var content = GenerateHookStubs(pascalProject, missingHooks);
                File.WriteAllText(hookFilePath, content, new UTF8Encoding(false));
                Console.WriteLine($"生成: {hookFilePath}");
                foreach (var h in missingHooks)
                    Console.WriteLine($"  + IEntityHook: {h}");
                result?.GeneratedFiles.Add(hookFilePath);
            }
        }

        // ICustomActionHandler スタブ生成
        if (missingHandlers.Count > 0)
        {
            var handlerFilePath = Path.Combine(hooksDir, $"{pascalProject}MissingActionHandlers.cs");
            if (File.Exists(handlerFilePath))
            {
                Console.WriteLine($"スキップ (既存): {handlerFilePath}");
                Console.WriteLine($"  以下のハンドラーを手動で追加してください: {string.Join(", ", missingHandlers)}");
                result?.SkippedFiles.Add(handlerFilePath);
            }
            else
            {
                var content = GenerateHandlerStubs(pascalProject, missingHandlers);
                File.WriteAllText(handlerFilePath, content, new UTF8Encoding(false));
                Console.WriteLine($"生成: {handlerFilePath}");
                foreach (var h in missingHandlers)
                    Console.WriteLine($"  + ICustomActionHandler: {h}");
                result?.GeneratedFiles.Add(handlerFilePath);
            }
        }

        // テストファイル生成
        if (withTests && result?.GeneratedFiles.Count > 0)
        {
            Directory.CreateDirectory(testsDir);
            GenerateTestStubs(testsDir, pascalProject, missingHooks, missingHandlers, result);
        }

        Console.WriteLine();
        Console.WriteLine("─── 次のステップ ────────────────────────────────────────");
        Console.WriteLine();
        Console.WriteLine("1. 生成されたスタブファイルのビジネスロジックを実装してください。");
        Console.WriteLine("   スタブは throw new NotImplementedException() の代わりに HookResult.Continue()");
        Console.WriteLine("   を返しています。適切なロジックに置き換えてください。");
        Console.WriteLine();
        Console.WriteLine("2. アプリを再起動して実装が正しく読み込まれるか確認:");
        Console.WriteLine("   dotnet run");
        Console.WriteLine();
        Console.WriteLine("3. 再度バリデーションを確認（起動ログに yaml_config_warn が出ないことを確認）");
        Console.WriteLine();

        result?.NextSteps.Add("生成されたスタブのビジネスロジックを実装する");
        result?.NextSteps.Add("dotnet run で再起動して起動ログの yaml_config_warn を確認");

        return exitCode;
    }

    // ─────────────────────────────────────────────────────────────
    // スタブコード生成
    // ─────────────────────────────────────────────────────────────

    private static string GenerateHookStubs(string pascalProject, List<string> hookNames)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// 自動生成スタブ: dotnet run -- --scaffold-missing-hooks --project={pascalProject.ToLowerInvariant()} で生成");
        sb.AppendLine($"// 各クラスの BeforeAsync / AfterAsync にビジネスロジックを実装してください。");
        sb.AppendLine();
        sb.AppendLine("using System.Data;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using NetYamlForge.Services.Hooks;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine();
        sb.AppendLine($"namespace NetYamlForge.Projects.{pascalProject}.Hooks;");
        sb.AppendLine();

        foreach (var hookName in hookNames)
        {
            var className = ToHookClassName(hookName);
            sb.AppendLine($"public sealed class {className} : IEntityHook");
            sb.AppendLine("{");
            sb.AppendLine($"    private readonly ILogger<{className}> _logger;");
            sb.AppendLine($"    public string Name => \"{hookName}\";");
            sb.AppendLine();
            sb.AppendLine($"    public {className}(ILogger<{className}> logger) => _logger = logger;");
            sb.AppendLine();
            sb.AppendLine("    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)");
            sb.AppendLine("    {");
            sb.AppendLine("        // TODO: 実装してください");
            sb.AppendLine("        return Task.FromResult(HookResult.Continue());");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)");
            sb.AppendLine("        => Task.CompletedTask;");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string GenerateHandlerStubs(string pascalProject, List<string> handlerNames)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// 自動生成スタブ: dotnet run -- --scaffold-missing-hooks --project={pascalProject.ToLowerInvariant()} で生成");
        sb.AppendLine($"// 各クラスの ExecuteAsync にビジネスロジックを実装してください。");
        sb.AppendLine();
        sb.AppendLine("using System.Data;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using NetYamlForge.Services.Hooks;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine();
        sb.AppendLine($"namespace NetYamlForge.Projects.{pascalProject}.Hooks;");
        sb.AppendLine();

        foreach (var handlerName in handlerNames)
        {
            var className = ToHandlerClassName(handlerName);
            sb.AppendLine($"public sealed class {className} : ICustomActionHandler");
            sb.AppendLine("{");
            sb.AppendLine($"    private readonly ILogger<{className}> _logger;");
            sb.AppendLine($"    public string Name => \"{handlerName}\";");
            sb.AppendLine();
            sb.AppendLine($"    public {className}(ILogger<{className}> logger) => _logger = logger;");
            sb.AppendLine();
            sb.AppendLine("    public Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)");
            sb.AppendLine("    {");
            sb.AppendLine("        // TODO: 実装してください");
            sb.AppendLine("        return Task.FromResult(ActionHandlerResult.Success());");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void GenerateTestStubs(
        string testsDir,
        string pascalProject,
        List<string> missingHooks,
        List<string> missingHandlers,
        CliScaffoldResult result)
    {
        if (missingHooks.Count > 0)
        {
            var testFilePath = Path.Combine(testsDir, $"{pascalProject}MissingHooksTests.cs");
            if (!File.Exists(testFilePath))
            {
                var content = GenerateHookTestStubs(pascalProject, missingHooks);
                File.WriteAllText(testFilePath, content, new UTF8Encoding(false));
                Console.WriteLine($"生成(テスト): {testFilePath}");
                result.GeneratedFiles.Add(testFilePath);
            }
        }
        if (missingHandlers.Count > 0)
        {
            var testFilePath = Path.Combine(testsDir, $"{pascalProject}MissingActionHandlersTests.cs");
            if (!File.Exists(testFilePath))
            {
                var content = GenerateHandlerTestStubs(pascalProject, missingHandlers);
                File.WriteAllText(testFilePath, content, new UTF8Encoding(false));
                Console.WriteLine($"生成(テスト): {testFilePath}");
                result.GeneratedFiles.Add(testFilePath);
            }
        }
    }

    private static string GenerateHookTestStubs(string pascalProject, List<string> hookNames)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// 自動生成テストスタブ");
        sb.AppendLine();
        sb.AppendLine("using NetYamlForge.Services.Hooks;");
        sb.AppendLine($"using NetYamlForge.Projects.{pascalProject}.Hooks;");
        sb.AppendLine("using Xunit;");
        sb.AppendLine("using Microsoft.Extensions.Logging.Abstractions;");
        sb.AppendLine();
        sb.AppendLine("namespace NetYamlForge.Tests.Hooks;");
        sb.AppendLine();

        foreach (var hookName in hookNames)
        {
            var className = ToHookClassName(hookName);
            sb.AppendLine($"public class {className}Tests");
            sb.AppendLine("{");
            sb.AppendLine($"    [Fact]");
            sb.AppendLine($"    public async Task BeforeAsync_ValidInput_ReturnsContinue()");
            sb.AppendLine("    {");
            sb.AppendLine($"        var sut = new {className}(NullLogger<{className}>.Instance);");
            sb.AppendLine("        var ctx = new EntityHookContext { Entity = \"entity\", Operation = CrudOperation.Create, Values = new() };");
            sb.AppendLine("        var result = await sut.BeforeAsync(ctx, null!, null);");
            sb.AppendLine("        Assert.False(result.Cancel); // TODO: 実装後に適切なアサートに変更");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    [Fact]");
            sb.AppendLine($"    public void Name_IsCorrectYamlKey() => Assert.Equal(\"{hookName}\", new {className}(NullLogger<{className}>.Instance).Name);");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string GenerateHandlerTestStubs(string pascalProject, List<string> handlerNames)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// 自動生成テストスタブ");
        sb.AppendLine();
        sb.AppendLine("using NetYamlForge.Services.Hooks;");
        sb.AppendLine($"using NetYamlForge.Projects.{pascalProject}.Hooks;");
        sb.AppendLine("using Xunit;");
        sb.AppendLine("using Microsoft.Extensions.Logging.Abstractions;");
        sb.AppendLine();
        sb.AppendLine("namespace NetYamlForge.Tests.Hooks;");
        sb.AppendLine();

        foreach (var handlerName in handlerNames)
        {
            var className = ToHandlerClassName(handlerName);
            sb.AppendLine($"public class {className}Tests");
            sb.AppendLine("{");
            sb.AppendLine("    [Fact]");
            sb.AppendLine($"    public async Task ExecuteAsync_ValidInput_ReturnsSuccess()");
            sb.AppendLine("    {");
            sb.AppendLine($"        var sut = new {className}(NullLogger<{className}>.Instance);");
            sb.AppendLine("        var ctx = new CustomActionContext { Project = \"test\", Entity = \"entity\", Action = \"action\" };");
            sb.AppendLine("        var result = await sut.ExecuteAsync(ctx, null!, null);");
            sb.AppendLine("        Assert.True(result.Ok); // TODO: 実装後に適切なアサートに変更");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    [Fact]");
            sb.AppendLine($"    public void Name_IsCorrectYamlKey() => Assert.Equal(\"{handlerName}\", new {className}(NullLogger<{className}>.Instance).Name);");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    // ─────────────────────────────────────────────────────────────
    // ユーティリティ
    // ─────────────────────────────────────────────────────────────

    /// <summary>Hooks/ 内の .cs ファイルから実装済み Name プロパティ値を収集します。</summary>
    private static HashSet<string> CollectImplementedNames(string hooksDir)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(hooksDir)) return names;

        foreach (var csFile in Directory.GetFiles(hooksDir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var content = File.ReadAllText(csFile);
            foreach (Match m in NamePropertyPattern.Matches(content))
                names.Add(m.Groups[1].Value);
        }
        return names;
    }

    private static string ReadDbType(string projectDir)
    {
        var yamlPath = Path.Combine(projectDir, "project.yaml");
        if (!File.Exists(yamlPath)) return "sqlite";

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        try
        {
            var config = deserializer.Deserialize<ProjectConfig>(File.ReadAllText(yamlPath));
            return (config.Database.Type ?? "sqlite").ToLowerInvariant();
        }
        catch { return "sqlite"; }
    }

    /// <summary>snake_case フック名 → PascalCase + Hook クラス名</summary>
    private static string ToHookClassName(string hookName)
    {
        var pascal = HookScaffolder.ToPascalCase(hookName);
        return pascal.EndsWith("Hook", StringComparison.OrdinalIgnoreCase) ? pascal : pascal + "Hook";
    }

    /// <summary>snake_case ハンドラー名 → PascalCase + Handler クラス名</summary>
    private static string ToHandlerClassName(string handlerName)
    {
        var pascal = HookScaffolder.ToPascalCase(handlerName);
        return pascal.EndsWith("Handler", StringComparison.OrdinalIgnoreCase) ? pascal : pascal + "Handler";
    }

    private static string ResolveContentRoot(string currentDir)
    {
        if (Directory.Exists(Path.Combine(currentDir, "projects"))) return currentDir;
        var sub = Path.Combine(currentDir, "NetYamlForge");
        if (Directory.Exists(Path.Combine(sub, "projects"))) return sub;
        throw new DirectoryNotFoundException($"content root を解決できません: {currentDir}");
    }

    private static void PrintUsage()
    {
        Console.WriteLine();
        Console.WriteLine("使い方:");
        Console.WriteLine("  dotnet run -- --scaffold-missing-hooks --project=<name> [--with-tests]");
        Console.WriteLine();
        Console.WriteLine("例:");
        Console.WriteLine("  dotnet run -- --scaffold-missing-hooks --project=blog");
        Console.WriteLine("  dotnet run -- --scaffold-missing-hooks --project=blog --with-tests");
    }
}
