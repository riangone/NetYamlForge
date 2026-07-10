// システム設定画面（SystemSettingsController）の中核となる AppSettingsWriter の検証。
// appsettings.json の一部セクションだけを書き換え、他セクションを壊さないこと、
// ビューモデル ⇔ CliChainOptions の相互変換が往復して一致することを確認する。

using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NetYamlForge.Models.Config;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.Config;
using Xunit;

namespace NetYamlForge.Tests;

public class AppSettingsWriterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _appSettingsPath;

    public AppSettingsWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "nyf-appsettings-writer-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _appSettingsPath = Path.Combine(_tempDir, "appsettings.json");
        File.WriteAllText(_appSettingsPath, """
        {
          "OtherSection": { "Foo": "bar" },
          "AiCliChain": { "TimeoutSeconds": 30, "DefaultOrder": [ "claude" ], "Providers": {} }
        }
        """);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
    }

    private class FakeEnv : IWebHostEnvironment
    {
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "test";
        public string EnvironmentName { get; set; } = "Test";
        public string WebRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public async Task UpdateSectionAsync_ReplacesTargetSection_KeepsOtherSectionsIntact()
    {
        var writer = new AppSettingsWriter(new FakeEnv { ContentRootPath = _tempDir }, NullLogger<AppSettingsWriter>.Instance);

        var updated = new CliChainOptions
        {
            TimeoutSeconds = 120,
            DefaultOrder = ["opencode", "claude"],
            Providers = new Dictionary<string, CliProviderOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["opencode"] = new CliProviderOptions { Command = "opencode", ArgsTemplate = "run \"{prompt}\"", SupportsVariant = true },
                ["claude"] = new CliProviderOptions { Command = "claude", ArgsTemplate = "-p \"{prompt}\"" }
            }
        };

        await writer.UpdateSectionAsync(CliChainOptions.SectionName, updated);

        var savedJson = await File.ReadAllTextAsync(_appSettingsPath);
        using var doc = JsonDocument.Parse(savedJson);

        // 他セクションはそのまま残っていること
        Assert.Equal("bar", doc.RootElement.GetProperty("OtherSection").GetProperty("Foo").GetString());

        // 対象セクションは新しい内容で置き換わっていること
        var cli = doc.RootElement.GetProperty("AiCliChain");
        Assert.Equal(120, cli.GetProperty("TimeoutSeconds").GetInt32());
        Assert.Equal("opencode", cli.GetProperty("DefaultOrder")[0].GetString());
        Assert.True(cli.GetProperty("Providers").TryGetProperty("opencode", out _));

        // バックアップファイルが作成されていること
        Assert.True(File.Exists(_appSettingsPath + ".bak"));
    }

    [Fact]
    public void ViewModel_RoundTrip_PreservesProviderData()
    {
        var original = new CliChainOptions
        {
            TimeoutSeconds = 45,
            DefaultOrder = ["antigravity", "opencode"],
            Providers = new Dictionary<string, CliProviderOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["antigravity"] = new CliProviderOptions { Command = "antigravity", ArgsTemplate = "-p \"{prompt}\"{model}", PreferredForImages = true },
                ["opencode"] = new CliProviderOptions { Command = "opencode", ArgsTemplate = "run \"{prompt}\"{variant}", SupportsVariant = true }
            }
        };

        var vm = AiCliChainSettingsViewModel.FromOptions(original);
        var roundTripped = vm.ToOptions();

        Assert.Equal(original.TimeoutSeconds, roundTripped.TimeoutSeconds);
        Assert.Equal(original.DefaultOrder, roundTripped.DefaultOrder);
        Assert.Equal(original.Providers.Count, roundTripped.Providers.Count);
        Assert.True(roundTripped.Providers["antigravity"].PreferredForImages);
        Assert.True(roundTripped.Providers["opencode"].SupportsVariant);
    }

    [Fact]
    public void ViewModel_ToOptions_SkipsBlankRows()
    {
        var vm = new AiCliChainSettingsViewModel
        {
            TimeoutSeconds = 90,
            DefaultOrderCsv = "claude",
            Providers =
            [
                new CliProviderRow { Name = "claude", Command = "claude" },
                new CliProviderRow { Name = "", Command = "" } // JS で削除したはずが送信された空行を想定
            ]
        };

        var options = vm.ToOptions();

        Assert.Single(options.Providers);
        Assert.True(options.Providers.ContainsKey("claude"));
    }
}
