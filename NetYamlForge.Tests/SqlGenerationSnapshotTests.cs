// 目的: DynamicCrudRepository の SQL 生成ロジックの回帰テスト。
//       YAML定義の変更が意図しないSQL変更を引き起こしていないかを検証する。
//       in-memory SQLite でクエリ結果を検証することで、生成SQL の正確性を確認する。

using System.Data;
using Dapper;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Dialect;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NetYamlForge.Tests;

/// <summary>
/// DynamicCrudRepository の SQL 生成が各種 EntityDefinition 設定に対して
/// 正しく動作することを保証する回帰テスト。
/// 新しい YAML 機能（join, softDelete, filter 等）を追加した際はここにテストを追加する。
/// </summary>
public class SqlGenerationSnapshotTests
{
    // ─────────────────────────────────────────────────────────────
    // ヘルパー
    // ─────────────────────────────────────────────────────────────

    private static DynamicCrudRepository CreateSut(
        IDbConnection conn,
        IEntityMetadataProvider meta)
    {
        var rls = new DynamicCrudRowLevelSecurity(
            null, null, null, null, null, conn, NullLogger<DynamicCrudRowLevelSecurity>.Instance);
        return new(conn, meta, new SqliteDialect(), NullLogger<DynamicCrudRepository>.Instance, rls);
    }

    private static StubMetadataProvider MetaOf(string name, EntityDefinition def)
        => new(name, def);

