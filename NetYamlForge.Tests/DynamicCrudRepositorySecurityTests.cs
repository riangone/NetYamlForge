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

public class DynamicCrudRepositorySecurityTests : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly Mock<IEntityMetadataProvider> _metaMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<IUserAuthService> _userAuthServiceMock;
    private readonly DefaultHttpContext _httpContext;

    public DynamicCrudRepositorySecurityTests()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        _db.Open();

        // 创建测试用的表
        _db.Execute(@"
            CREATE TABLE test_entity (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                email TEXT,
                ssn TEXT,
                normal_field TEXT
            );
        ");

        _metaMock = new Mock<IEntityMetadataProvider>();
        _userAuthServiceMock = new Mock<IUserAuthService>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();

        _httpContext = new DefaultHttpContext();
        _httpContextAccessorMock.Setup(h => h.HttpContext).Returns(_httpContext);
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
    }

    private EntityDefinition CreateMockEntityDefinition()
    {
        var columns = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            { "id", new ColumnDefinition { Type = "int", Identity = true } },
            { "email", new ColumnDefinition 
                { 
                    Type = "string", 
                    Security = new FieldSecurityDefinition 
                    { 
                        ReadMask = "email",
                        WriteRoles = new List<string> { "Manager", "Admin" }
                    } 
                } 
            },
            { "ssn", new ColumnDefinition 
                { 
                    Type = "string", 
                    Security = new FieldSecurityDefinition 
                    { 
                        ReadRoles = new List<string> { "Admin" },
                        WriteRoles = new List<string> { "Admin" }
                    } 
                } 
            },
            { "normal_field", new ColumnDefinition { Type = "string" } }
        };

        var forms = new Dictionary<string, FormDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            { "id", new FormDefinition { Type = "int", Identity = true } },
            { "email", new FormDefinition { Type = "string", Security = columns["email"].Security } },
            { "ssn", new FormDefinition { Type = "string", Security = columns["ssn"].Security } },
            { "normal_field", new FormDefinition { Type = "string" } }
        };

        return new EntityDefinition
        {
            Table = "test_entity",
            Key = "id",
            Columns = columns,
            Forms = forms,
            Security = new SecurityDefinition
            {
                Permissions = new PermissionsDefinition
                {
                    Read = new List<string> { "User", "Manager", "Admin" },
                    Write = new List<string> { "User", "Manager", "Admin" },
                    Delete = new List<string> { "Admin" }
                }
            }
        };
    }

    [Fact]
    public async Task InsertAsync_Succeeds_WhenUserHasFieldWriteRoles()
    {
        // Arrange
        SetupUser("manager_user", new[] { "Manager" });
        var entityDef = CreateMockEntityDefinition();
        _metaMock.Setup(m => m.Get("test_entity")).Returns(entityDef);

        var repo = new DynamicCrudRepository(
            _db,
            _metaMock.Object,
            new SqliteDialect(),
            NullLogger<DynamicCrudRepository>.Instance,
            _httpContextAccessorMock.Object,
            bizLogicRegistry: null,
            userAuthService: _userAuthServiceMock.Object
        );

        var values = new Dictionary<string, object?>
        {
            { "email", "manager@test.com" },
            { "normal_field", "value" }
        };

        // Act & Assert (should not throw write permission exception)
        var id = await repo.InsertAsync("test_entity", values);
        Assert.True(id > 0);

        var inserted = (await _db.QueryAsync<dynamic>("SELECT * FROM test_entity WHERE id = @Id", new { Id = id })).FirstOrDefault();
        Assert.NotNull(inserted);
    }

    [Fact]
    public async Task InsertAsync_ThrowsUnauthorizedAccessException_WhenUserLacksFieldWriteRoles()
    {
        // Arrange
        SetupUser("normal_user", new[] { "User" });
        var entityDef = CreateMockEntityDefinition();
        _metaMock.Setup(m => m.Get("test_entity")).Returns(entityDef);

        var repo = new DynamicCrudRepository(
            _db,
            _metaMock.Object,
            new SqliteDialect(),
            NullLogger<DynamicCrudRepository>.Instance,
            _httpContextAccessorMock.Object,
            bizLogicRegistry: null,
            userAuthService: _userAuthServiceMock.Object
        );

        var values = new Dictionary<string, object?>
        {
            { "email", "normal@test.com" }, // Requires Manager
            { "normal_field", "value" }
        };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repo.InsertAsync("test_entity", values));
    }

    [Fact]
    public async Task UpdateAsync_ThrowsUnauthorizedAccessException_WhenUserLacksFieldWriteRoles()
    {
        // Arrange
        SetupUser("manager_user", new[] { "Manager" }); // Manager has write roles for email, but NOT ssn
        var entityDef = CreateMockEntityDefinition();
        _metaMock.Setup(m => m.Get("test_entity")).Returns(entityDef);

        var repo = new DynamicCrudRepository(
            _db,
            _metaMock.Object,
            new SqliteDialect(),
            NullLogger<DynamicCrudRepository>.Instance,
            _httpContextAccessorMock.Object,
            bizLogicRegistry: null,
            userAuthService: _userAuthServiceMock.Object
        );

        var values = new Dictionary<string, object?>
        {
            { "ssn", "123-456-789" }, // Requires Admin
            { "normal_field", "updated" }
        };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repo.UpdateAsync("test_entity", 1, values));
    }

    [Fact]
    public async Task UpdateAsync_Succeeds_WhenAdminHasAllRoles()
    {
        // Arrange
        SetupUser("admin_user", new[] { "Admin" }, isAdmin: true);
        var entityDef = CreateMockEntityDefinition();
        _metaMock.Setup(m => m.Get("test_entity")).Returns(entityDef);

        // Pre-insert
        await _db.ExecuteAsync("INSERT INTO test_entity (id, email, ssn, normal_field) VALUES (1, 'old@email.com', 'ssn-old', 'old')");

        var repo = new DynamicCrudRepository(
            _db,
            _metaMock.Object,
            new SqliteDialect(),
            NullLogger<DynamicCrudRepository>.Instance,
            _httpContextAccessorMock.Object,
            bizLogicRegistry: null,
            userAuthService: _userAuthServiceMock.Object
        );

        var values = new Dictionary<string, object?>
        {
            { "email", "admin@email.com" },
            { "ssn", "ssn-new" },
            { "normal_field", "new-val" }
        };

        // Act
        var count = await repo.UpdateAsync("test_entity", 1, values);

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetByIdAsync_MasksAndRemovesFieldsBasedOnReadPermissions()
    {
        // Arrange
        SetupUser("manager_user", new[] { "Manager" }); // Manager has read permission for email but lacks ssn
        var entityDef = CreateMockEntityDefinition();
        _metaMock.Setup(m => m.Get("test_entity")).Returns(entityDef);

        // Pre-insert
        await _db.ExecuteAsync("INSERT INTO test_entity (id, email, ssn, normal_field) VALUES (42, 'secret@email.com', '123-45-6789', 'visible')");

        var repo = new DynamicCrudRepository(
            _db,
            _metaMock.Object,
            new SqliteDialect(),
            NullLogger<DynamicCrudRepository>.Instance,
            _httpContextAccessorMock.Object,
            bizLogicRegistry: null,
            userAuthService: _userAuthServiceMock.Object
        );

        // Act
        var result = await repo.GetByIdAsync("test_entity", 42);

        // Assert
        Assert.NotNull(result);
        var dict = result as IDictionary<string, object>;
        Assert.NotNull(dict);

        // ssn requires Admin, so it should be removed from result
        Assert.False(dict.ContainsKey("ssn"));

        // email should be masked
        Assert.True(dict.ContainsKey("email"));
        Assert.Equal("s****t@email.com", dict["email"]);

        // normal_field should be normal
        Assert.Equal("visible", dict["normal_field"]);
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}
