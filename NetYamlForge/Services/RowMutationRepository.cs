#pragma warning disable DCS001

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NetYamlForge.Models;
using NetYamlForge.Services.Tenant;
using NetYamlForge.Services.Workflow;
using NetYamlForge.Services.Webhook;
using NetYamlForge.Services.Auth;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services;

public sealed class RowMutationRepository : IRowMutationRepository
{
    private readonly IDbConnection _db;
    private readonly ILogger<RowMutationRepository> _logger;
    private readonly ProjectScope _projectScope;
    private readonly IEntityMetadataProvider _metadataProvider;
    private readonly TenantContext _tenantContext;
    private readonly ITenantQuotaValidator _quotaValidator;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly IAuditLogService _auditLogService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RowMutationRepository(
        IDbConnection db, 
        ILogger<RowMutationRepository> logger,
        ProjectScope projectScope,
        IEntityMetadataProvider metadataProvider,
        TenantContext tenantContext,
        ITenantQuotaValidator quotaValidator,
        IWorkflowEngine workflowEngine,
        IAuditLogService auditLogService,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _logger = logger;
        _projectScope = projectScope;
        _metadataProvider = metadataProvider;
        _tenantContext = tenantContext;
        _quotaValidator = quotaValidator;
        _workflowEngine = workflowEngine;
        _auditLogService = auditLogService;
        _httpContextAccessor = httpContextAccessor;
    }
    
    private string GetCurrentUserName()
    {
        return _httpContextAccessor?.HttpContext?.User?.Identity?.Name ?? "system";
    }

