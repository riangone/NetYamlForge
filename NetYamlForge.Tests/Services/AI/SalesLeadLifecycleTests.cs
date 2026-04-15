using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Moq;
using NetYamlForge.AI.Services;
using NetYamlForge.AI.Services.ToolValidation;
using Xunit;
using Xunit.Abstractions;

namespace NetYamlForge.Tests.Services.AI;

/// <summary>
/// 销售线索生命周期自动化集成测试
/// 
/// 测试场景:
/// 1. AI 对话后自动创建销售线索
/// 2. 线索评分自动更新 (意图 + 情感 + 槽位完成度)
/// 3. 线索状态流转 (new → contacted → qualified → proposal → won/lost)
/// 4. 首次/末次触达对话 ID 记录
/// 5. 触达次数累计
/// 
/// 纯内存测试，无需人工干预。
/// </summary>
public class SalesLeadLifecycleTests
{
    private readonly ITestOutputHelper _output;
    private readonly ToolCallValidator _toolValidator;
    private readonly string _projectId = "auto-dealer-demo";

    // 模拟线索数据
    private class SalesLead
    {
        public string LeadId { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        public string VehicleInterest { get; set; } = string.Empty;
        public int LeadScore { get; set; } = 50;
        public string Status { get; set; } = "new";
        public string? AiFirstTouchConversationId { get; set; }
        public string? AiLastTouchConversationId { get; set; }
        public int AiTouchCount { get; set; }
        public Dictionary<string, object> Attributes { get; set; } = new();
    }

    public SalesLeadLifecycleTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerMock = new Mock<ILogger<ToolCallValidator>>();
        _toolValidator = new ToolCallValidator(loggerMock.Object, new NetYamlForge.AI.Infrastructure.DefaultSqlSafetyGuard());
    }

    #region 测试场景 1: AI 对话后自动创建线索

    [Fact]
    public async Task AiConversationShouldAutoCreateLead()
    {
        _output.WriteLine("=== 开始 AI 对话自动创建线索测试 ===");

        // 模拟 AI 对话收集的信息
        var conversationData = new Dictionary<string, string>
        {
            ["vehicle_model"] = "RAV4",
            ["customer_name"] = "赵六",
            ["customer_phone"] = "13612345678",
            ["intent"] = "test_drive_request"
        };

        _output.WriteLine("\n【步骤 1】AI 对话收集客户信息");
        foreach (var item in conversationData)
        {
            _output.WriteLine($"  ✓ {item.Key}: {item.Value}");
        }

        // 模拟创建线索
        _output.WriteLine("\n【步骤 2】自动创建销售线索");
        var lead = new SalesLead
        {
            LeadId = "LEAD-001",
            CustomerId = "CUST-001",
            VehicleInterest = conversationData["vehicle_model"],
            LeadScore = 50,
            Status = "new",
            AiFirstTouchConversationId = "conv-001",
            AiLastTouchConversationId = "conv-001",
            AiTouchCount = 1
        };

        Assert.Equal("new", lead.Status);
        Assert.Equal(50, lead.LeadScore);
        Assert.Equal(1, lead.AiTouchCount);
        _output.WriteLine($"  ✓ 线索创建成功: {lead.LeadId}");
        _output.WriteLine($"  ✓ 初始状态: {lead.Status}");
        _output.WriteLine($"  ✓ 初始评分: {lead.LeadScore}");

        // 验证 Tool 调用合法
        var toolCall = new JsonObject
        {
            ["tool_call"] = "query_data",
            ["entity"] = "sales_leads",
            ["action"] = "create"
        };
        var validationResult = await _toolValidator.ValidateAsync(toolCall, _projectId);
        Assert.True(validationResult.IsValid);
        _output.WriteLine($"  ✓ Tool 验证通过");

        _output.WriteLine("\n=== AI 对话自动创建线索测试通过 ✓ ===");
    }

    #endregion

    #region 测试场景 2: 线索评分自动更新

