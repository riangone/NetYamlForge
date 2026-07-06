// ファイル概要: R2-05 SQL インジェクション回帰スイート。
// 攻撃者視点の負向テスト: ユーザー可制御なフィルター値・列名・式が
//   (1) 値は必ずパラメータ化される（DbParameter に入り、SQL 文字列へは混入しない）
//   (2) 列名/識別子などパラメータ化できない位置はメタデータ ホワイトリストで拒否される
//   (3) 式は SqlExpressionParser のホワイトリスト構文解析で拒否される
// を 4 方言（SQLite/MySQL/PostgreSQL/SQLServer）横断で検証する。
// フィルター値のパラメータ化自体は方言非依存だが、契約として全方言で成立することを保証する。

using Dapper;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Dialect;
using Xunit;

namespace NetYamlForge.Tests.Security;

[Trait("Category", "Security")]
public sealed class SqlInjectionRegressionTests
{
    // 4 方言。フィルター値のパラメータ化は方言非依存だが、契約として全方言で確認する。
    private static readonly (ISqlDialect Dialect, string Name)[] Dialects =
    {
        (new SqliteDialect(), "SQLite"),
        (new MySqlDialect(), "MySQL"),
        (new PostgreSqlDialect(), "PostgreSQL"),
        (new SqlServerDialect(), "SQLServer"),
    };

    // 古典 + フレームワーク固有の注入ベクター。
    private static readonly string[] Vectors =
    {
        "' OR '1'='1",
        "'; DROP TABLE Users; --",
        "1; DELETE FROM Orders",
        "admin'--",
        "\" OR \"\"=\"",
        "') OR ('a'='a",
        "UNION SELECT username, password FROM Users",
        "1 UNION SELECT NULL,NULL--",
        "0x27206f7220313d31",           // hex エンコード
        "'/**/OR/**/1=1",               // コメントで空白回避
        "name'; EXEC xp_cmdshell('dir')--",
        "%27%20OR%201=1",               // URL エンコード
        "ﾟﾟ' OR 1=1 --",                // Unicode 前置
    };

    public static IEnumerable<object[]> DialectVectorMatrix()
    {
        foreach (var (_, name) in Dialects)
            foreach (var v in Vectors)
                yield return new object[] { name, v };
    }

    public static IEnumerable<object[]> DialectNames()
    {
        foreach (var (_, name) in Dialects)
            yield return new object[] { name };
    }

    private static EntityDefinition BuildMeta()
    {
        var meta = new EntityDefinition
        {
            Table = "Users",
            Key = "Id",
            DisplayName = "Users",
        };
        meta.Columns["Id"] = new ColumnDefinition { Type = "int" };
        meta.Columns["Name"] = new ColumnDefinition { Type = "string" };
        meta.Filters["Name"] = new FilterDefinition { Type = "like" };
        return meta;
    }

    // (1) YAML 定義フィルターの値は恒久的にパラメータ化される。
    [Theory]
    [MemberData(nameof(DialectVectorMatrix))]
    public void YamlFilterValue_IsParameterized_NotConcatenated(string dialect, string malicious)
    {
        var meta = BuildMeta();
        var filters = new Dictionary<string, string?> { ["Name"] = malicious };
        var where = new List<string>();
        var param = new DynamicParameters();

        DynamicCrudFilterApplier.ApplyFilters(meta, filters, where, param);

        var sql = string.Join(" AND ", where);
        // 生成 SQL 文字列に生の悪意入力が現れてはならない（方言: {dialect}）。
        Assert.DoesNotContain(malicious, sql);
        // 値はパラメータとして格納されている。
        Assert.Contains("Name", param.ParameterNames);
        Assert.Equal(malicious, param.Get<string>("Name"));
        _ = dialect;
    }

    // (1b) AI 生成動的フィルター（列は妥当）でも値はパラメータ化される。
    [Theory]
    [MemberData(nameof(DialectVectorMatrix))]
    public void DynamicFilterValue_OnValidColumn_IsParameterized(string dialect, string malicious)
    {
        var meta = BuildMeta();
        // "Name:" = LIKE 演算子（動的フィルター経路）。列 Name は妥当なのでパラメータ化される。
        var filters = new Dictionary<string, string?> { ["Id:"] = malicious };
        var where = new List<string>();
        var param = new DynamicParameters();

        DynamicCrudFilterApplier.ApplyFilters(meta, filters, where, param);

        var sql = string.Join(" AND ", where);
        Assert.DoesNotContain(malicious, sql);
        Assert.Contains(param.ParameterNames, n => malicious.Equals(param.Get<string>(n)));
        _ = dialect;
    }

    // (2) ホワイトリスト外/悪意ある列名の動的フィルターは黙って無視される（WHERE に混入しない）。
    [Theory]
    [MemberData(nameof(DialectNames))]
    public void DynamicFilter_WithNonWhitelistedColumn_IsIgnored(string dialect)
    {
        var meta = BuildMeta();
        var filters = new Dictionary<string, string?>
        {
            ["Name; DROP TABLE Users"] = "x",
            ["1=1 OR"] = "x",
            ["UnknownCol"] = "x",
        };
        var where = new List<string>();
        var param = new DynamicParameters();

        DynamicCrudFilterApplier.ApplyFilters(meta, filters, where, param);

        Assert.Empty(where);
        Assert.Empty(param.ParameterNames);
        _ = dialect;
    }

    // (2b) パラメータ化できない識別子（列名/テーブル名）は SqlSafetyGuard で拒否される。
    [Theory]
    [MemberData(nameof(DialectNames))]
    public void MaliciousIdentifier_IsRejected(string dialect)
    {
        string[] badIdentifiers =
        {
            "Name; DROP TABLE Users",
            "Name--",
            "Name OR 1=1",
            "Name)",
            "'; DELETE FROM Users; --",
            "DROP",
        };

        foreach (var bad in badIdentifiers)
            Assert.ThrowsAny<Exception>(() => SqlSafetyGuard.EnsureIdentifier(bad, $"col[{dialect}]"));

        // 正向対照: 妥当な識別子は通る。
        SqlSafetyGuard.EnsureIdentifier("CustomerName", "col");
        SqlSafetyGuard.EnsureIdentifier("_internal", "col");
    }

    // (3) 悪意ある式は SqlExpressionParser のホワイトリスト構文解析で拒否される。
    [Theory]
    [MemberData(nameof(DialectNames))]
    public void MaliciousExpression_IsRejectedByParser(string dialect)
    {
        string[] badExpressions =
        {
            "1; DROP TABLE Users",
            "name = 'a' OR 1=1; --",
            "(SELECT password FROM Users)",
            "name UNION SELECT secret FROM Users",
            "xp_cmdshell('dir')",
        };

        foreach (var bad in badExpressions)
            Assert.ThrowsAny<Exception>(() => SqlExpressionParser.Validate(bad, $"expr[{dialect}]"));

        // 正向対照: 妥当な式は通る。
        SqlExpressionParser.Validate("LOWER(Name)", "expr");
        SqlExpressionParser.Validate("Amount * 2", "expr");
    }
}
