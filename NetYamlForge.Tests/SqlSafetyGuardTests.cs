// ファイル概要: SqlSafetyGuard（識別子・式ホワイトリスト検証）のユニットテストです。
// Dashboard YAML から取得した Column / Filter / GroupExpression の
// 安全性検証ロジックを網羅的に確認します。

using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

/// <summary>
/// SqlSafetyGuard.EnsureIdentifier / EnsureExpression のユニットテスト。
/// Dashboard SQL インジェクション対策の核となるホワイトリスト検証を検証します。
/// </summary>
public class SqlSafetyGuardTests
{
    // ── EnsureIdentifier ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("status")]
    [InlineData("invoice_date")]
    [InlineData("TotalAmount")]
    [InlineData("_private")]
    [InlineData("col1")]
    [InlineData("类型")]
    [InlineData("AI提供商")]
    [InlineData("相册")]
    public void EnsureIdentifier_ValidIdentifier_DoesNotThrow(string value)
    {
        // 正当な SQL 識別子は例外なしで通過する
        var ex = Record.Exception(() => SqlSafetyGuard.EnsureIdentifier(value, "test"));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("1bad")]          // 先頭が数字
    [InlineData("bad-col")]       // ハイフン
    [InlineData("col name")]      // スペース
    [InlineData("col;drop")]      // セミコロン
    [InlineData("")]              // 空文字列
    public void EnsureIdentifier_InvalidIdentifier_Throws(string value)
    {
        // 不正な識別子は InvalidOperationException を投げる
        Assert.Throws<InvalidOperationException>(
            () => SqlSafetyGuard.EnsureIdentifier(value, "test"));
    }

    [Fact]
    public void EnsureIdentifier_NullValue_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => SqlSafetyGuard.EnsureIdentifier(null, "test"));
    }

    // ── EnsureExpression ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]                             // null はスキップ（フィルター未設定として許容）
    [InlineData("")]                               // 空文字列もスキップ
    [InlineData("IsDeleted = 0")]                  // 単純条件
    [InlineData("Status = 'active'")]              // 文字列比較
    [InlineData("Status='in_progress' AND Priority='urgent'")]  // 複合条件
    [InlineData("amount > 1000")]                  // 数値比較
    [InlineData("strftime('%Y-%m', invoice_date)")]// SQLite 日付関数
    [InlineData("BillingCountry")]                 // 単純カラム名
    [InlineData("状态 = '完成'")]                  // 中文条件
    [InlineData("创建时间 IS NOT NULL")]           // 中文カラム名条件
    public void EnsureExpression_ValidExpression_DoesNotThrow(string? value)
    {
        // 正当な SQL 式は例外なしで通過する
        var ex = Record.Exception(() => SqlSafetyGuard.EnsureExpression(value, "test"));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("Status = 'x'; DROP TABLE orders--")]  // セミコロン + DROP
    [InlineData("1=1; DELETE FROM users")]              // DELETE
    [InlineData("x UNION SELECT password FROM users")]  // UNION
    [InlineData("1=1 /* comment */")]                   // ブロックコメント
    [InlineData("col -- inline comment")]               // インラインコメント
    [InlineData("EXEC sp_executesql N'drop'")]          // EXEC
    [InlineData("1=1 UNION ALL SELECT")]                // UNION ALL
    [InlineData("col INSERT INTO hack")]                // INSERT
    [InlineData("UPDATE users SET admin=1")]            // UPDATE
    [InlineData("col DECLARE @x NVARCHAR")]             // DECLARE
    public void EnsureExpression_DangerousExpression_Throws(string value)
    {
        // SQL インジェクションパターンは InvalidOperationException を投げる
        Assert.Throws<InvalidOperationException>(
            () => SqlSafetyGuard.EnsureExpression(value, "test"));
    }

    [Fact]
    public void EnsureExpression_ContainsForbiddenCharacter_Throws()
    {
        // ExpressionRegex に含まれない文字（バックスラッシュ等）は拒否される
        Assert.Throws<InvalidOperationException>(
            () => SqlSafetyGuard.EnsureExpression(@"col\nORDER BY", "test"));
    }

    // ── IsUnsafeToken ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(";")]
    [InlineData("--")]
    [InlineData("/*")]
    [InlineData("*/")]
    public void IsUnsafeToken_KnownMarkers_ReturnsTrue(string token)
    {
        Assert.True(SqlSafetyGuard.IsUnsafeToken(token));
    }

    [Fact]
    public void IsUnsafeToken_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(SqlSafetyGuard.IsUnsafeToken(null));
        Assert.False(SqlSafetyGuard.IsUnsafeToken(""));
    }

    [Fact]
    public void IsUnsafeToken_SafeValue_ReturnsFalse()
    {
        Assert.False(SqlSafetyGuard.IsUnsafeToken("status = 'active'"));
    }

    // ── IsValidIdentifier ────────────────────────────────────────────────────

    [Fact]
    public void IsValidIdentifier_ValidName_ReturnsTrue()
    {
        Assert.True(SqlSafetyGuard.IsValidIdentifier("invoice_date"));
        Assert.True(SqlSafetyGuard.IsValidIdentifier("_col"));
        Assert.True(SqlSafetyGuard.IsValidIdentifier("A1"));
        Assert.True(SqlSafetyGuard.IsValidIdentifier("类型"));
        Assert.True(SqlSafetyGuard.IsValidIdentifier("照片_ID"));
    }

    [Fact]
    public void IsValidIdentifier_InvalidName_ReturnsFalse()
    {
        Assert.False(SqlSafetyGuard.IsValidIdentifier("1start"));
        Assert.False(SqlSafetyGuard.IsValidIdentifier("col-name"));
        Assert.False(SqlSafetyGuard.IsValidIdentifier(""));
        Assert.False(SqlSafetyGuard.IsValidIdentifier("  "));
    }
}
