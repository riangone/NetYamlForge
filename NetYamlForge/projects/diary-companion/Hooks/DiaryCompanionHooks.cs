using System;
using System.Data;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;
using NetYamlForge.Services.AI;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.DiaryCompanion.Hooks;

public class DiaryAnalysisResult
{
    public string Sentiment { get; set; } = "";
    public string AiResponse { get; set; } = "";
}

public class PeriodInsightResult
{
    public string OverallMood { get; set; } = "";
    public string Analysis { get; set; } = "";
    public string Recommendations { get; set; } = "";
}

public class DiaryRow
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Weather { get; set; } = "";
    public string MoodBefore { get; set; } = "";
    public string Location { get; set; } = "";
    public string Sentiment { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

/// <summary>
/// 当用户保存日记时，通过 AI 评估情绪并给出暖心寄语
/// </summary>
public class AnalyzeDiaryMoodHook : IEntityHook
{
    private readonly IAntigravityCliService _ai;
    private readonly ILogger<AnalyzeDiaryMoodHook> _logger;

    public string Name => "analyze_diary_mood";

    public AnalyzeDiaryMoodHook(IAntigravityCliService ai, ILogger<AnalyzeDiaryMoodHook> logger)
    {
        _ai = ai;
        _logger = logger;
    }

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        try
        {
            var title = ctx.Values.GetValueOrDefault("Title")?.ToString();
            var content = ctx.Values.GetValueOrDefault("Content")?.ToString();
            var weather = ctx.Values.GetValueOrDefault("Weather")?.ToString();
            var moodBefore = ctx.Values.GetValueOrDefault("MoodBefore")?.ToString();
            var location = ctx.Values.GetValueOrDefault("Location")?.ToString();

            if (string.IsNullOrWhiteSpace(content))
            {
                return HookResult.Continue();
            }

            var prompt = $$"""
你是一个温暖、贴心、充满同理心的智能心灵伴侣 AI。
下面是用户写下的一篇个人日记，请仔细阅读并进行情绪 analysis：
- 标题：{{title}}
- 天气：{{weather}}
- 记录前心情：{{moodBefore}}
- 地理位置：{{location}}
- 日记正文：
{{content}}

请输出合法的 JSON 格式数据（不要包含任何 markdown 代码块标识如 ```json，不要输出任何额外的解释性文字），包含以下两个字段：
1. "Sentiment": 必须是 "积极"、"平和" 或 "消极" 之一。
2. "AiResponse": 一小段温暖、真诚、善解人意且带有心理疏导/鼓励的文字（不超过 150 字），就像好朋友在关心他们一样。

JSON 示例：
{
  "Sentiment": "积极",
  "AiResponse": "看到你今天过得这么充实，我也为你感到高兴！继续保持这样的好状态，期待你明天发现更多小美好。"
}
""";

            _logger.LogInformation("正在调用 AI 进行日记分析...");
            var result = await _ai.PromptJsonAsync<DiaryAnalysisResult>(prompt, projectName: "diary-companion");

            if (result != null)
            {
                if (result.Sentiment == "积极" || result.Sentiment == "平和" || result.Sentiment == "消极")
                {
                    ctx.Values["Sentiment"] = result.Sentiment;
                }
                else
                {
                    ctx.Values["Sentiment"] = "平和"; // 默认兜底
                }
                ctx.Values["AiResponse"] = result.AiResponse;
            }
            else
            {
                ctx.Values["Sentiment"] = "平和";
                ctx.Values["AiResponse"] = "今天的日记已经妥善保管啦。无论今天过得如何，都辛苦了，早点休息吧！";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "日记情绪分析 Hook 运行失败");
            // 失败时不阻断日记保存
            ctx.Values["Sentiment"] = "平和";
            ctx.Values["AiResponse"] = "今天辛苦了，AI 伴侣正处于休眠中，但你的心事已被温柔记录。";
        }

        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 根据用户指定时间段，分析期间日记并生成情绪统计与建议
/// </summary>
public class GeneratePeriodInsightHook : IEntityHook
{
    private readonly IAntigravityCliService _ai;
    private readonly ILogger<GeneratePeriodInsightHook> _logger;

    public string Name => "generate_period_insight";

    public GeneratePeriodInsightHook(IAntigravityCliService ai, ILogger<GeneratePeriodInsightHook> logger)
    {
        _ai = ai;
        _logger = logger;
    }

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        try
        {
            var startDateObj = ctx.Values.GetValueOrDefault("StartDate");
            var endDateObj = ctx.Values.GetValueOrDefault("EndDate");

            if (startDateObj == null || endDateObj == null)
            {
                return HookResult.Abort("开始日期和结束日期不能为空。");
            }

            string startDateStr = startDateObj is DateTime dtStart ? dtStart.ToString("yyyy-MM-dd") : startDateObj.ToString()!;
            string endDateStr = endDateObj is DateTime dtEnd ? dtEnd.ToString("yyyy-MM-dd") : endDateObj.ToString()!;

            // 格式化为 SQLite 兼容日期范围比较
            var qStart = startDateStr;
            var qEnd = endDateStr + " 23:59:59";

            // 查询此期间的所有日记
            // 实体 DiaryEntry 映射的表名是 DiaryEntry
            var sql = "SELECT Title, Content, Weather, MoodBefore, Location, Sentiment, CreatedAt FROM DiaryEntry WHERE CreatedAt >= @Start AND CreatedAt <= @End ORDER BY CreatedAt ASC";
            var diaries = (await db.QueryAsync<DiaryRow>(sql, new { Start = qStart, End = qEnd }, tx)).ToList();

            if (diaries.Count == 0)
            {
                ctx.Values["OverallMood"] = "无数据";
                ctx.Values["Analysis"] = $"在 {startDateStr} 至 {endDateStr} 期间没有记录任何日记，AI 无法为您生成报告。写几篇日记再来吧！";
                ctx.Values["Recommendations"] = "每天花 3 分钟记录一下自己的心情，不仅能舒缓压力，也能让 AI 伴侣更了解你哦。";
                return HookResult.Continue();
            }

            // 构建日记汇总文本
            var summaryBuilder = new System.Text.StringBuilder();
            for (int i = 0; i < diaries.Count; i++)
            {
                var d = diaries[i];
                summaryBuilder.AppendLine($"--- 日记 #{i + 1} ({d.CreatedAt}) ---");
                summaryBuilder.AppendLine($"标题: {d.Title}");
                summaryBuilder.AppendLine($"天气: {d.Weather} | 记录前心情: {d.MoodBefore} | 地理位置: {d.Location} | AI评估情绪: {d.Sentiment}");
                summaryBuilder.AppendLine($"正文: {d.Content}");
                summaryBuilder.AppendLine();
            }

            var prompt = $$"""
你是一个专业的心理咨询师和情感分析 AI。
以下是用户在 {{startDateStr}} 至 {{endDateStr}} 期间记录的个人日记：

{{summaryBuilder}}

请基于这些日记的内容、天气、记录心情和 AI 评估情绪，生成一份情绪统计分析报告。
请输出合法的 JSON 格式数据（不要包含任何 markdown 代码块标识如 ```json，不要输出任何额外的解释性文字），包含以下三个字段：
1. "OverallMood": 期间的主要情绪，例如 "平和"、"焦虑中带点喜悦"、"有些疲惫"、"积极乐观" 等（不超过 10 个字）。
2. "Analysis": 情绪的深度统计与起伏趋势分析。分析用户这段时间的心态变化、主要影响因素（如果有提到的话）以及有什么积极或消极的信号（200-300 字）。
3. "Recommendations": 针对用户当前的心态和生活状态，提供 3 条具体、温暖、实用的生活或心理调整建议（150-200 字）。

JSON 示例：
{
  "OverallMood": "平和中带些疲惫",
  "Analysis": "在这段时间里，用户的整体心境维持在平和的状态，但在周中由于工作或生活琐事显露出较为明显的疲惫感。周三和周四的日记中多次提到‘困’和‘有些累’，然而随着周末的临近，在接触了大自然或休息后，积极情绪有所回升。总的来说，属于正常的情绪起伏，但需要注意周中的能量补给。",
  "Recommendations": "1. 建议在周三或周四中午安排 15 分钟的彻底小憩，有助于缓解累积的疲惫。\n2. 可以在感到疲惫时吃一点甜品或喝杯热茶，给自己一个心理缓冲期。\n3. 周末继续保持亲近自然或听音乐的放松方式，这对你恢复精力十分有效。"
}
""";

            _logger.LogInformation("正在调用 AI 进行周期情绪分析报告生成...");
            var result = await _ai.PromptJsonAsync<PeriodInsightResult>(prompt, projectName: "diary-companion");

            if (result != null)
            {
                ctx.Values["OverallMood"] = result.OverallMood;
                ctx.Values["Analysis"] = result.Analysis;
                ctx.Values["Recommendations"] = result.Recommendations;
            }
            else
            {
                ctx.Values["OverallMood"] = "分析失败";
                ctx.Values["Analysis"] = "无法解析 AI 返回的分析报告。";
                ctx.Values["Recommendations"] = "请尝试重新生成或检查网络。";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成周期情绪分析 Hook 运行失败");
            return HookResult.Abort($"生成报告时发生错误：{ex.Message}");
        }

        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
