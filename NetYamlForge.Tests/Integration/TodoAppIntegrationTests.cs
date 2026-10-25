using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NetYamlForge.Tests.Integration;

public sealed class TodoAppWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ProjectName = "todo-app";
    public string TempContentRoot { get; }
    public string TenantDbPath =>
        Path.Combine(TempContentRoot, "projects", ProjectName, "database", $"{ProjectName}.db");

    private static readonly string[] ExcludedFileSuffixes = { ".db", ".db-wal", ".db-shm", ".env" };
    private static readonly string[] ExcludedDirectoryNames = { "jobs", "Controllers", "bin", "obj" };

    public TodoAppWebApplicationFactory()
    {
        var appProjectDir = FindAppProjectDirectory();
        TempContentRoot = Path.Combine(Path.GetTempPath(), "nyf-todo-test", Guid.NewGuid().ToString("N"));
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
        CopyDirectoryFiltered(sourceProjectDir, Path.Combine(TempContentRoot, "projects", ProjectName));
        Directory.CreateDirectory(Path.Combine(TempContentRoot, "projects", ProjectName, "database"));

        using (File.Create(TenantDbPath)) { }
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
                ["SystemDbPath"] = tempSystemDb,
                [$"Projects:{ProjectName}:Connection"] = $"Data Source={TenantDbPath};Pooling=False"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
            }).AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });

            services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddAuthenticationSchemes(TestAuthHandler.SchemeName, "ApiToken")
                    .Build();
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            TestProcessCwdGuard.RestoreSafeCwd();
            if (Directory.Exists(TempContentRoot))
            {
                Directory.Delete(TempContentRoot, recursive: true);
            }
        }
        catch { }
    }

    private static string FindAppProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "NetYamlForge");
            if (File.Exists(Path.Combine(candidate, "Program.cs")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find NetYamlForge app directory.");
    }

    private static void CopyDirectoryFiltered(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            if (ExcludedFileSuffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
                continue;
            File.Copy(file, Path.Combine(targetDir, name));
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var name = Path.GetFileName(subDir);
            if (ExcludedDirectoryNames.Any(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase)))
                continue;
            CopyDirectoryFiltered(subDir, Path.Combine(targetDir, name));
        }
    }
}

public class TodoAppIntegrationTests : IClassFixture<TodoAppWebApplicationFactory>
{
    private readonly TodoAppWebApplicationFactory _factory;

    public TodoAppIntegrationTests(TodoAppWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetTodoIndex_ReturnsOk()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/todo-app/DynamicEntity/Index?entity=todo");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Todo", content);
    }

    [Fact]
    public async Task GetTodoDashboard_ReturnsOk()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/todo-app/Dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetTodoHomePage_ReturnsOk()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/todo-app/Page/HomePage");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(content.Contains("待办") || content.Contains("Todo"), $"Actual content was: {content.Substring(0, Math.Min(content.Length, 300))}");
    }

    [Fact]
    public async Task GetTodoRoot_RedirectsToHomePage()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.GetAsync("/todo-app");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("HomePage", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task Api_CreateAndToggleComplete_Success()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin-api-token");

        // 1. Create todo
        var createPayload = new Dictionary<string, object?>
        {
            ["title"] = "E2E Test Task",
            ["description"] = "Testing E2E",
            ["priority"] = "high"
        };
        var createResp = await client.PostAsJsonAsync("/api/todo-app/todo", createPayload);
        var createBody = await createResp.Content.ReadAsStringAsync();
        Assert.True(createResp.IsSuccessStatusCode, $"Create failed: {createResp.StatusCode} - {createBody}");

        // 2. Query list
        var listResp = await client.GetAsync("/api/todo-app/todo");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var json = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("data", out var data) || json.TryGetProperty("items", out data) || json.ValueKind == JsonValueKind.Array);

        // 3. Toggle Complete Action
        var actionResp = await client.PostAsJsonAsync("/api/todo-app/todo/1/actions/toggle_complete", new Dictionary<string, object?>());
        var actionBody = await actionResp.Content.ReadAsStringAsync();
        Assert.True(actionResp.IsSuccessStatusCode, $"Action failed: {actionResp.StatusCode} - {actionBody}");
    }
}
