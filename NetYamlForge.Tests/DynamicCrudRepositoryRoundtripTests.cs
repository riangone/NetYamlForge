#pragma warning disable DCS003
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.Dialect;
using Xunit;
using Dapper;

namespace NetYamlForge.Tests;

public class DynamicCrudRepositoryRoundtripTests : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly Mock<IEntityMetadataProvider> _metaMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<IUserAuthService> _userAuthServiceMock;
    private readonly DefaultHttpContext _httpContext;

    public DynamicCrudRepositoryRoundtripTests()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        _db.Open();

        // 创建 test_entity (产品) 和 categories 表
        _db.Execute(@"
            CREATE TABLE categories (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL
            );

            CREATE TABLE test_entity (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                category_id INTEGER,
                status TEXT,
                FOREIGN KEY(category_id) REFERENCES categories(id)
            );
        ");

        _metaMock = new Mock<IEntityMetadataProvider>();
        _userAuthServiceMock = new Mock<IUserAuthService>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();

        _httpContext = new DefaultHttpContext();
        _httpContextAccessorMock.Setup(h => h.HttpContext).Returns(_httpContext);

        SetupUser("admin_user", new[] { "Admin" }, isAdmin: true);
    }

    public void Dispose()
    {
        _db.Close();
        _db.Dispose();
    }

    private void SetupUser(string userName, string[] roles, bool isAdmin = false)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.Name, userName) };
        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        _httpContext.User = principal;

        _userAuthServiceMock.Setup(u => u.GetUserRolesAsync(userName)).ReturnsAsync(roles);
        // DynamicCrudRowLevelSecurity は projectName 付きの2引数オーバーロードを呼び出すため、
        // そちらも明示的に stub しないと Moq の緩いモードで null が返り ArgumentNullException になる。
        _userAuthServiceMock.Setup(u => u.GetUserRolesAsync(userName, It.IsAny<string?>())).ReturnsAsync(roles);
    }

    private DynamicCrudRepository CreateRepository()
    {
        var rls = new DynamicCrudRowLevelSecurity(
            _httpContextAccessorMock.Object,
            null,
            _userAuthServiceMock.Object,
            null,
            null,
            _db,
            NullLogger<DynamicCrudRowLevelSecurity>.Instance);
        return new DynamicCrudRepository(
            _db,
            _metaMock.Object,
            new SqliteDialect(),
            NullLogger<DynamicCrudRepository>.Instance,
            rls,
            _httpContextAccessorMock.Object,
            bizLogicRegistry: null,
            userAuthService: _userAuthServiceMock.Object
        );
    }

    private EntityDefinition CreateMockEntityDefinition(bool enableRls = false)
    {
        var columns = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            { "id", new ColumnDefinition { Type = "int", Identity = true } },
            { "name", new ColumnDefinition { Type = "string" } },
            { "category_id", new ColumnDefinition {
                Type = "int",
                ForeignKey = new ForeignKeyDefinition
                {
                    Entity = "category",
                    DisplayColumn = "name"
                }
            } },
            { "category_name", new ColumnDefinition {
                Type = "string",
                Expression = "categories.name"
            } },
            { "status", new ColumnDefinition { Type = "string" } }
        };

        var joins = new List<JoinDefinition>
        {
            new JoinDefinition
            {
                Table = "categories",
                Alias = "categories",
                Type = "left",
                On = "test_entity.category_id = categories.id"
            }
        };

        var security = new SecurityDefinition
        {
            Permissions = new PermissionsDefinition
            {
                Read = new List<string> { "Admin" },
                Write = new List<string> { "Admin" },
                Delete = new List<string> { "Admin" }
            }
        };

        if (enableRls)
        {
            security.RowLevelSecurity = new RowLevelSecurityDefinition
            {
                Enabled = true,
                Policies = new List<RowLevelSecurityPolicy>
                {
                    new RowLevelSecurityPolicy
                    {
                        Role = "Admin",
                        FilterClause = "test_entity.status = 'active'"
                    }
                }
            };
        }

        var forms = new Dictionary<string, FormDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            { "name", new FormDefinition { Type = "text" } },
            { "category_id", new FormDefinition { Type = "select" } },
            { "status", new FormDefinition { Type = "text" } }
        };

        return new EntityDefinition
        {
            Table = "test_entity",
            Key = "id",
            Columns = columns,
            Forms = forms,
            Joins = joins,
            Security = security
        };
    }

    [Fact]
    public async Task Insert_GetById_Update_Delete_Roundtrip_ShouldWork()
    {
        var entityDef = CreateMockEntityDefinition();
        _metaMock.Setup(m => m.Get("test_entity")).Returns(entityDef);
        _metaMock.Setup(m => m.TryGet("test_entity", out entityDef)).Returns(true);

        var repo = CreateRepository();

        // 1. Insert
        var values = new Dictionary<string, object?>
        {
            { "name", "Product A" },
            { "status", "active" }
        };

        var affected = await repo.InsertAsync("test_entity", values);
        Assert.Equal(1, affected);

        var lastId = await _db.ExecuteScalarAsync<int>("SELECT last_insert_rowid();");

        // 2. GetById
        var record = await repo.GetByIdAsync("test_entity", lastId);
        Assert.NotNull(record);
        
        var dictRecord = record as IDictionary<string, object>;
        Assert.NotNull(dictRecord);
        Assert.Equal("Product A", dictRecord["name"]?.ToString());
        Assert.Equal("active", dictRecord["status"]?.ToString());

        // 3. Update
        var updateValues = new Dictionary<string, object?>
        {
            { "name", "Product A Updated" },
            { "status", "inactive" }
        };

        var updateAffected = await repo.UpdateAsync("test_entity", lastId, updateValues);
        Assert.Equal(1, updateAffected);

        var recordUpdated = await repo.GetByIdAsync("test_entity", lastId);
        Assert.NotNull(recordUpdated);
        var dictRecordUpdated = recordUpdated as IDictionary<string, object>;
        Assert.NotNull(dictRecordUpdated);
        Assert.Equal("Product A Updated", dictRecordUpdated["name"]?.ToString());
        Assert.Equal("inactive", dictRecordUpdated["status"]?.ToString());

        // 4. Delete
        var deleteAffected = await repo.DeleteAsync("test_entity", lastId);
        Assert.Equal(1, deleteAffected);

        var recordDeleted = await repo.GetByIdAsync("test_entity", lastId);
        Assert.Null(recordDeleted);
    }

    [Fact]
    public async Task BulkDelete_MultipleDeletions_ShouldSucceed()
    {
        _db.Execute("INSERT INTO test_entity (name, status) VALUES ('P1', 'active');");
        _db.Execute("INSERT INTO test_entity (name, status) VALUES ('P2', 'active');");
        _db.Execute("INSERT INTO test_entity (name, status) VALUES ('P3', 'active');");

        var entityDef = CreateMockEntityDefinition();
        _metaMock.Setup(m => m.Get("test_entity")).Returns(entityDef);
        _metaMock.Setup(m => m.TryGet("test_entity", out entityDef)).Returns(true);

        var repo = CreateRepository();

        var ids = (await _db.QueryAsync<int>("SELECT id FROM test_entity ORDER BY id")).ToList();
        Assert.Equal(3, ids.Count);

        // 删除其中的 2 行
        var deletedCount = 0;
        deletedCount += await repo.DeleteAsync("test_entity", ids[0]);
        deletedCount += await repo.DeleteAsync("test_entity", ids[1]);

        Assert.Equal(2, deletedCount);

        var remainingCount = await _db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM test_entity;");
        Assert.Equal(1, remainingCount);
    }

    [Fact]
    public async Task ForeignKey_DisplayColumnProjection_ShouldWork()
    {
        // 插入 Category 记录
        _db.Execute("INSERT INTO categories (name) VALUES ('Category Alpha');");
        var categoryId = await _db.ExecuteScalarAsync<int>("SELECT last_insert_rowid();");

        // 插入包含 category_id 的产品记录
        _db.Execute("INSERT INTO test_entity (name, category_id, status) VALUES ('Product Beta', @categoryId, 'active');", new { categoryId });
        var productId = await _db.ExecuteScalarAsync<int>("SELECT last_insert_rowid();");

        var entityDef = CreateMockEntityDefinition();
        _metaMock.Setup(m => m.Get("test_entity")).Returns(entityDef);
        _metaMock.Setup(m => m.TryGet("test_entity", out entityDef)).Returns(true);

        var repo = CreateRepository();

        // 通过 GetAll 触发 SQL JOIN 检验是否有 category_name 字段
        var items = await repo.GetAllAsync("test_entity", search: null, sort: null, dir: null);
        var record = items.FirstOrDefault();

        Assert.NotNull(record);
        var dictRecord = record as IDictionary<string, object>;
        Assert.NotNull(dictRecord);
        Assert.Equal("Product Beta", dictRecord["name"]?.ToString());
        Assert.Equal("Category Alpha", dictRecord["category_name"]?.ToString());
    }

    [Fact]
    public async Task FilterExpression_ShouldFilterRowsCorrectly()
    {
        _db.Execute("INSERT INTO test_entity (name, status) VALUES ('P1', 'active');");
        _db.Execute("INSERT INTO test_entity (name, status) VALUES ('P2', 'inactive');");
        _db.Execute("INSERT INTO test_entity (name, status) VALUES ('P3', 'active');");

        // 启用 RLS 策略，让 status = 'active' 的行被匹配过滤
        var entityDef = CreateMockEntityDefinition(enableRls: true);
        _metaMock.Setup(m => m.Get("test_entity")).Returns(entityDef);
        _metaMock.Setup(m => m.TryGet("test_entity", out entityDef)).Returns(true);

        var repo = CreateRepository();

        var items = await repo.GetAllAsync("test_entity", search: null, sort: null, dir: null);
        var list = items.ToList();

        Assert.Equal(2, list.Count);
        foreach (var item in list)
        {
            var dict = item as IDictionary<string, object>;
            Assert.NotNull(dict);
            Assert.Equal("active", dict["status"]?.ToString());
        }
    }
}