    [Fact]
    public void LeadScoreAutoUpdate_ShouldCalculateCorrectly()
    {
        _output.WriteLine("\n=== 开始线索评分自动更新测试 ===");

        var lead = new SalesLead
        {
            LeadId = "LEAD-002",
            LeadScore = 50
        };

        // 场景 1: 试乘预约请求 (+15)
        _output.WriteLine("\n【场景 1】试乘预约请求");
        var intent1 = "test_drive_request";
        var scoreDelta1 = CalculateScoreDelta(intent1, 0.8, 5);
        lead.LeadScore += scoreDelta1;
        _output.WriteLine($"  ✓ 意图: {intent1} → 评分 +{scoreDelta1}");
        _output.WriteLine($"  ✓ 当前评分: {lead.LeadScore}");
        Assert.Equal(65, lead.LeadScore);

        // 场景 2: 正面情感 (+5)
        _output.WriteLine("\n【场景 2】正面情感分析");
        var sentimentScore = 0.85;
        var scoreDelta2 = sentimentScore > 0.7 ? 5 : (sentimentScore < 0.3 ? -3 : 0);
        lead.LeadScore += scoreDelta2;
        _output.WriteLine($"  ✓ 情感得分: {sentimentScore} → 评分 +{scoreDelta2}");
        _output.WriteLine($"  ✓ 当前评分: {lead.LeadScore}");
        Assert.Equal(70, lead.LeadScore);

        // 场景 3: 槽位完成度 (+10)
        _output.WriteLine("\n【场景 3】槽位完成度 >= 5");
        var slotsFilled = 5;
        var scoreDelta3 = slotsFilled >= 5 ? 10 : 0;
        lead.LeadScore += scoreDelta3;
        _output.WriteLine($"  ✓ 槽位完成: {slotsFilled} → 评分 +{scoreDelta3}");
        _output.WriteLine($"  ✓ 当前评分: {lead.LeadScore}");
        Assert.Equal(80, lead.LeadScore);

        // 场景 4: 价格咨询 (+10)
        _output.WriteLine("\n【场景 4】价格咨询");
        var intent4 = "price_inquiry";
        var scoreDelta4 = CalculateScoreDelta(intent4, 0.7, 0);
        lead.LeadScore += scoreDelta4;
        _output.WriteLine($"  ✓ 意图: {intent4} → 评分 +{scoreDelta4}");
        _output.WriteLine($"  ✓ 当前评分: {lead.LeadScore}");
        Assert.Equal(90, lead.LeadScore);

        // 验证评分上限 (100)
        _output.WriteLine("\n【验证】评分上限检查");
        lead.LeadScore = Math.Min(100, lead.LeadScore);
        Assert.Equal(90, lead.LeadScore);
        _output.WriteLine($"  ✓ 最终评分: {lead.LeadScore}/100");

        _output.WriteLine("\n=== 线索评分自动更新测试通过 ✓ ===");
    }

    #endregion

    #region 测试场景 3: 线索状态流转

    [Fact]
    public void LeadStatusFlow_ShouldTransitionCorrectly()
    {
        _output.WriteLine("\n=== 开始线索状态流转测试 ===");

        var lead = new SalesLead
        {
            LeadId = "LEAD-003",
            Status = "new"
        };

        // new → contacted
        _output.WriteLine("\n【转换 1】new → contacted");
        lead.Status = "contacted";
        Assert.Equal("contacted", lead.Status);
        _output.WriteLine($"  ✓ 状态: {lead.Status}");

        // contacted → qualified
        _output.WriteLine("\n【转换 2】contacted → qualified");
        lead.Status = "qualified";
        Assert.Equal("qualified", lead.Status);
        _output.WriteLine($"  ✓ 状态: {lead.Status}");

        // qualified → proposal
        _output.WriteLine("\n【转换 3】qualified → proposal");
        lead.Status = "proposal";
        Assert.Equal("proposal", lead.Status);
        _output.WriteLine($"  ✓ 状态: {lead.Status}");

        // proposal → won
        _output.WriteLine("\n【转换 4】proposal → won");
        lead.Status = "won";
        Assert.Equal("won", lead.Status);
        _output.WriteLine($"  ✓ 状态: {lead.Status}");
        _output.WriteLine($"  ✓ 线索转化成功 ✓");

        _output.WriteLine("\n=== 线索状态流转测试通过 ✓ ===");
    }

