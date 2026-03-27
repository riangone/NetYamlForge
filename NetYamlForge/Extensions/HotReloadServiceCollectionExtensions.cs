// ファイル概要：YAML ホットリロードサービスの DI 登録拡張メソッド
using NetYamlForge.Services.HotReload;

namespace Microsoft.Extensions.DependencyInjection;

public static class HotReloadServiceCollectionExtensions
{
    public static IServiceCollection AddYamlHotReload(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HotReloadOptions>(configuration.GetSection(HotReloadOptions.SectionName));
        services.AddSingleton<IYamlFileWatcher, YamlFileWatcher>();
        services.AddSingleton<ProjectYamlCacheManager>();
        services.AddHostedService<YamlHotReloadService>();
        return services;
    }
}
