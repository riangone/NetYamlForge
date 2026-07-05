using System;
using System.Data;
using System.Net.Http;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.Hooks;

/// <summary>
/// [汎用監査] 操作内容をアプリログに記録するフック。
/// </summary>
public class AuditLogHook : IEntityHook
{
    private readonly ILogger<AuditLogHook> _logger;

    public AuditLogHook(ILogger<AuditLogHook> logger)
    {
        _logger = logger;
    }

    public string Name => "audit_log";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var message = $"{ctx.Operation} completed — entity={ctx.Entity}, id={ctx.Id?.ToString() ?? "(new)"}, user={ctx.UserName ?? "unknown"}";
        _logger.LogInformation("[Hook:audit_log] {Message}", message);

        try
        {
            const string sql = @"
INSERT INTO AuditLog (UserName, Action, Entity, Detail, CreatedAt)
VALUES (@UserName, @Action, @Entity, @Detail, @CreatedAt)";

            var param = new
            {
                UserName = ctx.UserName,
                Action = ctx.Operation.ToString(),
                Entity = ctx.Entity,
                Detail = message,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };

            await db.ExecuteAsync(sql, param, tx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Hook:audit_log] 監査ログの記録に失敗しました");
        }
    }
}

/// <summary>
/// [汎用通知] 操作完了後に指定 Webhook URL へ POST するフック。
/// </summary>
public class WebhookHook : IEntityHook
{
    private readonly ILogger<WebhookHook> _logger;
    private readonly IHttpClientFactory? _httpClientFactory;

    public WebhookHook(ILogger<WebhookHook> logger, IHttpClientFactory? httpClientFactory = null)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public string Name => "webhook";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var config = ctx.Data.TryGetValue("__hookConfig", out var c) && c is string s ? s : string.Empty;
        if (string.IsNullOrWhiteSpace(config) || _httpClientFactory == null)
            return;

        try
        {
            var client = _httpClientFactory.CreateClient();
            var payload = new
            {
                entity = ctx.Entity,
                operation = ctx.Operation.ToString(),
                id = ctx.Id,
                user = ctx.UserName,
                values = ctx.Values,
                timestamp = DateTime.Now
            };

            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await client.PostAsync(config, content);
            _logger.LogInformation("[Hook:webhook] Webhook call to {Url} returned {StatusCode}", config, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Hook:webhook] Failed to call webhook {Url}", config);
        }
    }
}

/// <summary>
/// [汎用関連操作] 関連テーブルのカウント値を更新するフック。
/// </summary>
public class UpdateCountHook : IEntityHook
{
    private readonly ILogger<UpdateCountHook> _logger;

    public UpdateCountHook(ILogger<UpdateCountHook> logger)
    {
        _logger = logger;
    }

    public string Name => "update_count";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var config = ctx.Data.TryGetValue("__hookConfig", out var c) && c is string s ? s : string.Empty;
        if (string.IsNullOrWhiteSpace(config))
            return;

        var parts = config.Split(':');
        if (parts.Length != 4) return;

        var sourceEntity = parts[0].Trim();
        var sourceKey = parts[1].Trim();
        var targetTable = parts[2].Trim();
        var targetForeignKey = parts[3].Trim();

        if (!ctx.Values.TryGetValue(sourceKey, out var keyValue) || keyValue == null)
            return;

        var isCreate = ctx.Operation == CrudOperation.Create;
        var delta = isCreate ? 1 : -1;

        if (!HookConstants.HookIdentifierRegex.IsMatch(sourceEntity) || !HookConstants.HookIdentifierRegex.IsMatch(sourceKey))
        {
            _logger.LogWarning("[Hook:update_count] 無効な識別子が渡されました: entity={Entity} key={Key}", sourceEntity, sourceKey);
            return;
        }

        var countColumn = $"{sourceEntity}Count";
#pragma warning disable DCS001
        var sql = $"UPDATE {sourceEntity} SET {countColumn} = {countColumn} + {delta} WHERE {sourceKey} = @key";
#pragma warning restore DCS001

        await db.ExecuteAsync(sql, new { key = keyValue }, tx);
        _logger.LogInformation("[Hook:update_count] Updated count for {Entity}.{Key} = {Value}", sourceEntity, sourceKey, keyValue);
    }
}

/// <summary>
/// [汎用関連操作] 関連レコードのフィールドを更新するフック。
/// </summary>
public class UpdateRelatedHook : IEntityHook
{
    private readonly ILogger<UpdateRelatedHook> _logger;

    public UpdateRelatedHook(ILogger<UpdateRelatedHook> logger)
    {
        _logger = logger;
    }

    public string Name => "update_related";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var config = ctx.Data.TryGetValue("__hookConfig", out var c) && c is string s ? s : string.Empty;
        if (string.IsNullOrWhiteSpace(config))
            return;

        var parts = config.Split(':');
        if (parts.Length != 6) return;

        var sourceEntity = parts[0].Trim();
        var sourceKey = parts[1].Trim();
        var targetTable = parts[2].Trim();
        var targetFK = parts[3].Trim();
        var updateField = parts[4].Trim();
        var updateValue = parts[5].Trim();

        if (!ctx.Values.TryGetValue(sourceKey, out var keyValue) || keyValue == null)
            return;

        if (!HookConstants.HookIdentifierRegex.IsMatch(targetTable) || !HookConstants.HookIdentifierRegex.IsMatch(updateField) || !HookConstants.HookIdentifierRegex.IsMatch(targetFK))
        {
            _logger.LogWarning("[Hook:update_related] 無効な識別子が渡されました: table={Table} field={Field} fk={FK}", targetTable, updateField, targetFK);
            return;
        }

#pragma warning disable DCS001
        var sql = $"UPDATE {targetTable} SET {updateField} = @value WHERE {targetFK} = @key";
#pragma warning restore DCS001

        await db.ExecuteAsync(sql, new { value = updateValue, key = keyValue }, tx);
        _logger.LogInformation("[Hook:update_related] Updated {Table}.{FK} = {Key}", targetTable, targetFK, keyValue);
    }
}

/// <summary>
/// [汎用ソフト削除] 削除フラグと削除日時を設定するフック。
/// </summary>
public class SoftDeleteHook : IEntityHook
{
    private readonly ILogger<SoftDeleteHook> _logger;

    public SoftDeleteHook(ILogger<SoftDeleteHook> logger)
    {
        _logger = logger;
    }

    public string Name => "soft_delete";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var config = ctx.Data.TryGetValue("__hookConfig", out var c) && c is string s ? s : string.Empty;
        if (string.IsNullOrWhiteSpace(config))
            return Task.FromResult(HookResult.Continue());

        var parts = config.Split(':');
        if (parts.Length < 2) return Task.FromResult(HookResult.Continue());

        var deletedFlag = parts[0].Trim();
        var deletedAtColumn = parts[1].Trim();

        ctx.Values[deletedFlag] = 1;
        ctx.Values[deletedAtColumn] = DateTime.Now;

        if (!string.IsNullOrEmpty(ctx.UserName))
        {
            var deletedByColumn = parts.Length >= 3 ? parts[2].Trim() : null;
            if (!string.IsNullOrEmpty(deletedByColumn))
            {
                ctx.Values[deletedByColumn] = ctx.UserName;
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
