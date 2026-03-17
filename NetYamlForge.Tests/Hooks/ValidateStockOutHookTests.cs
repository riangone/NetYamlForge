// 責務: ValidateStockOutHook のユニットテスト。
// 正常系・異常系・フィールド欠損の3パターンを最低限テストする。

using NetYamlForge.Services.Hooks;
using NetYamlForge.Projects.Inventory.Hooks;
using Xunit;

namespace NetYamlForge.Tests.Hooks;

public class ValidateStockOutHookTests
{
    // ─── ヘルパー ───────────────────────────────────────────
    private static EntityHookContext MakeCtx(
        Dictionary<string, object?> values,
        CrudOperation op = CrudOperation.Create)
        => new() { Entity = "entity", Operation = op, Values = values };

    // ─── 正常系 ─────────────────────────────────────────────

    [Fact]
    public async Task BeforeAsync_ValidInput_ReturnsContinue()
    {
        var sut = new ValidateStockOutHook(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ValidateStockOutHook>.Instance);
        var ctx = MakeCtx(new() { ["fieldName"] = "valid_value" });

        var result = await sut.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);
    }

    // ─── 異常系 ─────────────────────────────────────────────

    [Fact]
    public async Task BeforeAsync_InvalidInput_ReturnsAbort()
    {
        var sut = new ValidateStockOutHook(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ValidateStockOutHook>.Instance);
        var ctx = MakeCtx(new() { ["fieldName"] = "invalid_value" });

        // TODO: 不正値を入力してフックが Abort を返すことを確認
        var result = await sut.BeforeAsync(ctx, null!, null);

        // TODO: 実装に合わせてアサートを修正
        // Assert.True(result.Cancel);
        // Assert.Contains("期待するエラーメッセージ", result.CancelMessage);
        Assert.False(result.Cancel); // ← 実装後に適切な検証に変更
    }

    // ─── フィールド欠損（スキップ確認）──────────────────────

    [Fact]
    public async Task BeforeAsync_MissingField_ReturnsContinue()
    {
        var sut = new ValidateStockOutHook(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ValidateStockOutHook>.Instance);
        var ctx = MakeCtx(new()); // フィールドなし

        var result = await sut.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel); // フィールドがなければスキップする
    }

    // ─── Hook 名の確認 ──────────────────────────────────────

    [Fact]
    public void Name_IsCorrectYamlKey()
    {
        var sut = new ValidateStockOutHook(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ValidateStockOutHook>.Instance);
        Assert.Equal("validate_stock_out", sut.Name);
    }
}