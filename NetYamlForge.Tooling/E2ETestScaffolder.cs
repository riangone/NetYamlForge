using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NetYamlForge.Services.Cli;

public static class E2ETestScaffolder
{
    public static int Run(string contentRoot, string projectName, AiScaffoldSpec spec, CliScaffoldResult result)
    {
        var solutionRoot = ResolveSolutionRoot(contentRoot);
        var testDir = Path.Combine(solutionRoot, "NetYamlForge.Tests", "Integration");
        Directory.CreateDirectory(testDir);

        var pascalProject = ToPascalCase(projectName);

        var fixturePath = Path.Combine(testDir, $"{pascalProject}TestFixture.cs");
        if (File.Exists(fixturePath))
        {
            result.SkippedFiles.Add(fixturePath);
        }
        else
        {
            var fixtureCode = GenerateFixtureClass(pascalProject, projectName, spec);
            File.WriteAllText(fixturePath, fixtureCode);
            result.GeneratedFiles.Add(fixturePath);
        }

        foreach (var entity in spec.Entities)
        {
            var pascalEntity = ToPascalCase(entity.Table);
            var testPath = Path.Combine(testDir, $"{pascalProject}{pascalEntity}CrudEndToEndTests.cs");

            if (File.Exists(testPath))
            {
                result.SkippedFiles.Add(testPath);
                continue;
            }

            var testCode = GenerateCrudTests(pascalProject, projectName, pascalEntity, entity, spec);
            File.WriteAllText(testPath, testCode);
            result.GeneratedFiles.Add(testPath);
        }

        result.Messages.Add($"[step6] E2E CRUD テストを生成しました（{spec.Entities.Count} エンティティ分）");
        return 0;
    }

    // ─────────────────────────────────────────────────────────────
    // Fixture: {PascalProject}TestFixture.cs
    // ─────────────────────────────────────────────────────────────

    private static string GenerateFixtureClass(string pascalProject, string projectName, AiScaffoldSpec spec)
    {
        var schemaSql = string.Join("\n", spec.Entities.Select(AiScaffoldOrchestrator.BuildCreateTableSql));

        return $$"""
// ファイル概要: {{projectName}} プロジェクトの E2E テスト用ファクトリです。
// --ai-scaffold によって自動生成されました。

#pragma warning disable DCS003

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace NetYamlForge.Tests.Integration;

public sealed class {{pascalProject}}TestFixture : WebApplicationFactory<Program>
{
    public const string ProjectName = "{{projectName}}";

    public string TempContentRoot { get; }

    public string TenantDbPath =>
        Path.Combine(TempContentRoot, "projects", ProjectName, "database", $"{ProjectName}.db");

    public string TenantDbConnectionString => $"Data Source={TenantDbPath};Pooling=False";

    private static readonly string[] ExcludedFileSuffixes = { ".db", ".db-wal", ".db-shm", ".env" };
    private static readonly string[] ExcludedDirectoryNames = { "jobs", "Controllers", "bin", "obj" };

    public {{pascalProject}}TestFixture()
    {
        var appProjectDir = FindAppProjectDirectory();
        TempContentRoot = Path.Combine(Path.GetTempPath(), "nyf-{{projectName}}-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempContentRoot);

        foreach (var fileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var source = Path.Combine(appProjectDir, fileName);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(TempContentRoot, fileName));
            }
        }

        Directory.CreateDirectory(Path.Combine(TempContentRoot, "wwwroot"));

        var sourceProjectDir = Path.Combine(appProjectDir, "projects", ProjectName);
        if (!Directory.Exists(sourceProjectDir))
        {
            throw new DirectoryNotFoundException($"プロジェクトが見つかりません: {sourceProjectDir}");
        }

        var targetProjectDir = Path.Combine(TempContentRoot, "projects", ProjectName);
        CopyDirectoryFiltered(sourceProjectDir, targetProjectDir);

        Directory.CreateDirectory(Path.Combine(targetProjectDir, "database"));

        CreateTenantSchema();
    }

    private void CreateTenantSchema()
    {
        using var conn = new SqliteConnection(TenantDbConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
{{schemaSql}}
";
        cmd.ExecuteNonQuery();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(TempContentRoot);
        builder.UseEnvironment("Development");

        var tempSystemDb = Path.Combine(TempContentRoot, "system_test.db");
        builder.ConfigureAppConfiguration((context, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SystemDbPath"] = tempSystemDb
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });

            services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddAuthenticationSchemes(TestAuthHandler.SchemeName)
                    .Build();
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(TempContentRoot))
            {
                Directory.Delete(TempContentRoot, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string FindAppProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "NetYamlForge");
            if (File.Exists(Path.Combine(candidate, "Program.cs")) &&
                Directory.Exists(Path.Combine(candidate, "projects", ProjectName)))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "NetYamlForge アプリプロジェクトのディレクトリが見つかりません。" +
            $"探索開始位置: {AppContext.BaseDirectory}");
    }

    private static void CopyDirectoryFiltered(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            if (ExcludedFileSuffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            File.Copy(file, Path.Combine(targetDir, name));
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var name = Path.GetFileName(subDir);
            if (ExcludedDirectoryNames.Any(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            CopyDirectoryFiltered(subDir, Path.Combine(targetDir, name));
        }
    }
}
""";
    }

