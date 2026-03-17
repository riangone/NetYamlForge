// ファイル概要: dashboard.yml を読み込み、DashboardConfig を提供するサービスです。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using NetYamlForge.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services;

public interface IDashboardConfigProvider
{
    DashboardConfig GetConfig();
}

public class DashboardConfigProvider : IDashboardConfigProvider
{
    private readonly DashboardConfig _config;

    /// <summary>
    /// ASP.NET Core DI から呼ばれる既存コンストラクタ（後方互換）。
    /// config/dashboard.yml を読み込みます。
    /// </summary>
    public DashboardConfigProvider(IWebHostEnvironment env)
    {
        var filePath = Path.Combine(env.ContentRootPath, "config", "dashboard.yml");
        _config = Load(filePath);
    }

    /// <summary>
    /// ProjectManager から呼ばれるコンストラクタ。
    /// projectDir/dashboard.yml を読み込みます。
    /// </summary>
    public DashboardConfigProvider(string projectDir)
    {
        var filePath = Path.Combine(projectDir, "dashboard.yml");
        _config = Load(filePath);
    }

    private static DashboardConfig Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new DashboardConfig();
        }

        var yaml = File.ReadAllText(filePath);
        YamlSchemaValidator.ValidateDashboardYaml(yaml, filePath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<DashboardConfig>(yaml) ?? new DashboardConfig();
    }

    public DashboardConfig GetConfig() => _config;
}
