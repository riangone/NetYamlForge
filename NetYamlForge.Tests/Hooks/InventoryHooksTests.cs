using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NetYamlForge.Projects.Inventory.Hooks;
using NetYamlForge.Services.Hooks;
using Xunit;

namespace NetYamlForge.Tests.Hooks;

public class InventoryHooksTests
{
    // ── NormalizeProductCodeHook ───────────────────────────────────────────

    [Fact]
    public async Task NormalizeProductCode_TrimsAndUppercases()
    {
        var hook = new NormalizeProductCodeHook();
        var ctx = MakeContext(new Dictionary<string, object?> { ["Code"] = "  abc-123  " });

        var result = await hook.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);
        Assert.Equal("ABC-123", ctx.Values["Code"]);
    }

    [Fact]
    public async Task NormalizeProductCode_AlreadyUppercase_Unchanged()
    {
        var hook = new NormalizeProductCodeHook();
        var ctx = MakeContext(new Dictionary<string, object?> { ["Code"] = "PRD-001" });

        await hook.BeforeAsync(ctx, null!, null);

        Assert.Equal("PRD-001", ctx.Values["Code"]);
    }

    [Fact]
    public async Task NormalizeProductCode_NoCodeField_Continues()
    {
        var hook = new NormalizeProductCodeHook();
        var ctx = MakeContext(new Dictionary<string, object?> { ["Name"] = "商品A" });

        var result = await hook.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);
    }

    // ── ValidateProductPriceHook ───────────────────────────────────────────

    [Theory]
    [InlineData("CostPrice", "-1")]
    [InlineData("SellingPrice", "-100")]
    public async Task ValidateProductPrice_NegativePrice_Aborts(string field, string value)
    {
        var hook = new ValidateProductPriceHook();
        var ctx = MakeContext(new Dictionary<string, object?> { [field] = value });

        var result = await hook.BeforeAsync(ctx, null!, null);

        Assert.True(result.Cancel);
        Assert.NotNull(result.CancelMessage);
    }

    [Theory]
    [InlineData("CostPrice", "0")]
    [InlineData("SellingPrice", "9800")]
    [InlineData("CostPrice", "500.5")]
    public async Task ValidateProductPrice_ZeroOrPositive_Continues(string field, string value)
    {
        var hook = new ValidateProductPriceHook();
        var ctx = MakeContext(new Dictionary<string, object?> { [field] = value });

        var result = await hook.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);
    }

    [Fact]
    public async Task ValidateProductPrice_NullValue_Continues()
    {
        var hook = new ValidateProductPriceHook();
        var ctx = MakeContext(new Dictionary<string, object?> { ["CostPrice"] = null });

        var result = await hook.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);
    }

    // ── ValidateStockQuantityHook ──────────────────────────────────────────

    [Fact]
    public async Task ValidateStockQuantity_Negative_Aborts()
    {
        var hook = new ValidateStockQuantityHook();
        var ctx = MakeContext(new Dictionary<string, object?> { ["Quantity"] = "-1" });

        var result = await hook.BeforeAsync(ctx, null!, null);

        Assert.True(result.Cancel);
        Assert.NotNull(result.CancelMessage);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("100")]
    [InlineData("0.5")]
    public async Task ValidateStockQuantity_ZeroOrPositive_Continues(string value)
    {
        var hook = new ValidateStockQuantityHook();
        var ctx = MakeContext(new Dictionary<string, object?> { ["Quantity"] = value });

        var result = await hook.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);
    }

    // ── SetStockUpdatedAtHook ──────────────────────────────────────────────

    [Fact]
    public async Task SetStockUpdatedAt_SetsToday()
    {
        var hook = new SetStockUpdatedAtHook();
        var ctx = MakeContext(new Dictionary<string, object?>());

        var result = await hook.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);
        Assert.True(ctx.Values.ContainsKey("UpdatedAt"));
        Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd"), ctx.Values["UpdatedAt"]);
    }

    [Fact]
    public async Task SetStockUpdatedAt_OverwritesExistingValue()
    {
        var hook = new SetStockUpdatedAtHook();
        var ctx = MakeContext(new Dictionary<string, object?> { ["UpdatedAt"] = "2020-01-01" });

        await hook.BeforeAsync(ctx, null!, null);

        Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd"), ctx.Values["UpdatedAt"]);
    }

    // ── ValidateMovementQuantityHook ───────────────────────────────────────

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("-0.1")]
    public async Task ValidateMovementQuantity_ZeroOrNegative_Aborts(string value)
    {
        var hook = new ValidateMovementQuantityHook();
        var ctx = MakeContext(new Dictionary<string, object?> { ["Quantity"] = value });

        var result = await hook.BeforeAsync(ctx, null!, null);

        Assert.True(result.Cancel);
        Assert.NotNull(result.CancelMessage);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("10")]
    [InlineData("0.5")]
    public async Task ValidateMovementQuantity_Positive_Continues(string value)
    {
        var hook = new ValidateMovementQuantityHook();
        var ctx = MakeContext(new Dictionary<string, object?> { ["Quantity"] = value });

        var result = await hook.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);
    }

    // ── ValidateMovementTypeHook ───────────────────────────────────────────

    [Theory]
    [InlineData("in")]
    [InlineData("out")]
    [InlineData("transfer")]
    public async Task ValidateMovementType_ValidTypes_Continue(string movementType)
    {
        var hook = new ValidateMovementTypeHook();
        var ctx = MakeContext(new Dictionary<string, object?> { ["MovementType"] = movementType });

        var result = await hook.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);
    }

    [Theory]
    [InlineData("receive")]
    [InlineData("ship")]
    [InlineData("INVALID")]
    public async Task ValidateMovementType_InvalidType_Aborts(string movementType)
    {
        var hook = new ValidateMovementTypeHook();
        var ctx = MakeContext(new Dictionary<string, object?> { ["MovementType"] = movementType });

        var result = await hook.BeforeAsync(ctx, null!, null);

        Assert.True(result.Cancel);
        Assert.NotNull(result.CancelMessage);
    }

    // ── SetMovedAtDefaultHook ──────────────────────────────────────────────

    [Fact]
    public async Task SetMovedAtDefault_NoValue_SetsToday()
    {
        var hook = new SetMovedAtDefaultHook();
        var ctx = MakeContext(new Dictionary<string, object?>());

        var result = await hook.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);
        Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd"), ctx.Values["MovedAt"]);
    }

    [Fact]
    public async Task SetMovedAtDefault_NullValue_SetsToday()
    {
        var hook = new SetMovedAtDefaultHook();
        var ctx = MakeContext(new Dictionary<string, object?> { ["MovedAt"] = null });

        await hook.BeforeAsync(ctx, null!, null);

        Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd"), ctx.Values["MovedAt"]);
    }

    [Fact]
    public async Task SetMovedAtDefault_AlreadySet_KeepsExistingValue()
    {
        var hook = new SetMovedAtDefaultHook();
        const string existingDate = "2026-01-15";
        var ctx = MakeContext(new Dictionary<string, object?> { ["MovedAt"] = existingDate });

        await hook.BeforeAsync(ctx, null!, null);

        Assert.Equal(existingDate, ctx.Values["MovedAt"]);
    }

    // ── StockUpdateAfterMovementHook（BeforeAsync は常に Continue）────────

    [Fact]
    public async Task StockUpdateAfterMovement_BeforeAsync_AlwaysContinues()
    {
        var hook = new StockUpdateAfterMovementHook();
        var ctx = MakeContext(new Dictionary<string, object?>());

        var result = await hook.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);
    }

    [Fact]
    public async Task StockUpdateAfterMovement_MissingFields_DoesNotThrow()
    {
        // ProductId / WarehouseId / MovementType / Quantity が揃っていない場合は
        // 何もせずに正常終了することを確認する。
        var hook = new StockUpdateAfterMovementHook();
        var ctx = MakeContext(new Dictionary<string, object?> { ["ProductId"] = "1" });

        // db = null でも例外が出ないこと（DBアクセスが発生しない）
        await hook.AfterAsync(ctx, null!, null);
    }

    [Fact]
    public async Task StockUpdateAfterMovement_TransferType_DoesNotAccessDb()
    {
        var hook = new StockUpdateAfterMovementHook();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["ProductId"]    = "1",
            ["WarehouseId"]  = "1",
            ["MovementType"] = "transfer",
            ["Quantity"]     = "10",
        });

        // transfer の場合は DB アクセスなしで即返却される
        await hook.AfterAsync(ctx, null!, null);
    }

    // ─────────────────────────────────────────────────────────────────────

    private static EntityHookContext MakeContext(Dictionary<string, object?> values)
        => new EntityHookContext { Entity = "test_entity", Operation = CrudOperation.Create, Values = values };
}
