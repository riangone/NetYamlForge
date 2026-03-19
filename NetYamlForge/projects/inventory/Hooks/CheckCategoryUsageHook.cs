// 責務：カテゴリ削除時に使用状況をチェックするフック

using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Inventory.Hooks;

/// <summary>
/// 在庫管理フック：カテゴリ削除時の使用チェック
/// 関連商品がある場合は削除を防止
/// </summary>
public sealed class CheckCategoryUsageHook : IEntityHook
{
    private readonly ILogger<CheckCategoryUsageHook> _logger;

    public string Name => "check_category_usage";

    public CheckCategoryUsageHook(ILogger<CheckCategoryUsageHook> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Delete)
            return HookResult.Continue();

        if (ctx.Id is int categoryId)
        {
            // 関連する商品数をチェック
            var sql = "SELECT COUNT(*) FROM Products WHERE CategoryId = @CategoryId";
            var productCount = await db.ExecuteScalarAsync<int>(sql, new { CategoryId = categoryId }, tx);

            if (productCount > 0)
            {
                _logger.LogWarning(
                    "カテゴリ {CategoryId} には {ProductCount} 件の商品が関連しています。",
                    categoryId, productCount);
                return HookResult.Abort($"このカテゴリには {productCount} 件の商品が関連しているため削除できません。");
            }
        }

        return HookResult.Continue();
    }

    /// <inheritdoc />
    public Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}