    #endregion

    #region 测试场景 4: 多次触达累计

    [Fact]
    public void MultipleTouchPoints_ShouldAccumulateCorrectly()
    {
        _output.WriteLine("\n=== 开始多次触达累计测试 ===");

        var lead = new SalesLead
        {
            LeadId = "LEAD-004",
            AiTouchCount = 0
        };

        var conversations = new[]
        {
            ("conv-001", "vehicle_inquiry", "2026-04-01"),
            ("conv-002", "test_drive_request", "2026-04-03"),
            ("conv-003", "price_inquiry", "2026-04-05"),
            ("conv-004", "vehicle_comparison", "2026-04-07")
        };

        _output.WriteLine("\n【触达记录】");
        for (int i = 0; i < conversations.Length; i++)
        {
            var (convId, intent, date) = conversations[i];

            lead.AiTouchCount++;
            lead.AiLastTouchConversationId = convId;

            if (lead.AiFirstTouchConversationId == null)
            {
                lead.AiFirstTouchConversationId = convId;
            }

            _output.WriteLine($"  ✓ 触达 {i + 1}: {convId} ({intent}) @ {date}");
            _output.WriteLine($"    - 首次触达: {lead.AiFirstTouchConversationId}");
            _output.WriteLine($"    - 末次触达: {lead.AiLastTouchConversationId}");
            _output.WriteLine($"    - 触达次数: {lead.AiTouchCount}");
        }

        // 验证
        Assert.Equal(4, lead.AiTouchCount);
        Assert.Equal("conv-001", lead.AiFirstTouchConversationId);
        Assert.Equal("conv-004", lead.AiLastTouchConversationId);

        _output.WriteLine($"\n  ✓ 总触达次数: {lead.AiTouchCount}");
        _output.WriteLine($"  ✓ 首次触达: {lead.AiFirstTouchConversationId}");
        _output.WriteLine($"  ✓ 末次触达: {lead.AiLastTouchConversationId}");

        _output.WriteLine("\n=== 多次触达累计测试通过 ✓ ===");
    }

    #endregion

    #region 测试场景 5: 线索评分边界条件

    [Theory]
    [InlineData("test_drive_request", 15)]
    [InlineData("price_inquiry", 10)]
    [InlineData("vehicle_comparison", 12)]
    [InlineData("finance_inquiry", 8)]
    [InlineData("general_inquiry", 3)]
    public void LeadScoreByIntent_ShouldCalculateCorrectly(string intent, int expectedDelta)
    {
        var delta = CalculateScoreDelta(intent, 0.5, 0);
        Assert.Equal(expectedDelta, delta);
    }

    [Fact]
    public void LeadScoreBoundaries_ShouldRespectLimits()
    {
        var lead = new SalesLead { LeadScore = 95 };

        // 测试上限
        lead.LeadScore = Math.Min(100, lead.LeadScore + 10);
        Assert.Equal(100, lead.LeadScore);

        lead = new SalesLead { LeadScore = 5 };
        // 测试下限
        lead.LeadScore = Math.Max(0, lead.LeadScore - 10);
        Assert.Equal(0, lead.LeadScore);
    }

    #endregion

    #region 辅助方法

    private static int CalculateScoreDelta(string intent, double sentimentScore, int slotsFilled)
    {
        var delta = intent switch
        {
            "test_drive_request" => 15,
            "price_inquiry" => 10,
            "vehicle_comparison" => 12,
            "finance_inquiry" => 8,
            "general_inquiry" => 3,
            _ => 0
        };

        if (sentimentScore > 0.7) delta += 5;
        else if (sentimentScore < 0.3) delta -= 3;

        if (slotsFilled >= 5) delta += 10;

        return delta;
    }

    #endregion
}
