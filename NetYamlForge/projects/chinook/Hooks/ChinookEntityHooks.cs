// Chinook プロジェクト固有のエンティティフック実装
// 音楽商店固有の CRUD 前後処理を定義します。

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Chinook.Hooks;

/// <summary>
/// Chinook 固有：顧客作成時に自動でウェルカムメールを送信するフック。
/// entities.yml の hooks.afterCreate で使用します。
/// </summary>
public class ChinookCustomerWelcomeHook : IEntityHook
{
    private readonly ILogger<ChinookCustomerWelcomeHook> _logger;
    private readonly IDbConnection _db;

    public string Name => "chinook_customer_welcome";

    public ChinookCustomerWelcomeHook(ILogger<ChinookCustomerWelcomeHook> logger, IDbConnection db)
    {
        _logger = logger;
        _db = db;
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 前処理は不要
        return Task.FromResult(HookResult.Continue());
    }

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create)
            return;

        // 顧客 ID から顧客情報を取得
        if (ctx.Id is int customerId)
        {
            var sql = "SELECT FirstName, LastName, Email FROM Customer WHERE CustomerId = @CustomerId";
            var customer = await (tx != null ? 
                db.QueryFirstOrDefaultAsync<CustomerRow>(sql, new { CustomerId = customerId }, tx) :
                db.QueryFirstOrDefaultAsync<CustomerRow>(sql, new { CustomerId = customerId }));

            if (customer != null)
            {
                _logger.LogInformation(
                    "[Chinook] 顧客 {FirstName} {LastName} さん、ようこそ！メールを {Email} へ送信します。",
                    customer.FirstName, customer.LastName, customer.Email);
            }
        }

        await Task.CompletedTask;
    }

    private class CustomerRow
    {
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";
    }
}

/// <summary>
/// Chinook 固有：請求書作成時に明細の合計を検証するフック。
/// entities.yml の hooks.beforeCreate で使用します。
/// </summary>
public class ChinookInvoiceValidationHook : IEntityHook
{
    private readonly ILogger<ChinookInvoiceValidationHook> _logger;

    public string Name => "chinook_invoice_validation";

    public ChinookInvoiceValidationHook(ILogger<ChinookInvoiceValidationHook> logger)
    {
        _logger = logger;
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create)
            return Task.FromResult(HookResult.Continue());

        // 請求書合計の検証
        if (ctx.Values.TryGetValue("Total", out var totalObj) && totalObj is decimal total)
        {
            if (total < 0)
            {
                _logger.LogWarning("請求書合計が負の値です：{Total}", total);
                return Task.FromResult(HookResult.Abort("請求書合計は 0 以上である必要があります。"));
            }

            if (total > 1000000)
            {
                _logger.LogWarning("請求書合計が上限を超えています：{Total}", total);
                return Task.FromResult(HookResult.Abort("請求書合計の上限は 1,000,000 です。"));
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 後処理は不要
        return Task.CompletedTask;
    }
}

/// <summary>
/// Chinook 固有：トラック更新時に再生時間を秒に変換するフック。
/// entities.yml の hooks.beforeUpdate で使用します。
/// </summary>
public class ChinookTrackDurationHook : IEntityHook
{
    private readonly ILogger<ChinookTrackDurationHook> _logger;

    public string Name => "chinook_track_duration";

    public ChinookTrackDurationHook(ILogger<ChinookTrackDurationHook> logger)
    {
        _logger = logger;
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("Milliseconds", out var msObj) && msObj is int milliseconds)
        {
            // 秒に変換してログ出力
            var seconds = milliseconds / 1000.0;
            _logger.LogDebug(
                "[Chinook] トラック再生時間：{Milliseconds}ms ({Seconds}秒)",
                milliseconds, seconds);
            
            // 必要に応じて values に秒数を追加
            ctx.Values["Seconds"] = seconds;
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Chinook 固有：アーティスト削除時に関連するアルバムをチェックするフック。
/// entities.yml の hooks.beforeDelete で使用します。
/// </summary>
public class ChinookArtistDeleteCheckHook : IEntityHook
{
    private readonly ILogger<ChinookArtistDeleteCheckHook> _logger;

    public string Name => "chinook_artist_delete_check";

    public ChinookArtistDeleteCheckHook(ILogger<ChinookArtistDeleteCheckHook> logger)
    {
        _logger = logger;
    }

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Delete)
            return HookResult.Continue();

        if (ctx.Id is int artistId)
        {
            // 関連するアルバム数をチェック
            var sql = "SELECT COUNT(*) FROM Album WHERE ArtistId = @ArtistId";
            var albumCount = await db.ExecuteScalarAsync<int>(sql, new { ArtistId = artistId }, tx);

            if (albumCount > 0)
            {
                _logger.LogWarning(
                    "アーティスト {ArtistId} には {AlbumCount} 件のアルバムが関連しています。",
                    artistId, albumCount);
                // 警告のみログ出力（処理は継続）
            }
        }

        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}
