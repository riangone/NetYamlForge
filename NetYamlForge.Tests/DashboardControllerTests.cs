// ファイル概要: DashboardController の統合テストです。
// SQL 安全検証（EnsureIdentifier/EnsureExpression）が正しく機能し、
// 不正な設定値が HasError プレースホルダーとして扱われることを確認します。
// インメモリ SQLite を使用して実際のクエリ実行まで検証します。

using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Dapper;
using NetYamlForge.Controllers;
using NetYamlForge.Models;
using NetYamlForge.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NetYamlForge.Tests;

/// <summary>
/// DashboardController の統合テスト。
/// - 正常系: インメモリSQLiteでクエリが実行され Stats/Charts が正しく返る
/// - SQL 安全検証: 不正な識別子/式は HasError=true プレースホルダーとして処理される
/// </summary>
public class DashboardControllerTests
{
    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TestApp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        public string EnvironmentName { get; set; } = "Test";
    }

    // ── 正常系テスト ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Index_ReturnsStatWithValue_WhenValidCountConfig()
    {
        // Arrange: 1件のレコードを持つインメモリDBでCOUNT(*)を実行
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        await db.ExecuteAsync("CREATE TABLE invoice (Id INTEGER PRIMARY KEY, Status TEXT)");
        await db.ExecuteAsync("INSERT INTO invoice (Status) VALUES ('active'), ('inactive')");

        var config = new DashboardConfig
        {
            Stats = new List<DashboardStatDefinition>
            {
                new() { Entity = "invoice", Label = "総数", Aggregate = "count" }
            }
        };
        var controller = CreateController(db, config, new EntityMeta("invoice", "Id"));

        // Act
        var result = await controller.Index();

        // Assert
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<DashboardViewModel>(view.Model);
        Assert.Single(vm.Stats);
        Assert.Equal("2", vm.Stats[0].Value);    // 2件のCOUNT
        Assert.False(vm.Stats[0].HasError);
    }

    [Fact]
    public async Task Index_ReturnsStatWithFilter_WhenValidFilterConfig()
    {
        // Arrange: WHERE 句付きでフィルタリング
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        await db.ExecuteAsync("CREATE TABLE invoice (Id INTEGER PRIMARY KEY, Status TEXT)");
        await db.ExecuteAsync("INSERT INTO invoice (Status) VALUES ('active'), ('active'), ('closed')");

        var config = new DashboardConfig
        {
            Stats = new List<DashboardStatDefinition>
            {
                new() { Entity = "invoice", Label = "アクティブ", Aggregate = "count", Filter = "Status = 'active'" }
            }
        };
        var controller = CreateController(db, config, new EntityMeta("invoice", "Id"));

        // Act
        var result = await controller.Index();

        // Assert
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<DashboardViewModel>(view.Model);
        Assert.Single(vm.Stats);
        Assert.Equal("2", vm.Stats[0].Value);    // active 2件のみ
        Assert.False(vm.Stats[0].HasError);
    }

    [Fact]
    public async Task Index_ReturnsSumStat_WhenValidSumConfig()
    {
        // Arrange: SUM 集計テスト
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        await db.ExecuteAsync("CREATE TABLE invoice (Id INTEGER PRIMARY KEY, Total REAL)");
        await db.ExecuteAsync("INSERT INTO invoice (Total) VALUES (100.0), (200.0), (50.5)");

        var config = new DashboardConfig
        {
            Stats = new List<DashboardStatDefinition>
            {
                new() { Entity = "invoice", Label = "売上合計", Aggregate = "sum", Column = "Total" }
            }
        };
        var controller = CreateController(db, config, new EntityMeta("invoice", "Id"));

        // Act
        var result = await controller.Index();

        // Assert
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<DashboardViewModel>(view.Model);
        Assert.Single(vm.Stats);
        Assert.Contains("350", vm.Stats[0].Value);   // 350.50 の整形値
        Assert.False(vm.Stats[0].HasError);
    }

    // ── SQL 安全検証テスト（統計カード）─────────────────────────────────────

    [Theory]
    [InlineData("col; DROP TABLE invoice--")]   // セミコロン + DROP（識別子）
    [InlineData("1bad")]                         // 識別子として無効（先頭数字）
    [InlineData("col name")]                     // スペース入り識別子
    public async Task Index_StatColumn_InvalidIdentifier_ReturnsHasErrorPlaceholder(string badColumn)
    {
        // SUM/AVG 集計の Column に不正な識別子が指定された場合、HasError=true になる
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        await db.ExecuteAsync("CREATE TABLE invoice (Id INTEGER PRIMARY KEY, Total REAL)");

        var config = new DashboardConfig
        {
            Stats = new List<DashboardStatDefinition>
            {
                new() { Entity = "invoice", Label = "危険テスト", Aggregate = "sum", Column = badColumn }
            }
        };
        var controller = CreateController(db, config, new EntityMeta("invoice", "Id"));

        // Act
        var result = await controller.Index();

        // Assert: 安全検証が機能してエラープレースホルダーが返る
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<DashboardViewModel>(view.Model);
        Assert.Single(vm.Stats);
        Assert.True(vm.Stats[0].HasError);
        Assert.Equal("設定エラー", vm.Stats[0].ErrorHint);
    }

    [Theory]
    [InlineData("Status = 'x'; DROP TABLE invoice--")]   // DROP
    [InlineData("1=1 UNION SELECT password FROM users")] // UNION
    [InlineData("1=1 -- コメント")]                       // インラインコメント
    [InlineData("col EXEC sp_executesql")]                // EXEC
    public async Task Index_StatFilter_DangerousExpression_ReturnsHasErrorPlaceholder(string badFilter)
    {
        // Filter に危険な SQL 式が指定された場合、HasError=true になる
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        await db.ExecuteAsync("CREATE TABLE invoice (Id INTEGER PRIMARY KEY)");

        var config = new DashboardConfig
        {
            Stats = new List<DashboardStatDefinition>
            {
                new() { Entity = "invoice", Label = "フィルターテスト", Aggregate = "count", Filter = badFilter }
            }
        };
        var controller = CreateController(db, config, new EntityMeta("invoice", "Id"));

        // Act
        var result = await controller.Index();

        // Assert
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<DashboardViewModel>(view.Model);
        Assert.Single(vm.Stats);
        Assert.True(vm.Stats[0].HasError);
        Assert.Equal("設定エラー", vm.Stats[0].ErrorHint);
    }

    // ── SQL 安全検証テスト（グラフ）─────────────────────────────────────────

    [Theory]
    [InlineData("grp; DROP TABLE invoice")]              // DROP（GroupExpression）
    [InlineData("grp UNION SELECT password FROM users")] // UNION（GroupExpression）
    public async Task Index_ChartGroupExpression_Dangerous_ReturnsHasErrorPlaceholder(string badGroupExpr)
    {
        // GroupExpression に危険な SQL 式が指定された場合、HasError=true になる
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        await db.ExecuteAsync("CREATE TABLE invoice (Id INTEGER PRIMARY KEY, Status TEXT)");

        var config = new DashboardConfig
        {
            Charts = new List<DashboardChartDefinition>
            {
                new()
                {
                    Entity          = "invoice",
                    Title           = "テスト",
                    Type            = "bar",
                    ValueAggregate  = "count",
                    GroupExpression = badGroupExpr,
                    Limit           = 5
                }
            }
        };
        var controller = CreateController(db, config, new EntityMeta("invoice", "Id"));

        // Act
        var result = await controller.Index();

        // Assert
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<DashboardViewModel>(view.Model);
        Assert.Single(vm.Charts);
        Assert.True(vm.Charts[0].HasError);
        Assert.Equal("設定エラー", vm.Charts[0].ErrorHint);
    }

    [Theory]
    [InlineData("Status")]         // 単純カラム名
    [InlineData("strftime('%Y-%m', created_at)")]  // SQLite 日付関数
    public async Task Index_ChartGroupExpression_Valid_ReturnsData(string groupExpr)
    {
        // 正当な GroupExpression ではグラフデータが返る
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        await db.ExecuteAsync("CREATE TABLE invoice (Id INTEGER PRIMARY KEY, Status TEXT, created_at TEXT)");
        await db.ExecuteAsync("INSERT INTO invoice (Status, created_at) VALUES ('active', '2024-01-15'), ('active', '2024-02-10'), ('closed', '2024-02-20')");

        var config = new DashboardConfig
        {
            Charts = new List<DashboardChartDefinition>
            {
                new()
                {
                    Entity          = "invoice",
                    Title           = "グループテスト",
                    Type            = "bar",
                    ValueAggregate  = "count",
                    GroupExpression = groupExpr,
                    Limit           = 10
                }
            }
        };
        var controller = CreateController(db, config, new EntityMeta("invoice", "Id"));

        // Act
        var result = await controller.Index();

        // Assert
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<DashboardViewModel>(view.Model);
        Assert.Single(vm.Charts);
        Assert.False(vm.Charts[0].HasError);
        Assert.NotEqual("[]", vm.Charts[0].LabelsJson);  // データあり
    }

    // ── エラー混在テスト ─────────────────────────────────────────────────────

    [Fact]
    public async Task Index_MultipleStats_PartialError_ReturnsAllWithCorrectState()
    {
        // 正常な stat とエラーな stat が混在しても、両方が結果に含まれる
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        await db.ExecuteAsync("CREATE TABLE invoice (Id INTEGER PRIMARY KEY, Total REAL)");
        await db.ExecuteAsync("INSERT INTO invoice (Total) VALUES (100.0)");

        var config = new DashboardConfig
        {
            Stats = new List<DashboardStatDefinition>
            {
                new() { Entity = "invoice", Label = "正常カード", Aggregate = "count" },
                new() { Entity = "invoice", Label = "エラーカード", Aggregate = "sum", Column = "bad-col;DROP" }
            }
        };
        var controller = CreateController(db, config, new EntityMeta("invoice", "Id"));

        // Act
        var result = await controller.Index();

        // Assert: 2件返る（正常 + エラープレースホルダー）
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<DashboardViewModel>(view.Model);
        Assert.Equal(2, vm.Stats.Count);
        Assert.False(vm.Stats[0].HasError);    // 正常カード
        Assert.Equal("1", vm.Stats[0].Value);
        Assert.True(vm.Stats[1].HasError);     // エラーカード
        Assert.Equal("–", vm.Stats[1].Value);
    }

    // ── ヘルパーメソッド ──────────────────────────────────────────────────────

    private static DashboardController CreateController(
        IDbConnection db,
        DashboardConfig config,
        EntityMeta entityMeta)
    {
        var metaProvider = new SingleEntityMetadataProvider(entityMeta.EntityName, new EntityDefinition
        {
            Table = entityMeta.EntityName,
            Key = entityMeta.Key
        });

        var scope = new ProjectScope();
        SetProjectScope(scope, "p1", metaProvider);

        var configProvider = new StubDashboardConfigProvider(config);

        var controller = new DashboardController(
            configProvider,
            metaProvider,
            db,
            scope,
            NullLogger<DashboardController>.Instance,
            new TestWebHostEnvironment())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Url = new StubUrlHelper("/p1/Dashboard");
        return controller;
    }

    private static void SetProjectScope(ProjectScope scope, string projectName, IEntityMetadataProvider metaProvider)
    {
        var method = typeof(ProjectScope).GetMethod("Set", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(scope, new object[]
        {
            new ProjectInfo
            {
                Name = projectName,
                DisplayName = projectName,
                ProjectDir = ".",
                DatabaseType = "sqlite",
                ConnectionString = "Data Source=:memory:",
                EntityMetadata = metaProvider,
                DashboardConfig = new StubDashboardConfigProvider(new DashboardConfig()),
                PageMetadata = NullPageMetadataProvider.Instance
            }
        });
    }

    private sealed record EntityMeta(string EntityName, string Key);

    private sealed class SingleEntityMetadataProvider : IEntityMetadataProvider
    {
        private readonly string _name;
        private readonly EntityDefinition _def;

        public SingleEntityMetadataProvider(string name, EntityDefinition def)
        {
            _name = name;
            _def = def;
        }

        public EntityDefinition Get(string entityName) => _def;
        public IReadOnlyDictionary<string, EntityDefinition> GetAll() =>
            new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase) { [_name] = _def };

        public bool TryGet(string entityName, [NotNullWhen(true)] out EntityDefinition? definition)
        {
            if (string.Equals(entityName, _name, StringComparison.OrdinalIgnoreCase))
            {
                definition = _def;
                return true;
            }
            definition = null;
            return false;
        }
    }

    private sealed class StubDashboardConfigProvider : IDashboardConfigProvider
    {
        private readonly DashboardConfig _config;
        public StubDashboardConfigProvider(DashboardConfig config) { _config = config; }
        public DashboardConfig GetConfig() => _config;
    }

    private sealed class StubUrlHelper : IUrlHelper
    {
        private readonly string _actionUrl;
        public StubUrlHelper(string actionUrl) { _actionUrl = actionUrl; }

        public ActionContext ActionContext { get; } = new(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        public string? Action(UrlActionContext actionContext) => _actionUrl;
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => _actionUrl;
        public string? RouteUrl(UrlRouteContext routeContext) => _actionUrl;
    }
}
