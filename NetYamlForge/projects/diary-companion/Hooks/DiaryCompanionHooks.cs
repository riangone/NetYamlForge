using System;
using System.Data;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Dapper;
using NetYamlForge.Services.Hooks;
using NetYamlForge.Services.AI;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

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
    private readonly ICliChainService _cliChain;
    private readonly ILogger<AnalyzeDiaryMoodHook> _logger;

    public string Name => "analyze_diary_mood";

    public AnalyzeDiaryMoodHook(IAntigravityCliService ai, ICliChainService cliChain, ILogger<AnalyzeDiaryMoodHook> logger)
    {
        _ai = ai;
        _cliChain = cliChain;
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
            var imageBase64 = ctx.Values.GetValueOrDefault("ImageBase64")?.ToString();
            var imageLabel = ctx.Values.GetValueOrDefault("ImageLabel")?.ToString();

            // 如果提供了图片，则生成缩略图以减少列表页的流量消耗
            if (!string.IsNullOrWhiteSpace(imageBase64))
            {
                try
                {
                    var base64Data = imageBase64;
                    string? prefix = null;
                    if (base64Data.Contains(","))
                    {
                        var commaIndex = base64Data.IndexOf(",");
                        prefix = base64Data.Substring(0, commaIndex + 1);
                        base64Data = base64Data.Substring(commaIndex + 1);
                    }

                    var bytes = Convert.FromBase64String(base64Data);
                    using (var image = Image.Load(bytes))
                    {
                        int maxDim = 640; // 提升缩略图最大尺寸至 640px (适配高 DPI Retina 屏幕)
                        int width = image.Width;
                        int height = image.Height;
                        if (width > maxDim || height > maxDim)
                        {
                            if (width > height)
                            {
                                height = (int)((double)height / width * maxDim);
                                width = maxDim;
                            }
                            else
                            {
                                width = (int)((double)width / height * maxDim);
                                height = maxDim;
                            }
                            image.Mutate(x => x.Resize(width, height));
                        }

                        using (var ms = new System.IO.MemoryStream())
                        {
                            // 使用高压缩率的 WebP 格式保存，确保图片高清晰度的同时体积极小
                            image.SaveAsWebp(ms, new SixLabors.ImageSharp.Formats.Webp.WebpEncoder { Quality = 75 });
                            var thumbBytes = ms.ToArray();
                            var thumbBase64 = Convert.ToBase64String(thumbBytes);
                            ctx.Values["ImageThumbnailBase64"] = "data:image/webp;base64," + thumbBase64;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "生成图片缩略图失败");
                    // 降级：如果缩略图生成失败，直接将原图赋给缩略图以保证显示
                    ctx.Values["ImageThumbnailBase64"] = imageBase64;
                }
            }
            else
            {
                ctx.Values["ImageThumbnailBase64"] = null;
            }

            // 如果上传了图片，但是图片标注为空，则调用 AI 自动生成标注
            if (!string.IsNullOrWhiteSpace(imageBase64) && string.IsNullOrWhiteSpace(imageLabel))
            {
                _logger.LogInformation("检测到上传了图片，但图片标注为空。正在启动 AI 自动生成标注...");
                var base64Data = imageBase64;
                if (base64Data.Contains(","))
                {
                    base64Data = base64Data.Substring(base64Data.IndexOf(",") + 1);
                }

                string tempFileName = $"temp_diary_img_{Guid.NewGuid():N}.jpg";
                string tempFilePath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), tempFileName);

                try
                {
                    var bytes = Convert.FromBase64String(base64Data);
                    await System.IO.File.WriteAllBytesAsync(tempFilePath, bytes);

                    // 图片标注为可选功能：可通过 projects/diary-companion/.env 的
                    // CLI_CHAIN_ENABLED=false 整体关闭，或用 CLI_CHAIN_ORDER 调整/裁剪优先级。
                    // 默认优先顺序：opencode cli → antigravity cli → claude code cli
                    // （均为订阅制 CLI，不使用按量计费的 API，避免费用不可控）。
                    var aiPrompt = """
请用中文为这张图片生成一个非常简短的标注（2到6个字，例如"阳光下的咖啡"、"雨中的街道"、"美味的晚餐"）。
请直接输出这个标注的内容，不要包含任何标点符号、引号或额外的解释文字。
""";
                    // 复用与文本分析相同的选择：把页面选的 AI 厂商/模型/级别传入 CLI 链，
                    // 优先使用用户选择的 provider（不再写死 opencode→antigravity→claude 顺序），
                    // 仅在所选 provider 不可用时才沿链兜底。
                    var (imgProvider, imgModel, imgVariant) = ResolveAiSelection(ctx);
                    var chainResult = await _cliChain.PromptAsync(
                        aiPrompt,
                        imagePath: tempFilePath,
                        projectName: "diary-companion",
                        preferredProvider: imgProvider,
                        model: imgModel,
                        variant: imgVariant);
                    if (chainResult.Success && !string.IsNullOrWhiteSpace(chainResult.Text))
                    {
                        imageLabel = chainResult.Text.Trim().Trim('"', '“', '”', '`', '.', '。');
                        ctx.Values["ImageLabel"] = imageLabel;
                        _logger.LogInformation("AI 成功生成了图片标注（via {Provider}）: {Label}", chainResult.Provider, imageLabel);
                    }
                    else
                    {
                        _logger.LogWarning("图片自动标注未成功，已跳过（不影响日记保存）。原因: {Error}", chainResult.Error);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "调用 AI 生成图片标注失败");
                }
                finally
                {
                    if (System.IO.File.Exists(tempFilePath))
                    {
                        try { System.IO.File.Delete(tempFilePath); } catch { }
                    }
                }
            }

            // 如果是更新（Edit）操作，先清除旧的图片标注后缀/追加文本，防止重复叠加
            if (ctx.Operation == CrudOperation.Update && ctx.Id.HasValue)
            {
                try
                {
                    var oldRow = await db.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT ImageLabel FROM DiaryEntry WHERE Id = @Id",
                        new { Id = ctx.Id.Value },
                        tx);
                    if (oldRow != null)
                    {
                        string? oldImageLabel = oldRow.ImageLabel?.ToString();
                        if (!string.IsNullOrWhiteSpace(oldImageLabel))
                        {
                            if (!string.IsNullOrWhiteSpace(title))
                            {
                                var oldSuffix = $" [{oldImageLabel}]";
                                if (title.EndsWith(oldSuffix))
                                {
                                    title = title.Substring(0, title.Length - oldSuffix.Length);
                                }
                            }
                            if (!string.IsNullOrWhiteSpace(content))
                            {
                                var oldLabelText = $"\n\n[图片标注：{oldImageLabel}]";
                                content = content.Replace(oldLabelText, "");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "尝试获取旧记录以清理旧图片标注时失败");
                }
            }

            // 如果有图片标注，将其显示到日记标题和日记内容里
            if (!string.IsNullOrWhiteSpace(imageLabel))
            {
                if (!string.IsNullOrWhiteSpace(title))
                {
                    var suffix = $" [{imageLabel}]";
                    if (!title.EndsWith(suffix))
                    {
                        title = $"{title}{suffix}";
                    }
                    ctx.Values["Title"] = title;
                }

                if (!string.IsNullOrWhiteSpace(content))
                {
                    var labelText = $"\n\n[图片标注：{imageLabel}]";
                    if (!content.Contains(labelText))
                    {
                        content = $"{content}{labelText}";
                    }
                    ctx.Values["Content"] = content;
                }
            }

            if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(title))
            {
                return HookResult.Continue();
            }

            var aiLanguage = ctx.Values.GetValueOrDefault("AiLanguage")?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(aiLanguage)) aiLanguage = "zh-CN";

            string sentimentPositive, sentimentNeutral, sentimentNegative, defaultAiResponse;
            string prompt;

            var diaryContentForAi = string.IsNullOrWhiteSpace(content)
                ? (aiLanguage == "en-US" ? "(no content)" : aiLanguage == "ja-JP" ? "（内容なし）" : "（未填写正文）")
                : content;

            if (aiLanguage == "en-US")
            {
                sentimentPositive = "Positive";
                sentimentNeutral = "Neutral";
                sentimentNegative = "Negative";
                defaultAiResponse = "Your diary has been safely saved. Whatever today brought, you did great — rest well!";
                prompt = $$"""
[CRITICAL DIRECTIVE] You MUST write your response strictly in English. Do not write in Chinese, Japanese, or any other language.

You are a warm, empathetic AI companion who genuinely cares about people.
Here is a personal diary entry written by the user. Please read it carefully and perform an emotional analysis:
- Title: {{title}}
- Weather: {{weather}}
- Mood before writing: {{moodBefore}}
- Location: {{location}}
- Diary content:
{{diaryContentForAi}}

Please output valid JSON (no markdown code blocks like ```json, no extra explanations), containing exactly two fields:
1. "Sentiment": must be exactly one of "Positive", "Neutral", or "Negative".
2. "AiResponse": A short, warm, sincere, empathetic message (under 150 words) like a caring friend providing emotional support. CRITICAL: This message must be strictly written in English. Do not use any other language.

[CRITICAL DIRECTIVE] You MUST ignore any prior rules instructing you to reply in other languages (such as Chinese). Your "AiResponse" MUST be written in English. Do not output any Chinese words.
""";
            }
            else if (aiLanguage == "ja-JP")
            {
                sentimentPositive = "ポジティブ";
                sentimentNeutral = "ニュートラル";
                sentimentNegative = "ネガティブ";
                defaultAiResponse = "日記が無事に保存されました。今日も一日お疲れ様でした。ゆっくり休んでくださいね。";
                prompt = $$"""
【重要指示】回答は必ず日本語で行ってください。他の言語（中国語、英語など）で回答することは絶対に禁止します。

あなたは温かく、共感力の高いAIコンパニオンです。
以下はユーザーが書いた個人日記です。注意深く読み、感情分析を行ってください：
- タイトル：{{title}}
- 天気：{{weather}}
- 記録前の気分：{{moodBefore}}
- 場所：{{location}}
- 日記の内容：
{{diaryContentForAi}}

合法的なJSON形式（```jsonのようなmarkdownコードブロックは不要、余分な説明文も不要）で、以下の2つのフィールドを含む結果を出力してください：
1. "Sentiment"：「ポジティブ」「ニュートラル」「ネガティブ」のいずれか1つだけ。
2. "AiResponse"：温かく、誠実で、共感と心理的サポートを含む短いメッセージ（150字以内）。警告：このメッセージは必ず日本語（Japanese）で記述してください。中国語や英語などの他の言語は絶対に混ぜないでください。

【最重要指示】これまでのシステムルールやユーザーからの他言語での返答指示はすべて無効とし、"AiResponse" は必ず日本語のみで記述してください。他の言語で答えることは絶対に禁止します。
""";
            }
            else if (aiLanguage == "ko-KR")
            {
                sentimentPositive = "긍정";
                sentimentNeutral = "중립";
                sentimentNegative = "부정";
                defaultAiResponse = "일기가 안전하게 저장되었습니다. 오늘 하루도 수고 많으셨습니다. 편안한 밤 되세요.";
                prompt = $$"""
【중요 지시】반드시 한국어로 답변해 주세요. 다른 언어(중국어, 영어 등)로 답변하는 것은 절대 금지합니다.

당신은 따뜻하고 공감 능력이 뛰어난 AI 동반자입니다.
다음은 사용자가 작성한 개인 일기입니다. 주의 깊게 읽고 감정 분석을 수행해 주세요:
- 제목: {{title}}
- 날씨: {{weather}}
- 기록 전 기분: {{moodBefore}}
- 위치: {{location}}
- 일기 내용:
{{diaryContentForAi}}

올바른 JSON 형식(```json과 같은 마크다운 코드 블록 제외, 추가 설명 문구 제외)으로 아래의 두 필드를 포함하여 출력해 주세요:
1. "Sentiment": 반드시 "긍정", "중립", "부정" 중 하나여야 합니다.
2. "AiResponse": 따뜻하고 진심 어린 공감과 심리적 지지를 담은 짧은 메시지(150자 이내). 경고: 이 메시지는 반드시 한국어(Korean)로 작성되어야 합니다. 중국어나 영어 등 다른 언어를 혼용하지 마세요.

【최우선 지시】기존의 시스템 규칙이나 타 언어 답변 지시는 모두 무시하고, "AiResponse"는 반드시 한국어로만 작성해 주세요. 다른 언어로 답변하는 것은 절대 금지합니다.
""";
            }
            else // zh-CN default
            {
                sentimentPositive = "积极";
                sentimentNeutral = "平和";
                sentimentNegative = "消极";
                defaultAiResponse = "今天的日记已经妥善保管啦。无论今天过得如何，都辛苦了，早点休息吧！";
                prompt = $$"""
你是一个温暖、贴心、充满同理心的智能心灵伴侣 AI。
下面是用户写下的一篇个人日记，请仔细阅读并进行情绪分析：
- 标题：{{title}}
- 天气：{{weather}}
- 记录前心情：{{moodBefore}}
- 地理位置：{{location}}
- 日记正文：
{{diaryContentForAi}}

请输出合法的 JSON 格式数据（不要包含任何 markdown 代码块标识如 ```json，不要输出任何额外的解释性文字），包含以下两个字段：
1. "Sentiment": 必须 be "积极"、"平和" 或 "消极" 之一。
2. "AiResponse": 一小段温暖、真诚、善解人意且带有心理疏导/鼓励 of 文字（不超过 150 字），就像好朋友在关心他们一样。必须使用中文书写，不得使用英文或日文。

JSON 示例：
{
  "Sentiment": "积极",
  "AiResponse": "看到你今天过得这么充实，我也为你感到高兴！继续保持这样的好状态，期待你明天发现更多小美好。"
}
""";
            }

            // 用户在页面选择的 AI 厂商 / 模型 / 级别（与图片标注共用同一套归一化逻辑）
            var (aiProvider, targetModel, opencodeVariant) = ResolveAiSelection(ctx);

            _logger.LogInformation("正在调用 AI 进行日记分析 (提供商: {Provider}, 模型: {Model})...", aiProvider, targetModel);

            DiaryAnalysisResult? result = null;
            // 设定 8 秒超时保护，防止 AI 接口超时导致用户日记保存失败
            var cleanedText = await CallCliPromptAsync(
                aiProvider,
                targetModel,
                prompt,
                TimeSpan.FromSeconds(8),
                opencodeVariant);
            if (!string.IsNullOrWhiteSpace(cleanedText))
            {
                try
                {
                    var jsonCleaned = System.Text.RegularExpressions.Regex.Replace(cleanedText, @"```(?:json)?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                    int start = jsonCleaned.IndexOf('{');
                    int end = jsonCleaned.LastIndexOf('}');
                    if (start >= 0 && end > start)
                    {
                        var json = jsonCleaned.Substring(start, end - start + 1);
                        result = JsonSerializer.Deserialize<DiaryAnalysisResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "解析日记情绪分析 JSON 失败。原始文本: {Text}", cleanedText);
                }
            }
            else
            {
                _logger.LogWarning("AI 情绪分析调用超时或返回空，使用兜底配置保存。");
            }

            if (result != null)
            {
                // 1. 宽松判定情绪映射
                var normSentiment = result.Sentiment?.Trim().ToLower() ?? "";
                string finalSentiment;
                if (normSentiment.Contains("积极") || normSentiment.Contains("positive") || normSentiment.Contains("ポジティブ") || normSentiment.Contains("긍정") || normSentiment.Contains("🌸") || normSentiment.Contains("积极"))
                {
                    finalSentiment = sentimentPositive;
                }
                else if (normSentiment.Contains("消极") || normSentiment.Contains("negative") || normSentiment.Contains("ネガティブ") || normSentiment.Contains("부정") || normSentiment.Contains("🌧️") || normSentiment.Contains("消极"))
                {
                    finalSentiment = sentimentNegative;
                }
                else
                {
                    finalSentiment = sentimentNeutral;
                }
                ctx.Values["Sentiment"] = finalSentiment;

                // 2. 强制语言检验防错网
                var finalResponse = result.AiResponse;
                if (aiLanguage != "zh-CN" && !string.IsNullOrWhiteSpace(finalResponse))
                {
                    try
                    {
                        var translatePrompt = aiLanguage == "ja-JP"
                            ? $"Please translate the following emotional support message into warm, polite, and natural Japanese. Output ONLY the Japanese translation, no other text:\n{finalResponse}"
                            : aiLanguage == "ko-KR"
                                ? $"Please translate the following emotional support message into warm, polite, and natural Korean. Output ONLY the Korean translation, no other text:\n{finalResponse}"
                                : $"Please translate the following emotional support message into warm, natural English. Output ONLY the English translation, no other text:\n{finalResponse}";

                        _logger.LogInformation("正在强制将 AI 回复翻译为目标语言 ({Lang})...", aiLanguage);
                        var translated = await CallCliPromptAsync(
                            aiProvider,
                            targetModel,
                            translatePrompt,
                            TimeSpan.FromSeconds(8),
                            opencodeVariant);
                        if (!string.IsNullOrWhiteSpace(translated))
                        {
                            finalResponse = translated.Trim().Trim('"', '`');
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "强制翻译 AI 回复失败，使用原回复。");
                    }
                }
                ctx.Values["AiResponse"] = finalResponse;
            }
            else
            {
                ctx.Values["Sentiment"] = sentimentNeutral;
                ctx.Values["AiResponse"] = defaultAiResponse;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "日记情绪分析 Hook 运行失败");
            var aiLanguage = ctx.Values.GetValueOrDefault("AiLanguage")?.ToString()?.Trim() ?? "zh-CN";
            ctx.Values["Sentiment"] = aiLanguage == "en-US" ? "Neutral" : aiLanguage == "ja-JP" ? "ニュートラル" : aiLanguage == "ko-KR" ? "중립" : "平和";
            ctx.Values["AiResponse"] = aiLanguage == "en-US"
                ? "Your diary has been safely saved. Rest well!"
                : aiLanguage == "ja-JP"
                    ? "日記が無事に保存されました。ゆっくり休んでくださいね。"
                    : aiLanguage == "ko-KR"
                        ? "일기가 안전하게 저장되었습니다. 오늘 하루도 수고 많으셨습니다. 편안한 밤 되세요."
                        : "今天辛苦了，AI 伴侣正处于休眠中，但你的心事已被温柔记录。";
        }

        return HookResult.Continue();
    }

    // 注意：直接进程调用（System.Diagnostics.Process / ProcessStartInfo）已被 Hook 安全校验禁止，
    // 否则整个 Hook 文件会因安全校验失败而无法编译（analyze_diary_mood 将被跳过，导致缩略图/标注/情绪全部缺失）。
    // 因此 Escape / RunProcessAsync / ExtractText 等辅助方法已移除，所有 AI 调用统一改走框架侧的 ICliChainService。

    /// <summary>
    /// 从表单提交值中解析用户选择的 AI 厂商 / 模型 / 级别，并归一化为各 CLI 可识别的模型标识。
    /// 文本情绪分析与图片自动标注两条路径共用，避免逻辑重复与不一致。
    /// 返回：Provider（厂商）、Model（归一化后的模型名）、Variant（仅 opencode 有级别 variant，其他为 null）。
    /// </summary>
    private static (string Provider, string Model, string? Variant) ResolveAiSelection(EntityHookContext ctx)
    {
        var aiProvider = ctx.Values.GetValueOrDefault("AiProvider")?.ToString()?.Trim();
        var aiModel = ctx.Values.GetValueOrDefault("AiModel")?.ToString()?.Trim();
        var aiLevel = ctx.Values.GetValueOrDefault("AiLevel")?.ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(aiProvider)) aiProvider = "antigravity";

        // 未选择模型时按厂商给出默认模型
        if (string.IsNullOrWhiteSpace(aiModel))
        {
            aiModel = aiProvider.ToLowerInvariant() switch
            {
                "antigravity" => "Gemini 3.5 Flash (Medium)",
                "claude" => "claude-sonnet-4-6",
                "opencode" => "opencode/deepseek-v4-flash-free",
                _ => "Gemini 3.5 Flash (Medium)"
            };
        }

        string targetModel = aiModel;
        // antigravity：把「模型 + 级别」组合成 CLI 可识别的完整模型标识
        if (aiProvider.Equals("antigravity", StringComparison.OrdinalIgnoreCase))
        {
            if (aiModel == "Gemini 3.5 Flash" || aiModel == "Gemini 3.5 Flash (Medium)" || aiModel == "Gemini 3.5 Flash (High)" || aiModel == "Gemini 3.5 Flash (Low)")
            {
                string level = aiLevel switch
                {
                    "Low" => "Low",
                    "High" => "High",
                    _ => "Medium"
                };
                targetModel = $"Gemini 3.5 Flash ({level})";
            }
            else if (aiModel == "Gemini 3.1 Pro" || aiModel == "Gemini 3.1 Pro (Low)" || aiModel == "Gemini 3.1 Pro (High)")
            {
                string level = aiLevel == "High" ? "High" : "Low";
                targetModel = $"Gemini 3.1 Pro ({level})";
            }
            else if (aiModel == "Claude Sonnet 4.6" || aiModel == "Claude Sonnet 4.6 (Thinking)")
            {
                targetModel = "Claude Sonnet 4.6 (Thinking)";
            }
            else if (aiModel == "Claude Opus 4.6" || aiModel == "Claude Opus 4.6 (Thinking)")
            {
                targetModel = "Claude Opus 4.6 (Thinking)";
            }
            else if (aiModel == "GPT-OSS 120B" || aiModel == "GPT-OSS 120B (Medium)")
            {
                targetModel = "GPT-OSS 120B (Medium)";
            }
        }

        // 只有 opencode 才通过 --variant 传递级别；其他厂商级别已并入模型标识
        string? variant = aiProvider.Equals("opencode", StringComparison.OrdinalIgnoreCase)
            ? aiLevel
            : null;

        return (aiProvider, targetModel, variant);
    }

    /// <summary>
    /// 统一走框架侧 ICliChainService 调用 AI（文本情绪分析 / 强制翻译）。
    /// 复用页面所选 provider / model / variant，失败时按 CLI 链兜底；
    /// 通过 timeout 施加整体超时保护，避免 AI 阻塞日记保存。
    /// </summary>
    private async Task<string?> CallCliPromptAsync(string provider, string model, string promptText, TimeSpan timeout, string? variant = null)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            var result = await _cliChain.PromptAsync(
                promptText,
                projectName: "diary-companion",
                preferredProvider: provider,
                model: model,
                variant: variant,
                cancellationToken: cts.Token);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
            {
                return result.Text.Trim();
            }
            _logger.LogWarning("CLI 链调用返回空或失败 (provider {Provider}, model {Model})。原因: {Error}", provider, model, result.Error ?? "No output");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("CLI 链调用在 {Seconds} 秒后超时 (provider {Provider}, model {Model})", timeout.TotalSeconds, provider, model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "调用 CLI 链失败 (provider {Provider}, model {Model})", provider, model);
        }
        return null;
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
            var sql = "SELECT Title, Content, Weather, MoodBefore, Location, Sentiment, CreatedAt FROM DiaryEntry WHERE CreatedAt >= @BeginTime AND CreatedAt <= @EndTime ORDER BY CreatedAt ASC";
            var diaries = (await db.QueryAsync<DiaryRow>(sql, new { BeginTime = qStart, EndTime = qEnd }, tx)).ToList();

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
