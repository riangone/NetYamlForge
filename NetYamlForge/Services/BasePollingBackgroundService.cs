using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services;

/// <summary>
/// 作用域ライフサイクル、エラーハンドリング、および周期的なポーリング処理を提供するバックグラウンドサービスの共通基底クラスです。
/// </summary>
public abstract class BasePollingBackgroundService : BackgroundService
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly ILogger Logger;
    protected readonly TimeSpan PollInterval;

    protected BasePollingBackgroundService(IServiceProvider serviceProvider, ILogger logger, TimeSpan pollInterval)
    {
        ServiceProvider = serviceProvider;
        Logger = logger;
        PollInterval = pollInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation("{ServiceName} Background Service is starting.", GetType().Name);

        try
        {
            await OnStartupAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to execute startup initialization in {ServiceName}.", GetType().Name);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = ServiceProvider.CreateScope();
                await PollAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error occurred during polling cycle in {ServiceName}.", GetType().Name);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown sequence
            }
        }

        Logger.LogInformation("{ServiceName} Background Service is stopping.", GetType().Name);
    }

    /// <summary>
    /// サービス開始時に一度だけ実行される初期化処理。
    /// </summary>
    protected virtual Task OnStartupAsync(CancellationToken stoppingToken) => Task.CompletedTask;

    /// <summary>
    /// 各ポーリングサイクルで実行される処理。
    /// </summary>
    protected abstract Task PollAsync(IServiceProvider serviceProvider, CancellationToken stoppingToken);
}
