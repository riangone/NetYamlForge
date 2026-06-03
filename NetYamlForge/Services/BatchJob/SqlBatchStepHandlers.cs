using System.Data;
using System.Text;
using Dapper;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// SQL クエリ結果を CSV ファイルに出力するハンドラー
/// </summary>
public class SqlToCsvHandler : IBatchStepHandler
{
    public string StepType => "sql_to_csv";
    private readonly ILogger<SqlToCsvHandler> _logger;

    public SqlToCsvHandler(ILogger<SqlToCsvHandler> logger) => _logger = logger;

    public async Task ExecuteAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx,
        BatchJobResult result, CancellationToken ct)
    {
        var sql = await GetSqlAsync(job);
        var outputFile = ResolveOutputPath(job.Settings.OutputFile);

        var directory = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        using var reader = db.ExecuteReader(sql, transaction: tx);
        using var writer = new StreamWriter(outputFile, false, Encoding.UTF8);
        var rowCount = 0;

        if (job.Settings.IncludeHeader)
        {
            var headers = Enumerable.Range(0, reader.FieldCount)
                .Select(i => EscapeCsvValue(reader.GetName(i), job.Settings.Delimiter));
            await writer.WriteLineAsync(string.Join(job.Settings.Delimiter, headers));
        }

        var delimiterStr = job.Settings.Delimiter.ToString();
        while (reader.Read())
        {
            var values = Enumerable.Range(0, reader.FieldCount)
                .Select(i => EscapeCsvValue(reader.GetValue(i)?.ToString() ?? "", job.Settings.Delimiter));
            await writer.WriteLineAsync(string.Join(delimiterStr, values));
            rowCount++;
        }

        result.RowsAffected = rowCount;
        result.OutputFile = outputFile;
        _logger.LogInformation("SQL->CSV 完了: {JobId}, Rows={Rows}, File={File}", job.Id, rowCount, outputFile);
    }

    private static async Task<string> GetSqlAsync(BatchJobDefinition job)
    {
        if (!string.IsNullOrEmpty(job.Settings.SqlQuery)) return job.Settings.SqlQuery;
        if (!string.IsNullOrEmpty(job.Settings.SqlFile) && File.Exists(job.Settings.SqlFile))
            return await File.ReadAllTextAsync(job.Settings.SqlFile);
        throw new InvalidOperationException("SQL ファイルまたはクエリが指定されていません");
    }

    private static string ResolveOutputPath(string? outputFile)
    {
        if (string.IsNullOrEmpty(outputFile))
            return Path.Combine(Path.GetTempPath(), $"batch_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");

        return outputFile
            .Replace("{date:yyyyMMdd}", DateTime.UtcNow.ToString("yyyyMMdd"))
            .Replace("{date:yyyy-MM-dd}", DateTime.UtcNow.ToString("yyyy-MM-dd"))
            .Replace("{datetime:yyyyMMdd_HHmmss}", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
    }

    private static string EscapeCsvValue(string value, char delimiter)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.Contains(delimiter) || value.Contains('\n') || value.Contains('\r') || value.Contains('"'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}

/// <summary>
/// SQL コマンド（INSERT/UPDATE/DELETE）を実行するハンドラー
/// </summary>
public class SqlCommandHandler : IBatchStepHandler
{
    public string StepType => "sql_command";
    private readonly ILogger<SqlCommandHandler> _logger;

    public SqlCommandHandler(ILogger<SqlCommandHandler> logger) => _logger = logger;

    public async Task ExecuteAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx,
        BatchJobResult result, CancellationToken ct)
    {
        var sql = !string.IsNullOrEmpty(job.Settings.SqlQuery)
            ? job.Settings.SqlQuery
            : await File.ReadAllTextAsync(job.Settings.SqlFile!);
        result.RowsAffected = await db.ExecuteAsync(sql, transaction: tx);
        _logger.LogInformation("SQL コマンド完了: {JobId}, Rows={Rows}", job.Id, result.RowsAffected);
    }
}

/// <summary>
/// ストアドプロシージャ実行ハンドラー
/// </summary>
public class StoredProcedureHandler : IBatchStepHandler
{
    public string StepType => "stored_procedure";
    private readonly ILogger<StoredProcedureHandler> _logger;

    public StoredProcedureHandler(ILogger<StoredProcedureHandler> logger) => _logger = logger;

    public Task ExecuteAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx,
        BatchJobResult result, CancellationToken ct)
    {
        _logger.LogWarning("ストアドプロシージャは未実装です: {JobId}", job.Id);
        return Task.CompletedTask;
    }
}