    // ─────────────────────────────────────────────────────────────
    // Per-entity test: {PascalEntity}CrudEndToEndTests.cs
    // ─────────────────────────────────────────────────────────────

    private static string GenerateCrudTests(
        string pascalProject,
        string projectName,
        string pascalEntity,
        SpecEntity entity,
        AiScaffoldSpec spec)
    {
        var nonIdentityCols = entity.Columns.Where(c => !c.Identity).ToList();
        var requiredCols = nonIdentityCols.Where(c => c.NotNull && !c.PrimaryKey).ToList();
        var optionalCols = nonIdentityCols.Where(c => !c.NotNull || c.PrimaryKey).ToList();

        var firstRequired = requiredCols.FirstOrDefault() ?? nonIdentityCols.First();
        var searchCol = requiredCols.FirstOrDefault(c =>
            c.Type.Equals("text", StringComparison.OrdinalIgnoreCase) ||
            c.Type.Equals("string", StringComparison.OrdinalIgnoreCase))
            ?? requiredCols.FirstOrDefault()
            ?? nonIdentityCols.First();

        var hasSearch = !entity.Columns.All(c => c.PrimaryKey || c.Identity);
        var pkName = entity.Columns.First(c => c.PrimaryKey).Name;

        var createBlankOptionalForm = GenerateFormFields(nonIdentityCols, fillRequired: true, fillOptional: false);
        var createExplicitForm = GenerateFormFields(nonIdentityCols, fillRequired: true, fillOptional: true);
        var editCreateForm = GenerateFormFields(nonIdentityCols, fillRequired: true, fillOptional: true, overrideRequired: firstRequired.Name, overrideValue: "originalVal");
        var editForm = GenerateFormFields(nonIdentityCols, fillRequired: true, fillOptional: true, overrideRequired: firstRequired.Name, overrideValue: "newVal");
        var editBlankOptionalAllForm = optionalCols.Count > 0
            ? GenerateFormFields(nonIdentityCols, fillRequired: true, fillOptional: true, overrideOptional: optionalCols.First().Name)
            : GenerateFormFields(nonIdentityCols, fillRequired: true, fillOptional: false);
        var deleteCreateForm = GenerateFormFields(nonIdentityCols, fillRequired: true, fillOptional: true, overrideRequired: firstRequired.Name, overrideValue: "subjectVal");
        var searchFormA = GenerateFormFields(nonIdentityCols, fillRequired: true, fillOptional: true, overrideRequired: searchCol.Name, overrideValue: "valAlpha");
        var searchFormB = GenerateFormFields(nonIdentityCols, fillRequired: true, fillOptional: true, overrideRequired: searchCol.Name, overrideValue: "valBeta");

        var searchTestCode = hasSearch
            ? GenerateSearchTestMethod(pascalProject, projectName, pascalEntity, searchCol.Name, entity.Table, searchFormA, searchFormB, pkName)
            : "    // Search test skipped: no suitable searchable column found.";

        return $$"""
// ファイル概要: {{projectName}} の {{entity.Table}} CRUD E2E テストです。
// --ai-scaffold によって自動生成されました。

#pragma warning disable DCS003

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace NetYamlForge.Tests.Integration;

public class {{pascalProject}}{{pascalEntity}}CrudEndToEndTests : IClassFixture<{{pascalProject}}TestFixture>
{
    private const string IndexUrl = "/{{projectName}}/DynamicEntity/Index?entity={{entity.Table}}";
    private const string CreateUrl = "/{{projectName}}/DynamicEntity/Create?entity={{entity.Table}}";

    private static readonly Regex AntiForgeryTokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly {{pascalProject}}TestFixture _fixture;

    public {{pascalEntity}}CrudEndToEndTests({{pascalProject}}TestFixture fixture)
    {
        _fixture = fixture;
    }

    // ── CREATE ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create{{pascalEntity}}_WithBlankOptionalDefaultedColumns_SucceedsAndAppliesDbDefaults()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
{{createBlankOptionalForm}}
        };

        var response = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"Create failed: {(int)response.StatusCode} {response.StatusCode}\n{Truncate(body)}");

        await using var db = new SqliteConnection(_fixture.TenantDbConnectionString);
        var count = await db.QuerySingleOrDefaultAsync<long>(
            $"SELECT COUNT(*) FROM [{{entity.Table}}] WHERE [{{firstRequired.Name}}] = @val",
            new { val = form["{{firstRequired.Name}}"] });
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Create{{pascalEntity}}_WithExplicitOptionalValues_PersistsProvidedValues()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
{{createExplicitForm}}
        };

        var response = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"Create failed: {(int)response.StatusCode} {response.StatusCode}\n{Truncate(body)}");

        await using var db = new SqliteConnection(_fixture.TenantDbConnectionString);
        var count = await db.QuerySingleOrDefaultAsync<long>(
            $"SELECT COUNT(*) FROM [{{entity.Table}}] WHERE [{{firstRequired.Name}}] = @val",
            new { val = form["{{firstRequired.Name}}"] });
        Assert.Equal(1, count);
    }

    // ── EDIT ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Edit{{pascalEntity}}_Changes{{ToPascalCase(firstRequired.Name)}}_Succeeds()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var originalVal = $"orig-{Guid.NewGuid():N}";
        var createForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
{{editCreateForm}}
        };
        var createResp = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(createForm));
        Assert.True(createResp.IsSuccessStatusCode, $"Create failed: {createResp.StatusCode}");

        await using var db = new SqliteConnection(_fixture.TenantDbConnectionString);
        var id = await db.QuerySingleOrDefaultAsync<long?>(
            $"SELECT [{{pkName}}] FROM [{{entity.Table}}] WHERE [{{firstRequired.Name}}] = @val",
            new { val = originalVal });
        Assert.NotNull(id);

        var newVal = $"edited-{Guid.NewGuid():N}";
        var editForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
{{editForm}}
        };
        var editUrl = $"/{{projectName}}/DynamicEntity/Edit?entity={{entity.Table}}&id={id}";
        var editResp = await client.PostAsync(editUrl, new FormUrlEncodedContent(editForm));
        var editBody = await editResp.Content.ReadAsStringAsync();
        Assert.True(editResp.IsSuccessStatusCode,
            $"Edit failed: {(int)editResp.StatusCode} {editResp.StatusCode}\n{Truncate(editBody)}");

        var updated = await db.QuerySingleOrDefaultAsync<string>(
            $"SELECT [{{firstRequired.Name}}] FROM [{{entity.Table}}] WHERE [{{pkName}}] = @id",
            new { id });
        Assert.Equal(newVal, updated);
    }

    [Fact]
    public async Task Edit{{pascalEntity}}_WithBlankOptionalField_PreservesOldValue()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var originalVal = $"preserve-{Guid.NewGuid():N}";
        var createForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
{{editCreateForm}}
        };
        var createResp = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(createForm));
        Assert.True(createResp.IsSuccessStatusCode, $"Create failed: {createResp.StatusCode}");

        await using var db = new SqliteConnection(_fixture.TenantDbConnectionString);
        var id = await db.QuerySingleOrDefaultAsync<long?>(
            $"SELECT [{{pkName}}] FROM [{{entity.Table}}] WHERE [{{firstRequired.Name}}] = @val",
            new { val = originalVal });
        Assert.NotNull(id);

{{(optionalCols.Count > 0
    ? GenerateEditBlankOptionalBody(projectName, entity.Table, pkName, optionalCols.First().Name, editBlankOptionalAllForm)
    : "        // No optional columns to test blank-preserve behavior.")}}
    }

    // ── DELETE ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete{{pascalEntity}}_RemovesRecord()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var subjectVal = $"del-{Guid.NewGuid():N}";
        var createForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
{{deleteCreateForm}}
        };
        var createResp = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(createForm));
        Assert.True(createResp.IsSuccessStatusCode, $"Create failed: {createResp.StatusCode}");

        await using var db = new SqliteConnection(_fixture.TenantDbConnectionString);
        var id = await db.QuerySingleOrDefaultAsync<long?>(
            $"SELECT [{{pkName}}] FROM [{{entity.Table}}] WHERE [{{firstRequired.Name}}] = @val",
            new { val = subjectVal });
        Assert.NotNull(id);

        var deleteUrl = $"/{{projectName}}/DynamicEntity/Delete?entity={{entity.Table}}&id={id}";
        var deleteForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        };
        var deleteResp = await client.PostAsync(deleteUrl, new FormUrlEncodedContent(deleteForm));
        var deleteBody = await deleteResp.Content.ReadAsStringAsync();
        Assert.True(deleteResp.IsSuccessStatusCode,
            $"Delete failed: {(int)deleteResp.StatusCode} {deleteResp.StatusCode}\n{Truncate(deleteBody)}");

        var count = await db.QuerySingleOrDefaultAsync<long>(
            $"SELECT COUNT(*) FROM [{{entity.Table}}] WHERE [{{pkName}}] = @id",
            new { id });
        Assert.Equal(0, count);
    }

    // ── SEARCH ─────────────────────────────────────────────────────────────

{{searchTestCode}}

    // ── ヘルパー ───────────────────────────────────────────────────────────

    private HttpClient CreateClient() =>
        _fixture.CreateDefaultClient(new CookieContainerHandler());

    private async Task<string> GetAntiForgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync(IndexUrl);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = AntiForgeryTokenRegex.Match(html);
        Assert.True(match.Success, "Failed to get anti-forgery token from index page.");
        return match.Groups[1].Value;
    }

    private static string Truncate(string text) =>
        text.Length <= 2000 ? text : text[..2000];
}
""";
    }

