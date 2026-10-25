using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
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

public sealed class BlogAppWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ProjectName = "blog-app";
    public string TempContentRoot { get; }
    public string TenantDbPath =>
        Path.Combine(TempContentRoot, "projects", ProjectName, "database", $"{ProjectName}.db");

    private static readonly string[] ExcludedFileSuffixes = { ".db", ".db-wal", ".db-shm", ".env" };
    private static readonly string[] ExcludedDirectoryNames = { "jobs", "Controllers", "bin", "obj" };

    public BlogAppWebApplicationFactory()
    {
        var appProjectDir = FindAppProjectDirectory();
        TempContentRoot = Path.Combine(Path.GetTempPath(), "nyf-blog-test", Guid.NewGuid().ToString("N"));
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

        // 复制初始测试数据库
        var sourceDb = Path.Combine(sourceProjectDir, "database", $"{ProjectName}.db");
        if (File.Exists(sourceDb))
        {
            File.Copy(sourceDb, TenantDbPath, overwrite: true);
        }
        else
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

public class BlogAppIntegrationTests : IClassFixture<BlogAppWebApplicationFactory>
{
    private readonly BlogAppWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BlogAppIntegrationTests(BlogAppWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Get_HomePage_ReturnsSuccessAndRendersPortalContent()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/blog-app/Page/HomePage");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.True(content.Contains("博客") || content.Contains("Blog") || content.Contains("探索"), $"Actual content was: {content[..Math.Min(content.Length, 300)]}");
    }

    [Fact]
    public async Task Get_PostDetailPage_ReturnsSuccessAndRendersArticle()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/blog-app/Page/PostDetail?id=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.True(content.Contains("NetYamlForge") || content.Contains("文章") || content.Contains("返回博客一览"), $"Actual content was: {content[..Math.Min(content.Length, 300)]}");
    }

    [Fact]
    public async Task Get_BlogPostCrudIndex_ReturnsSuccess()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/blog-app/DynamicEntity/Index?entity=blog_post");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.True(content.Contains("文章") || content.Contains("blog_post"), $"Actual content was: {content[..Math.Min(content.Length, 300)]}");
    }

    [Fact]
    public async Task Get_BlogCategoryCrudIndex_ReturnsSuccess()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/blog-app/DynamicEntity/Index?entity=blog_category");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.True(content.Contains("分类") || content.Contains("blog_category"), $"Actual content was: {content[..Math.Min(content.Length, 300)]}");
    }

    [Fact]
    public async Task Api_CreateAndTogglePublish_Success()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "admin-api-token");

        // 1. Create blog post via API
        var payload = new Dictionary<string, object?>
        {
            ["title"] = "测试新文章",
            ["slug"] = "test-new-post",
            ["status"] = "draft",
            ["content"] = "自动化测试正文内容",
            ["category_id"] = 1,
            ["author"] = "Admin"
        };
        var createResp = await client.PostAsJsonAsync("/api/blog-app/blog_post", payload);
        var createBody = await createResp.Content.ReadAsStringAsync();
        Assert.True(createResp.IsSuccessStatusCode, $"Create failed: {createResp.StatusCode} - {createBody}");

        // 2. Query list
        var listResp = await client.GetAsync("/api/blog-app/blog_post");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);

        // 3. Toggle Publish Action via API
        var actionResp = await client.PostAsJsonAsync("/api/blog-app/blog_post/1/actions/toggle_publish", new Dictionary<string, object?>());
        var actionBody = await actionResp.Content.ReadAsStringAsync();
        Assert.True(actionResp.IsSuccessStatusCode, $"Action failed: {actionResp.StatusCode} - {actionBody}");
    }
}
