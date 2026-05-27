// ファイル概要：バッチジョブの実行エンジンです。SQL 実行、CSV 出力、フック呼び出しを担当します。

using System.Data;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Npgsql;
using NetYamlForge.Services.Connection;
using NetYamlForge.Services.Hooks;

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
/// </summary>
public class BatchJobExecutor : IBatchJobExecutor
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly HookExecutionService _hookExecutionService;
    private readonly IChinaStockService _chinaStockService;
    private readonly NetYamlForge.Services.Email.IEmailServiceFactory _emailFactory;
    private readonly IDocumentPdfService _docPdf;
    private readonly NetYamlForge.Services.Ai.IGeminiCliService _geminiCli;
    private readonly ILogger<BatchJobExecutor> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public BatchJobExecutor(
        IDbConnectionFactory dbConnectionFactory,
        HookExecutionService hookExecutionService,
        IChinaStockService chinaStockService,
        NetYamlForge.Services.Email.IEmailServiceFactory emailFactory,
        IDocumentPdfService docPdf,
        NetYamlForge.Services.Ai.IGeminiCliService geminiCli,
        ILogger<BatchJobExecutor> logger,
        ILoggerFactory loggerFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _hookExecutionService = hookExecutionService;
        _chinaStockService = chinaStockService;
        _emailFactory = emailFactory;
        _docPdf = docPdf;
        _geminiCli = geminiCli;
        _logger = logger;
        _loggerFactory = loggerFactory;
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

            using var db = _dbConnectionFactory.CreateConnection(projectName);
            db.Open();

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

            // ジョブタイプに応じて実行
            switch (job.Type.ToLowerInvariant())
            {
                case "sql_to_csv":
                    await ExecuteSqlToCsvAsync(job, db, tx, result, cancellationToken);
                    break;

                case "sql_command":
                    await ExecuteSqlCommandAsync(job, db, tx, result, cancellationToken);
                    break;

                case "stored_procedure":
                    await ExecuteStoredProcedureAsync(job, db, tx, result, cancellationToken);
                    break;

                case "china_stock_briefing":
                    await ExecuteChinaStockBriefingAsync(job, db, tx, result, cancellationToken);
                    break;

                case "email_fetch":
                    await ExecuteEmailFetchAsync(job, projectName, db, tx, result, cancellationToken);
                    break;

                /* Disabling AiEmailChatExecutor as requested by user
                case "ai_email_chat":
                    await ExecuteAiEmailChatAsync(job, projectName, db, tx, result, cancellationToken);
                    break;
                */

                case "invoice_email_processor":
                    await ExecuteInvoiceEmailProcessorAsync(job, projectName, db, tx, result, cancellationToken);
                    break;

                case "automated_blog_generator":
                    await ExecuteAutomatedBlogGeneratorAsync(job, projectName, db, tx, result, cancellationToken);
                    break;

                case "ai_dealer_engine":
                    await ExecuteAiDealerEngineAsync(job, projectName, db, tx, result, cancellationToken);
                    break;

                case "ai_communication_sender":
                    await ExecuteAiCommunicationSenderAsync(job, projectName, db, tx, result, cancellationToken);
                    break;

                default:
                    throw new NotSupportedException($"Unsupported job type: {job.Type}");
            }

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

    private async Task ExecuteSqlToCsvAsync(
        BatchJobDefinition job,
        IDbConnection db,
        IDbTransaction tx,
        BatchJobResult result,
        CancellationToken cancellationToken)
    {
        var sql = await GetSqlAsync(job);
        var outputFile = ResolveOutputPath(job.Settings.OutputFile);

        // ディレクトリが存在しない場合は作成
        var directory = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // クエリ実行
        using var reader = db.ExecuteReader(sql, transaction: tx);
        
        // CSV 書き込み
        using var writer = new StreamWriter(outputFile, false, Encoding.UTF8);
        var rowCount = 0;

        // ヘッダー
        if (job.Settings.IncludeHeader)
        {
            var headers = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                headers.Add(EscapeCsvValue(reader.GetName(i), job.Settings.Delimiter));
            }
            await writer.WriteLineAsync(string.Join(job.Settings.Delimiter.ToString(), headers));
        }

        // データ行
        var delimiterStr = job.Settings.Delimiter.ToString();
        while (reader.Read())
        {
            var values = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                values.Add(EscapeCsvValue(reader.GetValue(i)?.ToString() ?? "", job.Settings.Delimiter));
            }
            await writer.WriteLineAsync(string.Join(delimiterStr, values));
            rowCount++;
        }

        result.RowsAffected = rowCount;
        result.OutputFile = outputFile;

        _logger.LogInformation("SQL->CSV ジョブ完了：{JobId}, Rows: {Rows}, File: {File}", 
            job.Id, rowCount, outputFile);
    }

    private async Task ExecuteSqlCommandAsync(
        BatchJobDefinition job,
        IDbConnection db,
        IDbTransaction tx,
        BatchJobResult result,
        CancellationToken cancellationToken)
    {
        var sql = await GetSqlAsync(job);
        var rowsAffected = await db.ExecuteAsync(sql, transaction: tx);
        result.RowsAffected = rowsAffected;

        _logger.LogInformation("SQL コマンド完了：{JobId}, Rows: {Rows}", job.Id, rowsAffected);
    }

    private async Task ExecuteStoredProcedureAsync(
        BatchJobDefinition job,
        IDbConnection db,
        IDbTransaction tx,
        BatchJobResult result,
        CancellationToken cancellationToken)
    {
        // ストアドプロシージャ実行（実装は必要に応じて拡張）
        _logger.LogWarning("ストアドプロシージャは未実装です：{JobId}", job.Id);
        await Task.CompletedTask;
    }

    private async Task<string> GetSqlAsync(BatchJobDefinition job)
    {
        if (!string.IsNullOrEmpty(job.Settings.SqlQuery))
        {
            return job.Settings.SqlQuery;
        }

        if (!string.IsNullOrEmpty(job.Settings.SqlFile) && File.Exists(job.Settings.SqlFile))
        {
            return await File.ReadAllTextAsync(job.Settings.SqlFile);
        }

        throw new InvalidOperationException("SQL ファイルまたはクエリが指定されていません");
    }

    private string ResolveOutputPath(string? outputFile)
    {
        if (string.IsNullOrEmpty(outputFile))
        {
            return Path.Combine(Path.GetTempPath(), $"batch_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
        }

        // 日付プレースホルダーを置換
        return outputFile
            .Replace("{date:yyyyMMdd}", DateTime.UtcNow.ToString("yyyyMMdd"))
            .Replace("{date:yyyy-MM-dd}", DateTime.UtcNow.ToString("yyyy-MM-dd"))
            .Replace("{datetime:yyyyMMdd_HHmmss}", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
    }

    private static string EscapeCsvValue(string value, char delimiter)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        // 区切り文字、改行、ダブルクォートを含む場合はエスケープ
        if (value.Contains(delimiter) || value.Contains('\n') || value.Contains('\r') || value.Contains('"'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    /// <summary>
    /// 中国股市简报任务执行
    /// </summary>
    private async Task ExecuteChinaStockBriefingAsync(
        BatchJobDefinition job,
        IDbConnection db,
        IDbTransaction tx,
        BatchJobResult result,
        CancellationToken cancellationToken)
    {
        var logger = _loggerFactory.CreateLogger<ChinaStockBriefingExecutor>();
        var executor = new ChinaStockBriefingExecutor(_chinaStockService, logger);
        var briefResult = await executor.ExecuteAsync(job, db, tx, cancellationToken);

        result.Success = briefResult.Success;
        result.RowsAffected = briefResult.RowsAffected;
        result.ErrorMessage = briefResult.ErrorMessage;
        result.ErrorDetail = briefResult.ErrorDetail;
    }

    /// <summary>
    /// メール受信タスク実行
    /// </summary>
    private async Task ExecuteEmailFetchAsync(
        BatchJobDefinition job,
        string? projectName,
        IDbConnection db,
        IDbTransaction tx,
        BatchJobResult result,
        CancellationToken cancellationToken)
    {
        var logger = _loggerFactory.CreateLogger<EmailFetchExecutor>();
        var executor = new EmailFetchExecutor(_emailFactory.GetForProject(projectName), logger);
        var emailResult = await executor.ExecuteAsync(job, db, tx, cancellationToken);

        result.Success = emailResult.Success;
        result.RowsAffected = emailResult.RowsAffected;
        result.ErrorMessage = emailResult.ErrorMessage;
        result.ErrorDetail = emailResult.ErrorDetail;
    }

    /// <summary>
    /// AI Email Chat タスク実行
    /// </summary>
    private async Task ExecuteAiEmailChatAsync(
        BatchJobDefinition job,
        string? projectName,
        IDbConnection db,
        IDbTransaction tx,
        BatchJobResult result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(projectName))
        {
            throw new InvalidOperationException("projectName is required for ai_email_chat job.");
        }
        var logger = _loggerFactory.CreateLogger<AiEmailChatExecutor>();
        var executor = new AiEmailChatExecutor(_geminiCli, logger);
        var chatResult = await executor.ExecuteAsync(job, projectName, cancellationToken);

        result.Success = chatResult.Success;
        result.RowsAffected = chatResult.RowsAffected;
        result.ErrorMessage = chatResult.ErrorMessage;
        result.ErrorDetail = chatResult.ErrorDetail;
    }

    /// <summary>
    /// Invoice Email Processor タスク実行
    /// </summary>
    private async Task ExecuteInvoiceEmailProcessorAsync(
        BatchJobDefinition job,
        string? projectName,
        IDbConnection db,
        IDbTransaction tx,
        BatchJobResult result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(projectName))
            throw new InvalidOperationException("projectName is required for invoice_email_processor job.");

        var logger = _loggerFactory.CreateLogger<InvoiceEmailProcessorExecutor>();
        var executor = new InvoiceEmailProcessorExecutor(_docPdf, _geminiCli, logger);
        var execResult = await executor.ExecuteAsync(job, projectName, db, cancellationToken);

        result.Success = execResult.Success;
        result.RowsAffected = execResult.RowsAffected;
        result.ErrorMessage = execResult.ErrorMessage;
        result.ErrorDetail = execResult.ErrorDetail;
    }

    /// <summary>
    /// Automated Blog Generator タスク実行
    /// </summary>
    private async Task ExecuteAutomatedBlogGeneratorAsync(
        BatchJobDefinition job,
        string? projectName,
        IDbConnection db,
        IDbTransaction tx,
        BatchJobResult result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(projectName))
            throw new InvalidOperationException("projectName is required for automated_blog_generator job.");

        var logger = _loggerFactory.CreateLogger<AutomatedBlogGeneratorExecutor>();
        var executor = new AutomatedBlogGeneratorExecutor(_geminiCli, logger);
        var execResult = await executor.ExecuteAsync(job, projectName, db, cancellationToken);

        result.Success = execResult.Success;
        result.RowsAffected = execResult.RowsAffected;
        result.ErrorMessage = execResult.ErrorMessage;
        result.ErrorDetail = execResult.ErrorDetail;
    }

    /// <summary>
    /// AI ディーラーエンジンタスク実行
    /// </summary>
    private async Task ExecuteAiDealerEngineAsync(
        BatchJobDefinition job,
        string? projectName,
        IDbConnection db,
        IDbTransaction tx,
        BatchJobResult result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(projectName))
            throw new InvalidOperationException("projectName is required for ai_dealer_engine job.");

        var logger = _loggerFactory.CreateLogger<AiDealerEngineExecutor>();
        var executor = new AiDealerEngineExecutor(_geminiCli, logger);
        var execResult = await executor.ExecuteAsync(job, projectName, db, tx, cancellationToken);

        result.Success = execResult.Success;
        result.RowsAffected = execResult.RowsAffected;
        result.ErrorMessage = execResult.ErrorMessage;
        result.ErrorDetail = execResult.ErrorDetail;
    }

    /// <summary>
    /// AI コミュニケーション送信タスク実行
    /// </summary>
    private async Task ExecuteAiCommunicationSenderAsync(
        BatchJobDefinition job,
        string? projectName,
        IDbConnection db,
        IDbTransaction tx,
        BatchJobResult result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(projectName))
            throw new InvalidOperationException("projectName is required for ai_communication_sender job.");

        var logger = _loggerFactory.CreateLogger<AiCommunicationExecutor>();
        var executor = new AiCommunicationExecutor(_geminiCli, _emailFactory, logger);
        var execResult = await executor.ExecuteAsync(job, projectName, db, tx, cancellationToken);

        result.Success = execResult.Success;
        result.RowsAffected = execResult.RowsAffected;
        result.ErrorMessage = execResult.ErrorMessage;
        result.ErrorDetail = execResult.ErrorDetail;
    }

    /// <summary>
    /// エラー通知メールを送信
    /// </summary>
    private async Task SendErrorNotificationAsync(BatchJobDefinition job, string? projectName, Exception ex)
    {
        try
        {
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
}