    private static string GenerateSearchTestMethod(
        string pascalProject,
        string projectName,
        string pascalEntity,
        string searchColName,
        string entityTable,
        string searchFormA,
        string searchFormB,
        string pkName)
    {
        // Build the search test body as a string (no template escaping issues)
        return $$"""
    [Fact]
    public async Task Search{{pascalEntity}}_By{{ToPascalCase(searchColName)}}_ReturnsMatchingResults()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var searchNs = $"search-{Guid.NewGuid():N}";
        var valAlpha = $"{searchNs}-alpha";
        var valBeta = $"{searchNs}-beta";

        var formA = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
{{searchFormA}}
        };
        var respA = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(formA));
        Assert.True(respA.IsSuccessStatusCode, $"Create alpha failed: {respA.StatusCode}");

        var formB = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
{{searchFormB}}
        };
        var respB = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(formB));
        Assert.True(respB.IsSuccessStatusCode, $"Create beta failed: {respB.StatusCode}");

        var searchUrl = $"/{{projectName}}/DynamicEntity/ListPartial?entity={{entityTable}}&search={searchNs}-alpha&count=true";
        var searchResp = await client.GetAsync(searchUrl);
        var searchBody = await searchResp.Content.ReadAsStringAsync();
        Assert.True(searchResp.IsSuccessStatusCode,
            $"Search failed: {(int)searchResp.StatusCode} {searchResp.StatusCode}\n{Truncate(searchBody)}");

        Assert.Contains(valAlpha, searchBody);
        Assert.DoesNotContain(valBeta, searchBody);
    }
""";
    }

