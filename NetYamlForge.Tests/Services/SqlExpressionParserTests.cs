// ファイル概要: SqlExpressionParser のホワイトリスト検証テスト。

using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests.Services;

public class SqlExpressionParserTests
{
    private static void ShouldPass(string expression)
    {
        SqlExpressionParser.Validate(expression, "test");
    }

    private static void ShouldFail(string expression)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SqlExpressionParser.Validate(expression, "test"));
        Assert.Contains("test", ex.Message);
    }

    // ===== 正常系（受け入れ） =====

    [Theory]
    [InlineData("status = 'active'")]
    [InlineData("note = 'DELETE ME'")]
    [InlineData("price > 100 AND (stock <= 5 OR discontinued = 1)")]
    [InlineData("name LIKE '%山田%'")]
    [InlineData("created_at BETWEEN '2026-01-01' AND '2026-12-31'")]
    [InlineData("STRFTIME('%Y-%m', created_at)")]
    [InlineData("COALESCE(nickname, name)")]
    [InlineData("o.total * 1.1")]
    [InlineData("category IN ('a','b','c')")]
    [InlineData("deleted_at IS NULL")]
    [InlineData("CAST(qty AS INTEGER) > 0")]
    [InlineData("Sentiment = '积极' OR Sentiment = 'Positive'")]
    [InlineData("Sentiment IS NOT NULL AND Sentiment != ''")]
    [InlineData("MovementType='in'")]
    [InlineData("Status = 'completed' AND DocumentType IS NOT NULL AND DocumentType != ''")]
    [InlineData("UnitsInStock > 0 AND Discontinued = 0")]
    [InlineData("UnitsInStock <= ReorderLevel AND Discontinued = 0")]
    [InlineData("IsActive=1")]
    [InlineData("Status='published'")]
    [InlineData("Status NOT IN ('won','lost')")]
    [InlineData("date(preferred_date)=date('now')")]
    [InlineData("contract_status = 'AC' AND is_active = 1")]
    [InlineData("docstatus IN ('CO','IP') OR docstatus IS NULL")]
    [InlineData("p.ImageThumbnailBase64")]
    [InlineData("date(CreatedAt)")]
    [InlineData("a + b")]
    [InlineData("a || b")]
    [InlineData("a - b")]
    [InlineData("a / b")]
    [InlineData("a % b")]
    [InlineData("NOT flag = 1")]
    [InlineData("length(name) > 0")]
    [InlineData("UPPER(status) = 'ACTIVE'")]
    [InlineData("LOWER(name) LIKE '%test%'")]
    [InlineData("TRIM(name) != ''")]
    [InlineData("SUBSTR(name, 1, 10)")]
    [InlineData("REPLACE(name, 'old', 'new')")]
    [InlineData("ABS(value) > 0")]
    [InlineData("ROUND(price, 2)")]
    [InlineData("NULLIF(a, b)")]
    [InlineData("IFNULL(a, 'default')")]
    [InlineData("MIN(price)")]
    [InlineData("MAX(price)")]
    [InlineData("SUM(amount)")]
    [InlineData("COUNT(*)")]
    [InlineData("AVG(price)")]
    [InlineData("CURRENT_TIMESTAMP")]
    [InlineData("CURRENT_DATE")]
    [InlineData("a = 1 OR b = 2 OR c = 3")]
    [InlineData("a AND b AND c")]
    [InlineData("(a = 1) AND (b = 2)")]
    [InlineData("CAST(qty AS REAL) > 0")]
    [InlineData("CAST(qty AS TEXT)")]
    [InlineData("CAST(qty AS NUMERIC)")]
    [InlineData("CAST(qty AS BLOB)")]
    public void Accept_ValidExpression(string expression)
    {
        ShouldPass(expression);
    }

    // ===== 拒否 =====

    [Theory]
    [InlineData("1=1; DROP TABLE users")]
    [InlineData("1=1 -- comment")]
    [InlineData("id IN (SELECT id FROM users)")]
    [InlineData("1 UNION SELECT password FROM users")]
    [InlineData("name = 'a' || (SELECT 1)")]
    [InlineData("EXEC('sp_test')")]
    [InlineData("()")]
    [InlineData("name = 'abc")]
    [InlineData("DROP TABLE users")]
    [InlineData("DELETE FROM users")]
    [InlineData("INSERT INTO users VALUES (1)")]
    [InlineData("UPDATE users SET x=1")]
    [InlineData("ALTER TABLE users ADD col int")]
    [InlineData("TRUNCATE TABLE users")]
    public void Reject_DangerousExpression(string expression)
    {
        ShouldFail(expression);
    }

    // ===== エラーメッセージにコンテキストと位置が含まれる =====

    [Fact]
    public void ErrorMessageContainsContextAndPosition()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SqlExpressionParser.Validate("1=1; DROP TABLE", "my_filter"));
        Assert.Contains("my_filter", ex.Message);
        Assert.Contains("position", ex.Message);
    }

    // ===== 空/空白は許可（null/empty は EnsureExpression で弾かれるが、Validate は呼ばれない） =====

    [Fact]
    public void EmptyExpression_ThrowsInvalidOperation()
    {
        ShouldFail("");
    }

    [Fact]
    public void WhitespaceOnly_ThrowsInvalidOperation()
    {
        ShouldFail("   ");
    }

    // ===== 既存の YAML 表現が全て通ること =====

    [Theory]
    [InlineData("Sentiment = '积极' OR Sentiment = 'Positive' OR MoodBefore = '喜悦'")]
    [InlineData("Sentiment = '平和' OR Sentiment = 'Neutral' OR MoodBefore = '平静'")]
    [InlineData("Sentiment = '消极' OR Sentiment = 'Negative' OR MoodBefore = '焦虑' OR MoodBefore = '忧郁' OR MoodBefore = '愤怒' OR MoodBefore = '疲惫'")]
    [InlineData("Sentiment IS NOT NULL AND Sentiment != ''")]
    [InlineData("MoodBefore IS NOT NULL AND MoodBefore != ''")]
    [InlineData("CreatedAt IS NOT NULL")]
    [InlineData("Status = 'completed'")]
    [InlineData("Status = 'processing'")]
    [InlineData("Status = 'pending'")]
    [InlineData("Status = 'failed'")]
    [InlineData("Status = 'completed' AND DocumentType IS NOT NULL AND DocumentType != ''")]
    [InlineData("MovementType='in'")]
    [InlineData("MovementType='out'")]
    [InlineData("IsActive=1")]
    [InlineData("IsAdmin=1")]
    [InlineData("Status='published'")]
    [InlineData("Status='draft'")]
    [InlineData("Status='archived'")]
    [InlineData("Status='pending'")]
    [InlineData("FeaturedFlag=1")]
    [InlineData("Status='active'")]
    [InlineData("Status='in_progress'")]
    [InlineData("Status='review'")]
    [InlineData("Status='done'")]
    [InlineData("Priority='urgent'")]
    [InlineData("docstatus IN ('CO','IP') OR docstatus IS NULL")]
    [InlineData("isactive='Y'")]
    [InlineData("ShippedDate IS NULL")]
    [InlineData("UnitsInStock > 0 AND Discontinued = 0")]
    [InlineData("UnitsInStock <= ReorderLevel AND Discontinued = 0")]
    [InlineData("strftime('%Y-%m', OrderDate) = strftime('%Y-%m', 'now')")]
    [InlineData("deleted_at IS NULL")]
    [InlineData("annotation_status='done' AND deleted_at IS NULL")]
    [InlineData("annotation_status='pending' AND deleted_at IS NULL")]
    [InlineData("status='failed'")]
    [InlineData("date(preferred_date)=date('now')")]
    [InlineData("status='pending'")]
    [InlineData("status='open'")]
    [InlineData("priority='urgent'")]
    [InlineData("status='available'")]
    [InlineData("status NOT IN ('won','lost')")]
    [InlineData("contract_status = 'AC' AND is_active = 1")]
    public void Accept_ExistingYamlExpressions(string expression)
    {
        ShouldPass(expression);
    }
}
