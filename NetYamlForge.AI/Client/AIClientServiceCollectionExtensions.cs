// AI サービス HTTP API クライアント DI 登録拡張

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using NetYamlForge.AI.Client;

namespace NetYamlForge.AI;

/// <summary>
/// AI HTTP クライアント DI 登録拡張
/// </summary>
public static class AIClientServiceCollectionExtensions
{
    /// <summary>
    /// AI サービス HTTP API クライアントを登録
    /// 中期：独立プロセス版 AI サービスとの通信用
    /// </summary>
    public static IServiceCollection AddAIServiceClient(this IServiceCollection services, IConfiguration configuration)
    {
        var aiServiceBaseUrl = configuration["AI:ServiceBaseUrl"] ?? "http://localhost:5200";

        services.AddHttpClient<AIServiceClient>(client =>
        {
            client.BaseAddress = new Uri(aiServiceBaseUrl);
            client.Timeout = TimeSpan.FromMinutes(30); // AI 処理は長時間になる場合がある
        });

        services.AddSingleton<AIServiceClient>(sp => sp.GetRequiredService<AIServiceClient>());

        return services;
    }
}
