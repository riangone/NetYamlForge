using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using NetYamlForge.Data.Schemas;
using NetYamlForge.Models;
using NetYamlForge.Services.Connection;

namespace NetYamlForge.Services.DynamicEntity;

public sealed class EntitySchemaWatcherService : BackgroundService
{
    private const string DriftTableSql = """
CREATE TABLE IF NOT EXISTS _nyf_schema_drift (
    id TEXT PRIMARY KEY,
    project_name TEXT NOT NULL,
    entity_name TEXT NOT NULL,
    detected_at TEXT NOT NULL,
    operations_json TEXT NOT NULL
)
""";

    private readonly ProjectManager _projectManager;
    private readonly IConnectionManager _connectionManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<EntitySchemaWatcherService> _logger;

    public EntitySchemaWatcherService(
        ProjectManager projectManager,
        IConnectionManager connectionManager,
        ILoggerFactory loggerFactory,
        ILogger<EntitySchemaWatcherService> logger)
    {
        _projectManager = projectManager;
        _connectionManager = connectionManager;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        if (stoppingToken.IsCancellationRequested) return;

        _logger.LogInformation("[SCHEMA WATCHER] Starting startup schema drift detection");

        var systemDbPath = SystemDatabaseInitializer.DbPath;
        IDbConnection? systemConn = null;
        try
        {
#pragma warning disable DCS003
            var systemConnStr = new SqliteConnectionStringBuilder { DataSource = systemDbPath }.ConnectionString;
            systemConn = new SqliteConnection(systemConnStr);
#pragma warning restore DCS003
            systemConn.Open();
            await systemConn.ExecuteAsync(DriftTableSql);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SCHEMA WATCHER] Could not open system.db for drift recording; continuing without persistence");
            systemConn = null;
        }

        var migrationService = new DynamicEntitySchemaMigrationService(
            _loggerFactory.CreateLogger<DynamicEntitySchemaMigrationService>());

        foreach (var project in _projectManager.GetAll())
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await DetectDriftForProjectAsync(project, migrationService, systemConn, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SCHEMA WATCHER] Error detecting drift for project={Project}; skipping", project.Name);
            }
        }

        systemConn?.Dispose();
        _logger.LogInformation("[SCHEMA WATCHER] Startup schema drift detection complete");
    }

    private async Task DetectDriftForProjectAsync(
        ProjectInfo project,
        DynamicEntitySchemaMigrationService migrationService,
        IDbConnection? systemConn,
        CancellationToken stoppingToken)
    {
        IDbConnection? projectConn = null;
        try
        {
            projectConn = await _connectionManager.GetConnectionAsync(project.Name, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SCHEMA WATCHER] Cannot connect to project={Project}; skipping drift detection", project.Name);
            return;
        }

        try
        {
            foreach (var (entityName, entity) in project.EntityMetadata.GetAll())
            {
                if (stoppingToken.IsCancellationRequested) break;

                var apiMode = (entity.Api ?? "disabled").ToLowerInvariant();
                if (apiMode == "disabled") continue;

                try
                {
                    var physical = await migrationService.GetPhysicalColumnsAsync(projectConn, entity.Table, project.DatabaseType);
                    var plan = migrationService.BuildPlan(entityName, entity, physical, project.DatabaseType);

                    if (plan.Operations.Count == 0) continue;

                    _logger.LogWarning(
                        "[SCHEMA DRIFT] project={Project} entity={Entity} operations={Count}",
                        project.Name, entityName, plan.Operations.Count);

                    if (systemConn != null)
                    {
                        await WriteDriftRecordAsync(systemConn, project.Name, entityName, plan.Operations);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[SCHEMA WATCHER] Error checking drift for project={Project} entity={Entity}",
                        project.Name, entityName);
                }
            }
        }
        finally
        {
            _connectionManager.ReleaseConnection(project.Name, projectConn);
        }
    }

    private static async Task WriteDriftRecordAsync(
        IDbConnection systemConn,
        string projectName,
        string entityName,
        IReadOnlyList<MigrationOperation> operations)
    {
        var id = $"{projectName}_{entityName}_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        var detectedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var operationsJson = JsonSerializer.Serialize(operations.Select(o => new
        {
            opType = o.OpType.ToString(),
            columnName = o.ColumnName,
            oldType = o.OldSqlType,
            newType = o.NewSqlType
        }));

        await systemConn.ExecuteAsync(
            """
INSERT INTO _nyf_schema_drift (id, project_name, entity_name, detected_at, operations_json)
VALUES (@Id, @ProjectName, @EntityName, @DetectedAt, @OperationsJson)
""",
            new
            {
                Id = id,
                ProjectName = projectName,
                EntityName = entityName,
                DetectedAt = detectedAt,
                OperationsJson = operationsJson
            });
    }
}