    // ─────────────────────────────────────────────────────────────
    // SoftDelete
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_SoftDeleteTrue_ExcludesDeletedRows()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE product (
                id INTEGER PRIMARY KEY,
                name TEXT,
                IsDeleted INTEGER NOT NULL DEFAULT 0
            )
            """);
        await conn.ExecuteAsync("""
            INSERT INTO product (id, name, IsDeleted) VALUES
                (1, 'Active Product', 0),
                (2, 'Deleted Product', 1)
            """);

        var def = new EntityDefinition
        {
            Table = "product",
            Key = "id",
            DisplayName = "Products",
            SoftDelete = true,
            Columns = new() { ["name"] = new() { Type = "string", Searchable = true } }
        };

        var sut = CreateSut(conn, MetaOf("Product", def));

        var rows = (await sut.GetAllAsync("Product", null, null, null)).ToList();

        Assert.Single(rows);
        Assert.Equal("Active Product", (string)((IDictionary<string, object>)rows[0])["name"]);
    }

    [Fact]
    public async Task GetAllAsync_SoftDeleteFalse_IncludesAllRows()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE product (
                id INTEGER PRIMARY KEY,
                name TEXT,
                IsDeleted INTEGER NOT NULL DEFAULT 0
            )
            """);
        await conn.ExecuteAsync("""
            INSERT INTO product (id, name, IsDeleted) VALUES
                (1, 'Active', 0),
                (2, 'Deleted', 1)
            """);

        var def = new EntityDefinition
        {
            Table = "product",
            Key = "id",
            DisplayName = "Products",
            SoftDelete = false,
            Columns = new() { ["name"] = new() { Type = "string" } }
        };

        var sut = CreateSut(conn, MetaOf("Product", def));

        var rows = (await sut.GetAllAsync("Product", null, null, null)).ToList();

        Assert.Equal(2, rows.Count);
    }

    // ─────────────────────────────────────────────────────────────
    // 検索（Search）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_SearchTerm_FiltersSearchableColumns()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE customer (id INTEGER PRIMARY KEY, name TEXT, email TEXT)
            """);
        await conn.ExecuteAsync("""
            INSERT INTO customer VALUES (1, 'Alice', 'alice@example.com'),
                                        (2, 'Bob',   'bob@example.com'),
                                        (3, 'Carol', 'carol@example.com')
            """);

        var def = new EntityDefinition
        {
            Table = "customer",
            Key = "id",
            DisplayName = "Customers",
            Columns = new()
            {
                ["name"]  = new() { Type = "string", Searchable = true },
                ["email"] = new() { Type = "string", Searchable = false }  // 検索対象外
            }
        };

        var sut = CreateSut(conn, MetaOf("Customer", def));

        var rows = (await sut.GetAllAsync("Customer", "Alice", null, null)).ToList();

        Assert.Single(rows);
        Assert.Equal("Alice", (string)((IDictionary<string, object>)rows[0])["name"]);
    }

    [Fact]
    public async Task GetAllAsync_SearchTerm_IsNotSearchable_ReturnsAll()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE customer (id INTEGER PRIMARY KEY, name TEXT)
            """);
        await conn.ExecuteAsync("""
            INSERT INTO customer VALUES (1, 'Alice'), (2, 'Bob')
            """);

        var def = new EntityDefinition
        {
            Table = "customer",
            Key = "id",
            DisplayName = "Customers",
            Columns = new()
            {
                ["name"] = new() { Type = "string", Searchable = false }  // 検索対象なし
            }
        };

        var sut = CreateSut(conn, MetaOf("Customer", def));

        // searchable な列がないので検索は効かず、全件返す
        var rows = (await sut.GetAllAsync("Customer", "Alice", null, null)).ToList();

        Assert.Equal(2, rows.Count);
    }

    // ─────────────────────────────────────────────────────────────
    // フィルター
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_FilterApplied_NarrowsResults()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE orders (id INTEGER PRIMARY KEY, status TEXT, amount REAL)
            """);
        await conn.ExecuteAsync("""
            INSERT INTO orders VALUES (1, 'Open', 100), (2, 'Closed', 200), (3, 'Open', 300)
            """);

        var def = new EntityDefinition
        {
            Table = "orders",
            Key = "id",
            DisplayName = "Orders",
            Columns = new()
            {
                ["status"] = new() { Type = "string" },
                ["amount"] = new() { Type = "decimal" }
            },
            Filters = new()
            {
                ["status"] = new FilterDefinition { Type = "dropdown" }
            }
        };

        var sut = CreateSut(conn, MetaOf("Orders", def));

        var rows = (await sut.GetAllAsync(
            "Orders", null, null, null,
            filters: new() { ["status"] = "Open" }
        )).ToList();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
            Assert.Equal("Open", (string)((IDictionary<string, object>)r)["status"]));
    }

    [Fact]
    public async Task GetAllAsync_DateRangeFilter_IncludesBoundaries()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("CREATE TABLE session (id INTEGER PRIMARY KEY, CreatedAt TEXT)");
        await conn.ExecuteAsync("INSERT INTO session VALUES (1, '2023-10-27'), (2, '2023-10-28')");

        var def = new EntityDefinition
        {
            Table = "session",
            Key = "id",
            DisplayName = "Sessions",
            Columns = new() { ["CreatedAt"] = new() { Type = "date" } },
            Filters = new() { ["CreatedAt"] = new FilterDefinition { Type = "date-range" } }
        };

        var sut = CreateSut(conn, MetaOf("Session", def));

        // Filter from 2023-10-27 to 2023-10-27 (inclusive)
        var rows = (await sut.GetAllAsync(
            "Session", null, null, null,
            filters: new() { ["CreatedAt_from"] = "2023-10-27", ["CreatedAt_to"] = "2023-10-27" }
        )).ToList();

        Assert.Single(rows);
        Assert.Equal("2023-10-27", (string)((IDictionary<string, object>)rows[0])["CreatedAt"]);
    }

    // ─────────────────────────────────────────────────────────────
    // ソート
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_SortAsc_ReturnsRowsInAscendingOrder()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE product (id INTEGER PRIMARY KEY, price REAL)
            """);
        await conn.ExecuteAsync("""
            INSERT INTO product VALUES (1, 300), (2, 100), (3, 200)
            """);

        var def = new EntityDefinition
        {
            Table = "product",
            Key = "id",
            DisplayName = "Products",
            Columns = new()
            {
                ["price"] = new() { Type = "decimal", Sortable = true }
            }
        };

        var sut = CreateSut(conn, MetaOf("Product", def));

        var rows = (await sut.GetAllAsync("Product", null, "price", "asc")).ToList();

        var prices = rows
            .Select(r => Convert.ToDecimal(((IDictionary<string, object>)r)["price"]))
            .ToList();
        Assert.Equal(prices.OrderBy(x => x).ToList(), prices);
    }

    [Fact]
    public async Task GetAllAsync_SortDesc_ReturnsRowsInDescendingOrder()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE product (id INTEGER PRIMARY KEY, price REAL)
            """);
        await conn.ExecuteAsync("""
            INSERT INTO product VALUES (1, 300), (2, 100), (3, 200)
            """);

        var def = new EntityDefinition
        {
            Table = "product",
            Key = "id",
            DisplayName = "Products",
            Columns = new()
            {
                ["price"] = new() { Type = "decimal", Sortable = true }
            }
        };

        var sut = CreateSut(conn, MetaOf("Product", def));

        var rows = (await sut.GetAllAsync("Product", null, "price", "desc")).ToList();

        var prices = rows
            .Select(r => Convert.ToDecimal(((IDictionary<string, object>)r)["price"]))
            .ToList();
        Assert.Equal(prices.OrderByDescending(x => x).ToList(), prices);
    }

    // ─────────────────────────────────────────────────────────────
    // CRUD: Insert / GetById / Update / Delete
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task InsertAsync_ThenGetById_ReturnsInsertedRow()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE customer (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT, email TEXT)
            """);

        var def = new EntityDefinition
        {
            Table = "customer",
            Key = "id",
            DisplayName = "Customers",
            Columns = new()
            {
                ["name"]  = new() { Type = "string" },
                ["email"] = new() { Type = "string" }
            }
        };

        var sut = CreateSut(conn, MetaOf("Customer", def));

        await sut.InsertAsync("Customer", new Dictionary<string, object?>
        {
            ["name"]  = "Alice",
            ["email"] = "alice@example.com"
        });

        var row = (IDictionary<string, object>?)await sut.GetByIdAsync("Customer", 1L);

        Assert.NotNull(row);
        Assert.Equal("Alice", (string)row["name"]);
        Assert.Equal("alice@example.com", (string)row["email"]);
    }

    [Fact]
    public async Task UpdateAsync_ChangesFieldValue()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE customer (id INTEGER PRIMARY KEY, name TEXT)
            """);
        await conn.ExecuteAsync("INSERT INTO customer VALUES (1, 'Alice')");

        var def = new EntityDefinition
        {
            Table = "customer",
            Key = "id",
            DisplayName = "Customers",
            // UpdateAsync は Forms を使ってフィールドを特定する。Columns は一覧表示用。
            Forms = new()
            {
                ["id"]   = new() { Type = "int", Identity = true },
                ["name"] = new() { Type = "string", Editable = true }
            },
            Columns = new() { ["name"] = new() { Type = "string" } }
        };

        var sut = CreateSut(conn, MetaOf("Customer", def));

        await sut.UpdateAsync("Customer", 1L, new Dictionary<string, object?> { ["name"] = "Alice Updated" });

        var row = (IDictionary<string, object>?)await sut.GetByIdAsync("Customer", 1L);
        Assert.NotNull(row);
        Assert.Equal("Alice Updated", (string)row["name"]);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeleteFalse_PhysicallyRemovesRow()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE customer (id INTEGER PRIMARY KEY, name TEXT)
            """);
        await conn.ExecuteAsync("INSERT INTO customer VALUES (1, 'Alice')");

        var def = new EntityDefinition
        {
            Table = "customer",
            Key = "id",
            DisplayName = "Customers",
            SoftDelete = false,
            Columns = new() { ["name"] = new() { Type = "string" } }
        };

        var sut = CreateSut(conn, MetaOf("Customer", def));

        await sut.DeleteAsync("Customer", 1L);

        var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM customer WHERE id = 1");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeleteTrue_SetsIsDeletedFlag()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE customer (
                id INTEGER PRIMARY KEY,
                name TEXT,
                IsDeleted INTEGER NOT NULL DEFAULT 0
            )
            """);
        await conn.ExecuteAsync("INSERT INTO customer VALUES (1, 'Alice', 0)");

        var def = new EntityDefinition
        {
            Table = "customer",
            Key = "id",
            DisplayName = "Customers",
            SoftDelete = true,
            Columns = new() { ["name"] = new() { Type = "string" } }
        };

        var sut = CreateSut(conn, MetaOf("Customer", def));

        await sut.DeleteAsync("Customer", 1L);

        var isDeleted = await conn.ExecuteScalarAsync<int>(
            "SELECT IsDeleted FROM customer WHERE id = 1");
        Assert.Equal(1, isDeleted);
    }

    // ─────────────────────────────────────────────────────────────
    // ページング
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_Page2_ReturnsCorrectSlice()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE product (id INTEGER PRIMARY KEY, name TEXT)
            """);
        for (var i = 1; i <= 10; i++)
            await conn.ExecuteAsync("INSERT INTO product VALUES (@id, @name)", new { id = i, name = $"Product{i:D2}" });

        var def = new EntityDefinition
        {
            Table = "product",
            Key = "id",
            DisplayName = "Products",
            Paging = new() { PageSize = 3 },
            // id を Columns に含めないと SELECT に id が入らない
            Columns = new()
            {
                ["id"]   = new() { Type = "int", Identity = true, Sortable = true },
                ["name"] = new() { Type = "string" }
            }
        };

        var sut = CreateSut(conn, MetaOf("Product", def));

        var page2 = (await sut.GetAllAsync("Product", null, "id", "asc", page: 2, pageSize: 3)).ToList();

        Assert.Equal(3, page2.Count);
        // id 4, 5, 6 が返る
        var ids = page2.Select(r => Convert.ToInt64(((IDictionary<string, object>)r)["id"])).ToList();
        Assert.Equal(new long[] { 4, 5, 6 }, ids);
    }

    // ─────────────────────────────────────────────────────────────
    // CountAsync
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CountAsync_SoftDeleteTrue_CountsOnlyNonDeleted()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE product (
                id INTEGER PRIMARY KEY,
                name TEXT,
                IsDeleted INTEGER NOT NULL DEFAULT 0
            )
            """);
        await conn.ExecuteAsync("""
            INSERT INTO product VALUES (1, 'A', 0), (2, 'B', 1), (3, 'C', 0)
            """);

        var def = new EntityDefinition
        {
            Table = "product",
            Key = "id",
            DisplayName = "Products",
            SoftDelete = true,
            Columns = new() { ["name"] = new() { Type = "string", Searchable = true } }
        };

        var sut = CreateSut(conn, MetaOf("Product", def));

        var count = await sut.CountAsync("Product", null);

        Assert.Equal(2, count);
    }

    // ─────────────────────────────────────────────────────────────
    // Expression列（JOIN仮想列）の除外テスト
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task InsertAsync_WithExpressionColumn_ExcludesComputedField()
    {
        // Arrange: product + category テーブル（category_name は JOIN 仮想列）
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE category (id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE product (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                name        TEXT,
                category_id INTEGER
            );
            INSERT INTO category VALUES (1, '電子機器');
            """);

        var def = new EntityDefinition
        {
            Table = "product",
            Key = "id",
            DisplayName = "Products",
            Columns = new()
            {
                ["name"]          = new() { Type = "string" },
                ["category_id"]   = new() { Type = "int" },
                // expression 列: 実テーブルには存在しない
                ["category_name"] = new() { Type = "string", Expression = "cat.name" }
            }
        };

        var sut = CreateSut(conn, MetaOf("Product", def));

        // Act: expression 列を含む values を渡しても SQLite Error にならないこと
        var ex = await Record.ExceptionAsync(() =>
            sut.InsertAsync("Product", new Dictionary<string, object?>
            {
                ["name"]          = "テスト商品",
                ["category_id"]   = 1,
                ["category_name"] = "電子機器"   // 実列に存在しない → 除外されるべき
            }));

        Assert.Null(ex);

        var row = (IDictionary<string, object>?)await sut.GetByIdAsync("Product", 1L);
        Assert.NotNull(row);
        Assert.Equal("テスト商品", (string)row["name"]);
    }

    [Fact]
    public async Task GetAllAsync_WithJoinExpression_ReturnsVirtualColumn()
    {
        // Arrange: product + category、category_name は expression で SELECT
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE category (id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE product (
                id          INTEGER PRIMARY KEY,
                name        TEXT,
                category_id INTEGER
            );
            INSERT INTO category VALUES (1, '電子機器');
            INSERT INTO product VALUES (1, 'スマホ', 1);
            """);

        var def = new EntityDefinition
        {
            Table = "product",
            Key = "id",
            DisplayName = "Products",
            Columns = new()
            {
                ["name"]          = new() { Type = "string" },
                ["category_id"]   = new() { Type = "int" },
                ["category_name"] = new() { Type = "string", Expression = "cat.name" }
            },
            Joins =
            [
                new() { Table = "category", Alias = "cat", On = "product.category_id = cat.id", Type = "left" }
            ]
        };

        var sut = CreateSut(conn, MetaOf("Product", def));

        // Act
        var rows = (await sut.GetAllAsync("Product", null, null, null, null, 1, 10)).ToList();

        // Assert: expression 列 category_name が取得できること
        Assert.Single(rows);
        var row = (IDictionary<string, object>)rows[0];
        Assert.Equal("スマホ", (string)row["name"]);
        Assert.Equal("電子機器", (string)row["category_name"]);
    }
}

// ─────────────────────────────────────────────────────────────
// テスト専用スタブ実装
// ─────────────────────────────────────────────────────────────

internal sealed class StubMetadataProvider : IEntityMetadataProvider
{
    private readonly Dictionary<string, EntityDefinition> _defs;

    public StubMetadataProvider(string name, EntityDefinition def)
    {
        _defs = new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [name] = def
        };
    }

    public EntityDefinition Get(string entityName)
        => _defs.TryGetValue(entityName, out var def)
            ? def
            : throw new InvalidOperationException($"Entity '{entityName}' not found in stub.");

    public IReadOnlyDictionary<string, EntityDefinition> GetAll() => _defs;

    public bool TryGet(string entityName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out EntityDefinition? definition)
        => _defs.TryGetValue(entityName, out definition);
}
