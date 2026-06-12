using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace NetYamlForge.Tests.Integration;

public sealed class ApiTestWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ProjectName = "blog";
    public string TempContentRoot { get; }
    public string TenantDbPath =>
        Path.Combine(TempContentRoot, "projects", ProjectName, "database", $"{ProjectName}.db");

    private static readonly string[] ExcludedFileSuffixes = { ".db", ".db-wal", ".db-shm", ".env" };
    private static readonly string[] ExcludedDirectoryNames = { "jobs", "Controllers", "bin", "obj" };

    public ApiTestWebApplicationFactory()
    {
        var appProjectDir = FindAppProjectDirectory();
        TempContentRoot = Path.Combine(Path.GetTempPath(), "nyf-api-test", Guid.NewGuid().ToString("N"));
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
                ["SystemDbPath"] = tempSystemDb
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
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
            if (File.Exists(Path.Combine(candidate, "Program.cs")) &&
                Directory.Exists(Path.Combine(candidate, "projects", ProjectName)))
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
