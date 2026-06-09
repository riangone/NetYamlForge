// ファイル概要：バッチジョブの実行エンジンです。IBatchStepHandler を介してジョブタイプを委譲します。

using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Npgsql;
using NetYamlForge.Services.Connection;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// バッチジョブ実行エンジン
/// </summary>
public interface IBatchJobExecutor
{
    /// <summary>
    /// ジョブを実行する
    /// </summary>
    Task<BatchJobResult> ExecuteAsync(BatchJobDefinition job, string? projectName, CancellationToken cancellationToken = default);
}

/// <summary>
/// バッチジョブ実行エンジンの実装
/// IBatchStepHandler コレクションを DI で受け取り、job.Type でルーティングします。
/// 新しいジョブタイプを追加する際は IBatchStepHandler を実装して DI 登録するだけで OK です。
/// </summary>
public class BatchJobExecutor : IBatchJobExecutor
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly HookExecutionService _hookExecutionService;
    private readonly ILogger<BatchJobExecutor> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly NetYamlForge.Services.Email.IEmailServiceFactory? _emailFactory;

    private static readonly IReadOnlyDictionary<string, Type> _handlerTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
    {
        { "sql_to_csv", typeof(SqlToCsvHandler) },
        { "sql_command", typeof(SqlCommandHandler) },
        { "stored_procedure", typeof(StoredProcedureHandler) },
        { "china_stock_briefing", typeof(ChinaStockBriefingExecutor) },
        { "email_fetch", typeof(EmailFetchExecutor) },
        { "invoice_email_processor", typeof(InvoiceEmailProcessorExecutor) },
        { "automated_blog_generator", typeof(AutomatedBlogGeneratorExecutor) },
        { "ai_dealer_engine", typeof(AiDealerEngineExecutor) },
        { "ai_communication_sender", typeof(AiCommunicationExecutor) },
        { "ai_folder_processor", typeof(AiFolderProcessorExecutor) }
    };

    public BatchJobExecutor(
        IDbConnectionFactory dbConnectionFactory,
        HookExecutionService hookExecutionService,
        IServiceProvider serviceProvider,
        ILogger<BatchJobExecutor> logger,
        NetYamlForge.Services.Email.IEmailServiceFactory? emailFactory = null)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _hookExecutionService = hookExecutionService;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _emailFactory = emailFactory;
    }

    public async Task<BatchJobResult> ExecuteAsync(BatchJobDefinition job, string? projectName, CancellationToken cancellationToken = default)
    {
        var result = new BatchJobResult
        {
            JobId = job.Id,
            StartedAt = DateTime.UtcNow
        };

        try
        {
            // 前処理フックを実行
            var hookContext = new EntityHookContext
            {
                Entity = "BatchJob",
                Operation = CrudOperation.Create,
                Data = new Dictionary<string, object?> { { "JobId", job.Id } }
            };

            using var db = await _dbConnectionFactory.CreateConnectionAsync(projectName, cancellationToken);

            using var tx = db.BeginTransaction();

            // Before フック
            if (job.BeforeRun != null && job.BeforeRun.Count > 0)
            {
                var hookResult = await _hookExecutionService.RunBeforeAsync(
                    job.BeforeRun, hookContext, projectName, db, tx);

                if (hookResult.Cancel)
                {
                    _logger.LogWarning("ジョブがフックによってキャンセルされました：{JobId}, Reason: {Reason}", 
                        job.Id, hookResult.CancelMessage);
                    result.Success = false;
                    result.ErrorMessage = hookResult.CancelMessage ?? "Hook cancelled";
                    result.EndedAt = DateTime.UtcNow;
                    return result;
                }
            }

            // IBatchStepHandler 延迟路由解析
            if (!_handlerTypes.TryGetValue(job.Type, out var handlerType))
                throw new NotSupportedException($"Unsupported job type: {job.Type}. Registered: {string.Join(", ", _handlerTypes.Keys)}");

            var handler = (IBatchStepHandler)_serviceProvider.GetRequiredService(handlerType);

            await handler.ExecuteAsync(job, projectName, db, tx, result, cancellationToken);

            // After フック
            if (job.AfterRun != null && job.AfterRun.Count > 0)
            {
                await _hookExecutionService.RunAfterAsync(
                    job.AfterRun, hookContext, projectName, db, tx);
            }

            tx.Commit();
            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ジョブ実行中にエラーが発生しました：{JobId}", job.Id);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ErrorDetail = ex.ToString();

            // エラー通知
            if (!string.IsNullOrEmpty(job.Settings.NotifyEmails))
            {
                await SendErrorNotificationAsync(job, projectName, ex);
            }
        }
        finally
        {
            result.EndedAt = DateTime.UtcNow;
        }

        return result;
    }

    /// <summary>
    /// エラー通知メールを送信
    /// </summary>
    private async Task SendErrorNotificationAsync(BatchJobDefinition job, string? projectName, Exception ex)
    {
        try
        {
            if (_emailFactory == null) return;
            var emailService = _emailFactory.GetForProject(projectName);
            var emails = job.Settings.NotifyEmails!.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var email in emails)
            {
                await emailService.SendEmailAsync(new NetYamlForge.Models.Email.EmailMessage
                {
                    To = email.Trim(),
                    Subject = $"[NetYamlForge] Batch Job Error: {job.DisplayName} ({job.Id})",
                    Body = $@"
<h2>Batch Job Execution Error</h2>
<p><b>Job:</b> {job.DisplayName} ({job.Id})</p>
<p><b>Time:</b> {DateTime.UtcNow} (UTC)</p>
<p><b>Error:</b> {ex.Message}</p>
<pre>{ex.StackTrace}</pre>
",
                    IsHtml = true
                });
            }
        }
        catch (Exception notifyEx)
        {
            _logger.LogError(notifyEx, "Failed to send error notification for job {JobId}", job.Id);
        }
    }
}

/// <summary>
/// DB コネクションファクトリ
/// </summary>
public interface IDbConnectionFactory
{
    IDbConnection CreateConnection(string? projectName);
    Task<IDbConnection> CreateConnectionAsync(string? projectName, CancellationToken cancellationToken = default);
}

/// <summary>
/// DB コネクションファクトリ実装 - 使用连接池
/// </summary>
public class DbConnectionFactory : IDbConnectionFactory
{
    private readonly IConnectionManager _connectionManager;
    private readonly ProjectManager _projectManager;

    public DbConnectionFactory(
        IConnectionManager connectionManager,
        ProjectManager projectManager)
    {
        _connectionManager = connectionManager;
        _projectManager = projectManager;
    }

    public IDbConnection CreateConnection(string? projectName)
    {
        if (string.IsNullOrEmpty(projectName))
        {
            throw new InvalidOperationException("Project name is required");
        }

        if (!_projectManager.TryGet(projectName, out var project) || project == null)
        {
            throw new InvalidOperationException($"Project not found: {projectName}");
        }

        // 使用连接管理器获取连接（从池中复用）
        var connection = _connectionManager.GetConnectionAsync(projectName).GetAwaiter().GetResult();
        return connection;
    }

    public async Task<IDbConnection> CreateConnectionAsync(string? projectName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(projectName))
        {
            throw new InvalidOperationException("Project name is required");
        }

        if (!_projectManager.TryGet(projectName, out var project) || project == null)
        {
            throw new InvalidOperationException($"Project not found: {projectName}");
        }

        // 使用连接管理器异步获取连接（从池中复用）
        var connection = await _connectionManager.GetConnectionAsync(projectName, cancellationToken);
        return connection;
    }
}