    private bool IsSensitiveField(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName)) return false;
        var lower = fieldName.ToLowerInvariant();
        return lower.Contains("password") || 
               lower.Contains("pwd") || 
               lower.Contains("secret") || 
               lower.Contains("ssn") || 
               lower.Contains("token") || 
               lower.Contains("key");
    }

    /// <inheritdoc/>
    public async Task<int> InsertAsync(
        string table,
        IReadOnlyList<KeyValuePair<string, object?>> fields,
        IDbTransaction? tx = null)
    {
        var entityPair = _metadataProvider.GetAll().FirstOrDefault(kv => string.Equals(kv.Value.Table, table, StringComparison.OrdinalIgnoreCase));
        var entityName = entityPair.Key;
        var entityDef = entityPair.Value;
        var tenantId = _tenantContext?.TenantId ?? "DefaultTenant";

        // 1. Tenant rows limit check
        if (entityDef != null)
        {
            await _quotaValidator.CheckDatabaseRowsQuotaAsync(tenantId, table);
        }

        // 2. Workflow initial state inject
        var mutableFields = fields.ToList();
        if (entityDef != null && entityDef.Workflow?.Enabled == true)
        {
            var stateField = entityDef.Workflow.StateField ?? "status";
            var stateKv = mutableFields.FirstOrDefault(f => string.Equals(f.Key, stateField, StringComparison.OrdinalIgnoreCase));
            if (stateKv.Key == null)
            {
                mutableFields.Add(new KeyValuePair<string, object?>(stateField, entityDef.Workflow.InitialState));
            }
        }

        // 自动注入时间戳 (CreatedAt, UpdatedAt)
        if (entityDef != null)
        {
            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            var createdCol = entityDef.Columns.Keys.FirstOrDefault(k => string.Equals(k, "CreatedAt", StringComparison.OrdinalIgnoreCase) || string.Equals(k, "created_at", StringComparison.OrdinalIgnoreCase));
            if (createdCol != null && !mutableFields.Any(f => string.Equals(f.Key, createdCol, StringComparison.OrdinalIgnoreCase)))
            {
                mutableFields.Add(new KeyValuePair<string, object?>(createdCol, nowStr));
            }
            
            var updatedCol = entityDef.Columns.Keys.FirstOrDefault(k => string.Equals(k, "UpdatedAt", StringComparison.OrdinalIgnoreCase) || string.Equals(k, "updated_at", StringComparison.OrdinalIgnoreCase));
            if (updatedCol != null && !mutableFields.Any(f => string.Equals(f.Key, updatedCol, StringComparison.OrdinalIgnoreCase)))
            {
                mutableFields.Add(new KeyValuePair<string, object?>(updatedCol, nowStr));
            }

            // 自动注入租户 ID (逻辑隔离场景下)
            if (_tenantContext != null && _tenantContext.Strategy.Equals("logical", StringComparison.OrdinalIgnoreCase))
            {
                var tenantCol = entityDef.Columns.Keys.FirstOrDefault(k => string.Equals(k, "tenant_id", StringComparison.OrdinalIgnoreCase) || string.Equals(k, "TenantId", StringComparison.OrdinalIgnoreCase));
                if (tenantCol != null && !mutableFields.Any(f => string.Equals(f.Key, tenantCol, StringComparison.OrdinalIgnoreCase)))
                {
                    mutableFields.Add(new KeyValuePair<string, object?>(tenantCol, tenantId));
                }
            }
        }

        var cols = string.Join(", ", mutableFields.Select(f => $"\"{f.Key}\""));
        var parms = string.Join(", ", mutableFields.Select(f => $"@{f.Key}"));
        var sql = $"INSERT INTO \"{table}\" ({cols}) VALUES ({parms})";

        var param = new DynamicParameters();
        foreach (var f in mutableFields)
            param.Add(f.Key, f.Value);

        _logger.LogInformation("RowMutationRepository.InsertAsync table={Table} sql={Sql}", table, sql);
        var affectedRows = await _db.ExecuteAsync(sql, param, tx);

        // 3. Webhook Outbox Trigger
        if (entityDef != null && affectedRows > 0)
        {
            await EnqueueWebhookEventAsync(tenantId, entityName, "created", mutableFields, tx);
        }

        // 4. 写入审计日志 (Audit Log)
        if (affectedRows > 0)
        {
            var diff = new Dictionary<string, object>();
            var changedFields = new Dictionary<string, object>();
            foreach (var kv in mutableFields)
            {
                var val = IsSensitiveField(kv.Key) ? "[REDACTED]" : kv.Value;
                changedFields[kv.Key] = new { old = (object?)null, @new = val };
            }
            diff["changed_fields"] = changedFields;

            await _auditLogService.WriteAsync(
                action: "create",
                entity: entityName ?? table,
                detail: JsonSerializer.Serialize(diff),
                userName: GetCurrentUserName(),
                connection: _db,
                transaction: tx
            );
        }

        return affectedRows;
    }

    /// <inheritdoc/>
    public async Task<int> UpdateAsync(
        string table,
        string primaryKeyColumn,
        object primaryKeyValue,
        IReadOnlyList<KeyValuePair<string, object?>> fields,
        IDbTransaction? tx = null)
    {
        var entityPair = _metadataProvider.GetAll().FirstOrDefault(kv => string.Equals(kv.Value.Table, table, StringComparison.OrdinalIgnoreCase));
        var entityName = entityPair.Key;
        var entityDef = entityPair.Value;
        var tenantId = _tenantContext?.TenantId ?? "DefaultTenant";
        var mutableFields = fields.ToList();

        // 1. Workflow validation and state machine evaluation
        if (entityDef != null && entityDef.Workflow?.Enabled == true)
        {
            var stateField = entityDef.Workflow.StateField ?? "status";
            var stateKv = mutableFields.FirstOrDefault(f => string.Equals(f.Key, stateField, StringComparison.OrdinalIgnoreCase));
            
            if (stateKv.Key != null)
            {
                var currentStateSql = $"SELECT \"{stateField}\" FROM \"{table}\" WHERE \"{primaryKeyColumn}\" = @id";
                var currentState = await _db.QueryFirstOrDefaultAsync<string>(currentStateSql, new { id = primaryKeyValue }, tx) ?? entityDef.Workflow.InitialState;
                var targetState = stateKv.Value?.ToString();

                if (!string.Equals(currentState, targetState, StringComparison.OrdinalIgnoreCase))
                {
                    var transition = entityDef.Workflow.Transitions.FirstOrDefault(t => 
                        t.From.Any(s => string.Equals(s, currentState, StringComparison.OrdinalIgnoreCase)) &&
                        string.Equals(t.To, targetState, StringComparison.OrdinalIgnoreCase));

                    if (transition == null)
                    {
                        throw new InvalidOperationException($"Workflow validation failed: Transition from '{currentState}' to '{targetState}' is not allowed.");
                    }

                    var wfResult = await _workflowEngine.TriggerTransitionAsync(entityName, primaryKeyValue.ToString()!, transition.Name, new Dictionary<string, object>
                    {
                        { "operator", tenantId }
                    });

                    if (!wfResult.Success)
                    {
                        throw new InvalidOperationException($"Workflow transition failed: {wfResult.ErrorMessage}");
                    }

                    mutableFields = mutableFields.Where(f => !string.Equals(f.Key, stateField, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }
        }

        // 自动更新时间戳 (UpdatedAt)
        if (entityDef != null)
        {
            var updatedCol = entityDef.Columns.Keys.FirstOrDefault(k => string.Equals(k, "UpdatedAt", StringComparison.OrdinalIgnoreCase) || string.Equals(k, "updated_at", StringComparison.OrdinalIgnoreCase));
            if (updatedCol != null && !mutableFields.Any(f => string.Equals(f.Key, updatedCol, StringComparison.OrdinalIgnoreCase)))
            {
                mutableFields.Add(new KeyValuePair<string, object?>(updatedCol, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")));
            }
        }

        // 变更追踪前置：查询旧记录以用于 Diff 对比
        var selectSql = $"SELECT * FROM \"{table}\" WHERE \"{primaryKeyColumn}\" = @id";
        var oldRowRaw = await _db.QueryFirstOrDefaultAsync<dynamic>(selectSql, new { id = primaryKeyValue }, tx);
        var oldRowDict = oldRowRaw as IDictionary<string, object>;

        int affectedRows = 0;
        if (mutableFields.Count > 0)
        {
            var setSql = string.Join(", ", mutableFields.Select(f => $"\"{f.Key}\" = @{f.Key}"));
            var sql = $"UPDATE \"{table}\" SET {setSql} WHERE \"{primaryKeyColumn}\" = @__pk";

            var param = new DynamicParameters();
            foreach (var f in mutableFields)
                param.Add(f.Key, f.Value);
            param.Add("__pk", primaryKeyValue);

            _logger.LogInformation("RowMutationRepository.UpdateAsync table={Table} pk={Pk} sql={Sql}", table, primaryKeyValue, sql);
            affectedRows = await _db.ExecuteAsync(sql, param, tx);
        }
        else
        {
            affectedRows = 1;
        }

        // 2. Webhook Outbox Trigger
        if (entityDef != null && affectedRows > 0)
        {
            await EnqueueWebhookEventAsync(tenantId, entityName, "updated", fields, tx);
        }

        // 3. 写入审计日志 (Audit Log Diff)
        if (affectedRows > 0 && oldRowDict != null)
        {
            var diff = new Dictionary<string, object>();
            var changedFields = new Dictionary<string, object>();

            foreach (var kv in mutableFields)
            {
                // 获取旧值
                oldRowDict.TryGetValue(kv.Key, out var oldVal);

                // 对比是否一致
                var isChanged = false;
                if (oldVal == null && kv.Value != null) isChanged = true;
                else if (oldVal != null && kv.Value == null) isChanged = true;
                else if (oldVal != null && !oldVal.Equals(kv.Value)) isChanged = true;

                if (isChanged)
                {
                    var isSensitive = IsSensitiveField(kv.Key);
                    changedFields[kv.Key] = new
                    {
                        old = isSensitive ? "[REDACTED]" : oldVal,
                        @new = isSensitive ? "[REDACTED]" : kv.Value
                    };
                }
            }

            if (changedFields.Count > 0)
            {
                diff["changed_fields"] = changedFields;
                await _auditLogService.WriteAsync(
                    action: "update",
                    entity: entityName ?? table,
                    detail: JsonSerializer.Serialize(diff),
                    userName: GetCurrentUserName(),
                    connection: _db,
                    transaction: tx
                );
            }
        }

        return affectedRows;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteAsync(
        string table,
        string primaryKeyColumn,
        object primaryKeyValue,
        IDbTransaction? tx = null)
    {
        var entityPair = _metadataProvider.GetAll().FirstOrDefault(kv => string.Equals(kv.Value.Table, table, StringComparison.OrdinalIgnoreCase));
        var entityName = entityPair.Key;
        var entityDef = entityPair.Value;
        var tenantId = _tenantContext?.TenantId ?? "DefaultTenant";

        // 变更追踪前置：查询旧记录
        var selectSql = $"SELECT * FROM \"{table}\" WHERE \"{primaryKeyColumn}\" = @id";
        var oldRowRaw = await _db.QueryFirstOrDefaultAsync<dynamic>(selectSql, new { id = primaryKeyValue }, tx);
        var oldRowDict = oldRowRaw as IDictionary<string, object>;

        var affectedRows = 0;

        // 支持自动软删除（逻辑删除）
        if (entityDef != null && entityDef.SoftDelete)
        {
            var sets = new List<string> { "\"IsDeleted\" = 1" };
            var param = new DynamicParameters();
            param.Add("__pk", primaryKeyValue);

            var deletedCol = entityDef.Columns.Keys.FirstOrDefault(k => string.Equals(k, "DeletedAt", StringComparison.OrdinalIgnoreCase) || string.Equals(k, "deleted_at", StringComparison.OrdinalIgnoreCase));
            if (deletedCol != null)
            {
                var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                sets.Add($"\"{deletedCol}\" = @DeletedAt");
                param.Add("DeletedAt", nowStr);
            }

            var sql = $"UPDATE \"{table}\" SET {string.Join(", ", sets)} WHERE \"{primaryKeyColumn}\" = @__pk";
            _logger.LogInformation("RowMutationRepository.DeleteAsync (SoftDelete) table={Table} pk={Pk} sql={Sql}", table, primaryKeyValue, sql);
            affectedRows = await _db.ExecuteAsync(sql, param, tx);
        }
        else
        {
            var sql = $"DELETE FROM \"{table}\" WHERE \"{primaryKeyColumn}\" = @__pk";
            _logger.LogInformation("RowMutationRepository.DeleteAsync table={Table} pk={Pk} sql={Sql}", table, primaryKeyValue, sql);
            affectedRows = await _db.ExecuteAsync(sql, new { __pk = primaryKeyValue }, tx);
        }

        // Webhook Outbox Trigger
        if (entityDef != null && affectedRows > 0)
        {
            var payloadData = new Dictionary<string, object?> { { primaryKeyColumn, primaryKeyValue } };
            await EnqueueWebhookEventAsync(tenantId, entityName, "deleted", payloadData, tx);
        }

        // 写入审计日志 (Audit Log)
        if (affectedRows > 0 && oldRowDict != null)
        {
            var diff = new Dictionary<string, object>();
            var changedFields = new Dictionary<string, object>();

            foreach (var kv in oldRowDict)
            {
                var isSensitive = IsSensitiveField(kv.Key);
                changedFields[kv.Key] = new
                {
                    old = isSensitive ? "[REDACTED]" : kv.Value,
                    @new = (object?)null
                };
            }
            diff["changed_fields"] = changedFields;

            await _auditLogService.WriteAsync(
                action: "delete",
                entity: entityName ?? table,
                detail: JsonSerializer.Serialize(diff),
                userName: GetCurrentUserName(),
                connection: _db,
                transaction: tx
            );
        }

        return affectedRows;
    }

    private async Task EnqueueWebhookEventAsync(
        string tenantId,
        string entityName,
        string action,
        IEnumerable<KeyValuePair<string, object?>> data,
        IDbTransaction? tx)
    {
        try
        {
            if (_projectScope?.IsSet != true) return;
            var projectName = _projectScope.Current.Name;
            var projectDir = Path.Combine(Directory.GetCurrentDirectory(), "projects", projectName);
            if (!Directory.Exists(projectDir))
            {
                projectDir = Path.Combine(Directory.GetCurrentDirectory(), "NetYamlForge", "projects", projectName);
            }

            var webhooksPath = Path.Combine(projectDir, "config", "webhooks.yaml");
            if (!File.Exists(webhooksPath))
            {
                webhooksPath = Path.Combine(projectDir, "webhooks.yaml");
            }

            if (!File.Exists(webhooksPath)) return;

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var content = await File.ReadAllTextAsync(webhooksPath);
            var subscriptionList = deserializer.Deserialize<WebhookSubscriptionList>(content);
            if (subscriptionList?.Webhooks == null) return;

            var eventName = $"entity.{entityName}.{action}";
            var matchedSubscriptions = subscriptionList.Webhooks.Where(w => 
                w.Enabled && 
                w.Events.Any(e => string.Equals(e, eventName, StringComparison.OrdinalIgnoreCase) || e == "*"));

            if (!matchedSubscriptions.Any()) return;

            await EnsureWebhookOutboxTableExistsAsync();

            var dataDict = data.ToDictionary(k => k.Key, v => v.Value);
            var payload = JsonSerializer.Serialize(dataDict);

            foreach (var sub in matchedSubscriptions)
            {
                var sql = @"
                    INSERT INTO ""WebhookOutbox"" (""Id"", ""TenantId"", ""EventName"", ""Payload"", ""TargetUrl"", ""Secret"", ""State"", ""Attempts"", ""NextAttemptAt"", ""CreatedAt"")
                    VALUES (@Id, @TenantId, @EventName, @Payload, @TargetUrl, @Secret, @State, @Attempts, @NextAttemptAt, @CreatedAt)";
                
                await _db.ExecuteAsync(sql, new
                {
                    Id = Guid.NewGuid().ToString("N"),
                    TenantId = tenantId,
                    EventName = eventName,
                    Payload = payload,
                    TargetUrl = sub.TargetUrl,
                    Secret = sub.Secret,
                    State = 0,
                    Attempts = 0,
                    NextAttemptAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                }, tx);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue webhook event for entity {Entity} action {Action}", entityName, action);
        }
    }

    private async Task EnsureWebhookOutboxTableExistsAsync()
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS ""WebhookOutbox"" (
                ""Id"" TEXT PRIMARY KEY,
                ""TenantId"" TEXT NOT NULL,
                ""EventName"" TEXT NOT NULL,
                ""Payload"" TEXT NOT NULL,
                ""TargetUrl"" TEXT NOT NULL,
                ""Secret"" TEXT NULL,
                ""State"" INTEGER NOT NULL,
                ""Attempts"" INTEGER NOT NULL,
                ""ErrorMessage"" TEXT NULL,
                ""NextAttemptAt"" TEXT NOT NULL,
                ""CreatedAt"" TEXT NOT NULL
            )";
        await _db.ExecuteAsync(sql);
    }
}
