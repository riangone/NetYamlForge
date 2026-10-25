using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NetYamlForge.Tests.Integration;

public sealed class AgentStudioWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ProjectName = "agent-studio";
    public string TempContentRoot { get; }
    public string TenantDbPath =>
        Path.Combine(TempContentRoot, "projects", ProjectName, "database", $"{ProjectName}.db");

    private static readonly string[] ExcludedFileSuffixes = { ".db-wal", ".db-shm", ".env" };
    private static readonly string[] ExcludedDirectoryNames = { "jobs", "Controllers", "bin", "obj" };

    public AgentStudioWebApplicationFactory()
    {
        var appProjectDir = FindAppProjectDirectory();
        TempContentRoot = Path.Combine(Path.GetTempPath(), "nyf-agentstudio-test", Guid.NewGuid().ToString("N"));
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

        var projectsDir = Path.Combine(appProjectDir, "projects");
        if (Directory.Exists(projectsDir))
        {
            foreach (var projPath in Directory.GetDirectories(projectsDir))
            {
                var proj = Path.GetFileName(projPath);
                var dest = Path.Combine(TempContentRoot, "projects", proj);
                if (!Directory.Exists(dest))
                {
                    CopyDirectoryFiltered(projPath, dest);
                    var dbSrc = Path.Combine(projPath, "database", $"{proj}.db");
                    var dbDest = Path.Combine(dest, "database", $"{proj}.db");
                    if (File.Exists(dbSrc))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dbDest)!);
                        File.Copy(dbSrc, dbDest, overwrite: true);
                    }
                }
            }
        }

        Directory.CreateDirectory(Path.Combine(TempContentRoot, "projects", ProjectName, "database"));
        if (!File.Exists(TenantDbPath))
        {
            using (File.Create(TenantDbPath)) { }
        }
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
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "NetYamlForge");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "NetYamlForge.csproj")))
            {
                return candidate;
            }
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate NetYamlForge app project directory.");
    }

    private static void CopyDirectoryFiltered(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            if (ExcludedFileSuffixes.Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            File.Copy(file, Path.Combine(targetDir, fileName), overwrite: true);
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(subDir);
            if (ExcludedDirectoryNames.Contains(dirName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            CopyDirectoryFiltered(subDir, Path.Combine(targetDir, dirName));
        }
    }
}

public class AgentStudioIntegrationTests : IClassFixture<AgentStudioWebApplicationFactory>
{
    private readonly AgentStudioWebApplicationFactory _factory;

    public AgentStudioIntegrationTests(AgentStudioWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_HomePage_ReturnsSuccessAndRendersStudioTitleAndElements()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/agent-studio/Page/HomePage");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.True(content.Contains("Agent Studio") && content.Contains("演进工作台"), 
            $"Actual content was: {content[..Math.Min(content.Length, 300)]}");
    }

    [Fact]
    public async Task Get_HomePage_RendersTaskPipelineCardsAndArtifactModal()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/agent-studio/Page/HomePage");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Stage 1: 架构设计", content);
        Assert.Contains("Stage 2: 开发", content);
        Assert.Contains("Stage 3: 测试", content);
        Assert.Contains("artifactInspectorModal", content);
    }

    [Fact]
    public async Task Get_AgentTaskCrudIndex_ReturnsSuccess()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/agent-studio/DynamicEntity/Index?entity=agent_task");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.True(content.Contains("agent_task") || content.Contains("任务") || content.Contains("Agent Task"), 
            $"Actual content was: {content[..Math.Min(content.Length, 300)]}");
    }

    [Fact]
    public async Task Get_AgentTaskLogCrudIndex_ReturnsSuccess()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/agent-studio/DynamicEntity/Index?entity=agent_task_log");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.True(content.Contains("agent_task_log") || content.Contains("日志") || content.Contains("Agent Task Log"), 
            $"Actual content was: {content[..Math.Min(content.Length, 300)]}");
    }
}