    private static string GenerateEditBlankOptionalBody(
        string projectName,
        string entityTable,
        string pkName,
        string blankFieldName,
        string editBlankOptionalAllForm)
    {
        return $$"""
        var blankField = "{{blankFieldName}}";
        var editForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
{{editBlankOptionalAllForm}}
        };
        var editUrl = $"/{{projectName}}/DynamicEntity/Edit?entity={{entityTable}}&id={id}";
        var editResp = await client.PostAsync(editUrl, new FormUrlEncodedContent(editForm));
        var editBody = await editResp.Content.ReadAsStringAsync();
        Assert.True(editResp.IsSuccessStatusCode,
            $"Edit with blank optional field should not crash: {(int)editResp.StatusCode} {editResp.StatusCode}\n{Truncate(editBody)}");

        var keptValue = await db.QuerySingleOrDefaultAsync<string>(
            $"SELECT [{blankField}] FROM [{{entityTable}}] WHERE [{{pkName}}] = @id",
            new { id });
        Assert.Equal(createForm[blankField], keptValue);
""";
    }

    // ─────────────────────────────────────────────────────────────
    // ヘルパー（フィールドリスト → C# コード生成）
    // ─────────────────────────────────────────────────────────────

    private static string GenerateFormFields(
        List<SpecColumn> columns,
        bool fillRequired,
        bool fillOptional,
        string? overrideRequired = null,
        string? overrideValue = null,
        string? overrideOptional = null)
    {
        var sb = new StringBuilder();
        foreach (var col in columns)
        {
            if (col.PrimaryKey && col.Identity) continue;

            var isReq = col.NotNull && !col.PrimaryKey;
            var hasDefault = !string.IsNullOrWhiteSpace(col.Default);
            var typeName = col.Type;

            string valueExpr;

            if (isReq && overrideRequired == col.Name && overrideValue != null)
            {
                valueExpr = overrideValue;
            }
            else if (hasDefault && overrideOptional == col.Name)
            {
                valueExpr = "\"\"";
            }
            else if (isReq && overrideOptional == col.Name)
            {
                valueExpr = "\"\"";
            }
            else if (hasDefault && !fillOptional)
            {
                continue;
            }
            else if (isReq && hasDefault)
            {
                valueExpr = GenerateTypedValue(typeName, col.Name);
            }
            else if (isReq)
            {
                valueExpr = fillRequired
                    ? $"$\"e2e-{col.Name}-{{Guid.NewGuid():N}}\""
                    : "\"e2e-default\"";
            }
            else
            {
                if (overrideOptional == col.Name)
                {
                    valueExpr = "\"\"";
                }
                else if (fillOptional)
                {
                    valueExpr = GenerateTypedValue(typeName, col.Name);
                }
                else
                {
                    continue;
                }
            }

            sb.AppendLine($"            [\"{col.Name}\"] = {valueExpr},");
        }
        return sb.ToString().TrimEnd();
    }

    private static string GenerateTypedValue(string typeName, string colName)
    {
        if (typeName.Equals("boolean", StringComparison.OrdinalIgnoreCase))
            return "\"1\"";
        if (typeName.Equals("integer", StringComparison.OrdinalIgnoreCase) ||
            typeName.Equals("int", StringComparison.OrdinalIgnoreCase))
            return "\"0\"";
        if (typeName.Equals("datetime", StringComparison.OrdinalIgnoreCase))
            return "DateTime.UtcNow.ToString(\"yyyy-MM-dd HH:mm:ss\")";
        return $"$\"e2e-{colName}-{{Guid.NewGuid():N}}\"";
    }

    private static string ResolveSolutionRoot(string contentRoot)
    {
        var dir = new DirectoryInfo(contentRoot);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NetYamlForge.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return contentRoot;
    }

    internal static string ToPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return string.Join("", input
            .Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
    }
}
