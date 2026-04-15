using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Moq;
using NetYamlForge.AI.Services;
using NetYamlForge.AI.Services.ToolValidation;
using Xunit;
using Xunit.Abstractions;

namespace NetYamlForge.Tests.Services.AI;

/// <summary>
/// 车辆查询与推荐自动化集成测试
/// 
/// 测试场景:
/// 1. 客户查询车辆信息
/// 2. 根据条件筛选车辆 (品牌/价格/类型)
/// 3. 车辆对比推荐
/// 4. Tool 调用验证 (query_data)
/// 
/// 纯内存测试，无需人工干预。
/// </summary>
public class VehicleSearchIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private readonly AppointmentStateMachine _fsm;
    private readonly ToolCallValidator _toolValidator;
    private readonly string _conversationId = "vehicle-search-001";
    private readonly string _projectId = "auto-dealer-demo";

    // 模拟车辆数据
    private class Vehicle
    {
        public string Id { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }
    }

    public VehicleSearchIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _fsm = new AppointmentStateMachine(_conversationId);
        var loggerMock = new Mock<ILogger<ToolCallValidator>>();
        _toolValidator = new ToolCallValidator(loggerMock.Object, new NetYamlForge.AI.Infrastructure.DefaultSqlSafetyGuard());
    }

    #region 测试场景 1: 基础车辆查询

    [Fact]
    public async Task BasicVehicleQuery_ShouldReturnVehicles()
    {
        _output.WriteLine("=== 开始基础车辆查询测试 ===");

        // 步骤 1: 客户发起查询
        _output.WriteLine("\n【步骤 1】客户查询车辆信息");
        Assert.Equal(AppointmentStateMachine.State.Init, _fsm.CurrentState);
        Assert.True(_fsm.IsToolAllowed("query_data"));
        _output.WriteLine($"  ✓ 初始状态: {_fsm.CurrentState}");
        _output.WriteLine($"  ✓ Tool 允许: query_data");

        // 步骤 2: 验证 Tool 调用
        _output.WriteLine("\n【步骤 2】验证 query_data Tool 调用");
        var toolCall = new JsonObject
        {
            ["tool_call"] = "query_data",
            ["entity"] = "vehicles",
            ["action"] = "list",
            ["filters"] = new JsonArray(),
            ["top"] = 10
        };

        var validationResult = await _toolValidator.ValidateAsync(toolCall, _projectId, _fsm.CurrentState);
        Assert.True(validationResult.IsValid);
        _output.WriteLine($"  ✓ Tool 验证通过");

        // 步骤 3: 模拟返回车辆列表
        _output.WriteLine("\n【步骤 3】返回车辆列表");
        var vehicles = GetMockVehicles();
        _output.WriteLine($"  ✓ 返回 {vehicles.Count} 辆车");
        foreach (var v in vehicles)
        {
            _output.WriteLine($"    - {v.Brand} {v.Model} ({v.Type}) - ¥{v.Price:N0}");
        }

        _output.WriteLine("\n=== 基础车辆查询测试通过 ✓ ===");
    }

    #endregion

    #region 测试场景 2: 条件筛选

    [Fact]
    public async Task VehicleFiltering_ShouldReturnMatchingVehicles()
    {
        _output.WriteLine("\n=== 开始条件筛选测试 ===");

        var allVehicles = GetMockVehicles();

        // 场景 1: 按品牌筛选
        _output.WriteLine("\n【场景 1】按品牌筛选 (Toyota)");
        var toyotaVehicles = allVehicles.Where(v => v.Brand == "Toyota").ToList();
        Assert.Equal(3, toyotaVehicles.Count);
        _output.WriteLine($"  ✓ 找到 {toyotaVehicles.Count} 辆 Toyota");

        // 场景 2: 按类型筛选
        _output.WriteLine("\n【场景 2】按类型筛选 (SUV)");
        var suvVehicles = allVehicles.Where(v => v.Type == "SUV").ToList();
        Assert.Equal(3, suvVehicles.Count);
        _output.WriteLine($"  ✓ 找到 {suvVehicles.Count} 辆 SUV");

        // 场景 3: 按价格区间筛选
        _output.WriteLine("\n【场景 3】按价格区间筛选 (300-400万)");
        var priceRangeVehicles = allVehicles
            .Where(v => v.Price >= 3000000 && v.Price <= 4000000)
            .ToList();
        Assert.Equal(3, priceRangeVehicles.Count);
        _output.WriteLine($"  ✓ 找到 {priceRangeVehicles.Count} 辆车在 300-400 万区间");

        // 场景 4: 组合筛选
        _output.WriteLine("\n【场景 4】组合筛选 (Toyota + SUV)");
        var combinedVehicles = allVehicles
            .Where(v => v.Brand == "Toyota" && v.Type == "SUV")
            .ToList();
        Assert.Equal(2, combinedVehicles.Count);
        _output.WriteLine($"  ✓ 找到 {combinedVehicles.Count} 辆 Toyota SUV");

        // 验证 Tool 调用
        var toolCall = new JsonObject
        {
            ["tool_call"] = "query_data",
            ["entity"] = "vehicles",
            ["action"] = "list",
            ["filters"] = new JsonArray
            {
                new JsonObject
                {
                    ["field"] = "brand",
                    ["op"] = "eq",
                    ["value"] = "Toyota"
                },
                new JsonObject
                {
                    ["field"] = "type",
                    ["op"] = "eq",
                    ["value"] = "SUV"
                }
            }
        };

        var validationResult = await _toolValidator.ValidateAsync(toolCall, _projectId, _fsm.CurrentState);
        Assert.True(validationResult.IsValid);
        _output.WriteLine($"  ✓ Tool 验证通过");

        _output.WriteLine("\n=== 条件筛选测试通过 ✓ ===");
    }

    #endregion

    #region 测试场景 3: 车辆对比推荐

    [Fact]
    public void VehicleComparisonRecommendation_ShouldSuggestBestMatch()
    {
        _output.WriteLine("\n=== 开始车辆对比推荐测试 ===");

        var customerPreference = new Dictionary<string, object>
        {
            ["budget"] = 3500000,
            ["type"] = "SUV",
            ["brand_preference"] = "Toyota",
            ["priority"] = "reliability"
        };

        _output.WriteLine("\n【客户需求】");
        _output.WriteLine($"  ✓ 预算: ¥{customerPreference["budget"]:N0}");
        _output.WriteLine($"  ✓ 类型: {customerPreference["type"]}");
        _output.WriteLine($"  ✓ 品牌偏好: {customerPreference["brand_preference"]}");
        _output.WriteLine($"  ✓ 优先级: {customerPreference["priority"]}");

        var vehicles = GetMockVehicles();

        // 推荐算法: 基于偏好评分
        _output.WriteLine("\n【推荐评分】");
        var recommendations = vehicles
            .Select(v => new
            {
                Vehicle = v,
                Score = CalculateRecommendationScore(v, customerPreference)
            })
            .OrderByDescending(r => r.Score)
            .ToList();

        for (int i = 0; i < Math.Min(3, recommendations.Count); i++)
        {
            var rec = recommendations[i];
            _output.WriteLine($"  #{i + 1}: {rec.Vehicle.Brand} {rec.Vehicle.Model} - 评分 {rec.Score}");
        }

        // 最佳匹配应该是 RAV4 (Toyota SUV, 价格适中)
        var bestMatch = recommendations.First();
        Assert.Equal("RAV4", bestMatch.Vehicle.Model);
        Assert.Equal("Toyota", bestMatch.Vehicle.Brand);
        Assert.Equal("SUV", bestMatch.Vehicle.Type);
        _output.WriteLine($"\n  ✓ 最佳推荐: {bestMatch.Vehicle.Brand} {bestMatch.Vehicle.Model} (评分: {bestMatch.Score})");

        _output.WriteLine("\n=== 车辆对比推荐测试通过 ✓ ===");
    }

    #endregion

    #region 测试场景 4: 库存检查

    [Fact]
    public void VehicleStockCheck_ShouldReturnAvailability()
    {
        _output.WriteLine("\n=== 开始库存检查测试 ===");

        var vehicles = GetMockVehicles();

        _output.WriteLine("\n【库存状态】");
        foreach (var v in vehicles)
        {
            var isAvailable = v.Stock > 0;
            var status = isAvailable ? "有货 ✓" : "缺货 ✗";
            _output.WriteLine($"  - {v.Brand} {v.Model}: {v.Stock} 辆 ({status})");
        }

        // 检查有货车辆
        var availableVehicles = vehicles.Where(v => v.Stock > 0).ToList();
        Assert.Equal(6, availableVehicles.Count);
        _output.WriteLine($"\n  ✓ 有货车辆: {availableVehicles.Count}/{vehicles.Count}");

        _output.WriteLine("\n=== 库存检查测试通过 ✓ ===");
    }

    #endregion

    #region 测试场景 5: 无效查询拦截

    [Fact]
    public async Task InvalidQueryShouldBeRejected()
    {
        _output.WriteLine("\n=== 开始无效查询拦截测试 ===");

        // 场景 1: SQL 注入尝试
        _output.WriteLine("\n【场景 1】SQL 注入尝试");
        var maliciousToolCall = new JsonObject
        {
            ["tool_call"] = "query_data",
            ["entity"] = "vehicles; DROP TABLE customers",
            ["action"] = "list"
        };
        var result1 = await _toolValidator.ValidateAsync(maliciousToolCall, _projectId);
        Assert.False(result1.IsValid);
        _output.WriteLine($"  ✓ 拦截成功: {result1.ErrorMessage}");

        // 场景 2: 无效 entity
        _output.WriteLine("\n【场景 2】无效 entity");
        var invalidEntityCall = new JsonObject
        {
            ["tool_call"] = "query_data",
            ["entity"] = "invalid_entity",
            ["action"] = "list"
        };
        var result2 = await _toolValidator.ValidateAsync(invalidEntityCall, _projectId);
        Assert.False(result2.IsValid);
        _output.WriteLine($"  ✓ 拦截成功: {result2.ErrorMessage}");

        // 场景 3: 无效 action
        _output.WriteLine("\n【场景 3】无效 action (delete)");
        var invalidActionCall = new JsonObject
        {
            ["tool_call"] = "query_data",
            ["entity"] = "vehicles",
            ["action"] = "delete"
        };
        var result3 = await _toolValidator.ValidateAsync(invalidActionCall, _projectId);
        Assert.False(result3.IsValid);
        _output.WriteLine($"  ✓ 拦截成功: {result3.ErrorMessage}");

        _output.WriteLine("\n=== 无效查询拦截测试通过 ✓ ===");
    }

    #endregion

    #region 辅助方法

    private static List<Vehicle> GetMockVehicles()
    {
        return new List<Vehicle>
        {
            new() { Id = "V001", Brand = "Toyota", Model = "RAV4", Type = "SUV", Price = 3200000, Stock = 5 },
            new() { Id = "V002", Brand = "Toyota", Model = "Camry", Type = "Sedan", Price = 2800000, Stock = 3 },
            new() { Id = "V003", Brand = "Toyota", Model = "Highlander", Type = "SUV", Price = 4200000, Stock = 2 },
            new() { Id = "V004", Brand = "Honda", Model = "CR-V", Type = "SUV", Price = 3000000, Stock = 4 },
            new() { Id = "V005", Brand = "Honda", Model = "Accord", Type = "Sedan", Price = 2600000, Stock = 6 },
            new() { Id = "V006", Brand = "Nissan", Model = "X-Trail", Type = "SUV", Price = 2900000, Stock = 0 }
        };
    }

    private static int CalculateRecommendationScore(Vehicle vehicle, Dictionary<string, object> preference)
    {
        var score = 0;
        var budget = Convert.ToDecimal(preference["budget"]);
        var type = preference["type"].ToString();
        var brandPref = preference["brand_preference"].ToString();

        // 品牌匹配 (+50)
        if (vehicle.Brand == brandPref) score += 50;

        // 类型匹配 (+30)
        if (vehicle.Type == type) score += 30;

        // 价格接近预算 (+20)
        var priceDiff = Math.Abs(vehicle.Price - budget);
        if (priceDiff < 500000) score += 20;
        else if (priceDiff < 1000000) score += 10;

        // 库存充足 (+10)
        if (vehicle.Stock > 0) score += 10;

        return score;
    }

    #endregion
}
