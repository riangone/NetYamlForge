using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;
using NetYamlForge.Services.AI;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace NetYamlForge.Projects.PhotoVocab.Hooks;

public class VocabWordItem
{
    public string Word { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Example { get; set; } = "";
}

public class PhotoVocabAnalysisResult
{
    public string Caption { get; set; } = "";
    public List<VocabWordItem> Words { get; set; } = new();
}

/// <summary>
/// 上传图片后，通过 AI 生成图片描述，并从图片内容中提炼出若干背单词素材。
/// BeforeAsync 负责生成缩略图与调用 AI 分析；AfterAsync 在拿到新记录 ID 后写入 VocabWord 表。
/// </summary>
public class AnalyzePhotoAndExtractVocabHook : IEntityHook
{
    private readonly ICliChainService _cliChain;
    private readonly ILogger<AnalyzePhotoAndExtractVocabHook> _logger;

    public string Name => "analyze_photo_and_extract_vocab";

    // 目标语言（AnnotationLanguage）：Word / Caption / Example 使用该语言书写，是背单词要记住的内容。
    private static readonly Dictionary<string, string> LanguageNames = new()
    {
        ["zh-CN"] = "简体中文",
        ["en-US"] = "English",
        ["ja-JP"] = "日本語",
        ["ko-KR"] = "한국어",
    };

    public AnalyzePhotoAndExtractVocabHook(ICliChainService cliChain, ILogger<AnalyzePhotoAndExtractVocabHook> logger)
    {
        _cliChain = cliChain;
        _logger = logger;
    }

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var imageBase64 = ctx.Values.GetValueOrDefault("ImageBase64")?.ToString();
        var language = ctx.Values.GetValueOrDefault("AnnotationLanguage")?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(language) || !LanguageNames.ContainsKey(language))
        {
            language = "en-US";
            ctx.Values["AnnotationLanguage"] = language;
        }

        // 释义语言固定使用系统当前选择的界面语言（与 Quiz 页面挑战时一致），
        // 这样即使 Word 是目标语言（例如英语），Meaning/释义也能让用户看懂，
        // 不会出现“标注是英语，答题选项也全是英语”的情况。
        var nativeLanguage = System.Globalization.CultureInfo.CurrentUICulture.Name;
        if (!LanguageNames.ContainsKey(nativeLanguage))
        {
            nativeLanguage = "zh-CN";
        }

        if (string.IsNullOrWhiteSpace(imageBase64))
        {
            return HookResult.Abort("请先上传一张图片。");
        }

        // 生成缩略图，减少列表页流量消耗
        try
        {
            var base64Data = imageBase64;
            if (base64Data.Contains(","))
            {
                base64Data = base64Data.Substring(base64Data.IndexOf(",") + 1);
            }

            var bytes = Convert.FromBase64String(base64Data);
            using (var image = Image.Load(bytes))
            {
                int maxDim = 640;
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
                    image.SaveAsWebp(ms, new SixLabors.ImageSharp.Formats.Webp.WebpEncoder { Quality = 75 });
                    var thumbBase64 = Convert.ToBase64String(ms.ToArray());
                    ctx.Values["ImageThumbnailBase64"] = "data:image/webp;base64," + thumbBase64;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成图片缩略图失败");
            ctx.Values["ImageThumbnailBase64"] = imageBase64;
        }

        // 调用 AI：为图片生成描述，并提炼若干背单词素材
        var langName = LanguageNames[language];
        var nativeLangName = LanguageNames[nativeLanguage];
        string tempFilePath = System.IO.Path.Combine(
            System.IO.Directory.GetCurrentDirectory(),
            $"temp_photo_vocab_{Guid.NewGuid():N}.jpg");

        List<VocabWordItem> extractedWords = new();
        string caption = "";

        try
        {
            var rawBase64 = imageBase64.Contains(",")
                ? imageBase64.Substring(imageBase64.IndexOf(",") + 1)
                : imageBase64;
            await System.IO.File.WriteAllBytesAsync(tempFilePath, Convert.FromBase64String(rawBase64));

            var prompt = $$"""
这是一张用于语言学习的素材图片。
目标语言（要背诵记忆的语言）：{{langName}}（{{language}}）。
释义语言（用户能看懂的语言，用于解释目标语言）：{{nativeLangName}}（{{nativeLanguage}}）。

请完成两件事：
1. 用目标语言（{{langName}}）写一句简短的图片描述（Caption，20-40 字左右）。
2. 从图片内容中挑选 3 到 6 个适合背诵的目标语言单词（Word），为每个单词提供：
   - Word：目标语言（{{langName}}）的单词或短语本身，这是用户要记住的内容。
   - Meaning：该单词的释义/翻译，必须使用释义语言（{{nativeLangName}}）书写，不要使用目标语言，
     这样用户即使完全不认识目标语言，也能通过 Meaning 看懂这个单词的意思。
   - Example：一句包含该单词的目标语言（{{langName}}）例句，用来展示该单词的真实用法。

注意：如果目标语言和释义语言相同（{{language}} = {{nativeLanguage}}），Word/Caption/Example/Meaning 均使用该语言即可。

请输出合法的 JSON（不要包含 markdown 代码块标记如 ```json，不要输出任何额外说明文字），结构如下：
{
  "Caption": "...",
  "Words": [
    { "Word": "...", "Meaning": "...", "Example": "..." }
  ]
}
""";

            // 图片分析为可选功能：可通过 projects/photo-vocab/.env 的
            // CLI_CHAIN_ENABLED=false 整体关闭，或用 CLI_CHAIN_ORDER 调整/裁剪优先级。
            // 默认优先顺序：opencode cli → antigravity cli → claude code cli
            // （均为订阅制 CLI，不使用按量计费的 API，避免费用不可控）。
            // 链路内部对每个 CLI 各自有 90 秒超时并自动 fallback 到下一个，
            // 这里不再叠加一层自造的 20 秒计时器（那样只会导致"判负但后台任务还在跑"的假超时）。
            var chainResult = await _cliChain.PromptAsync(prompt, imagePath: tempFilePath, projectName: "photo-vocab");
            if (chainResult.Success && !string.IsNullOrWhiteSpace(chainResult.Text))
            {
                var result = ParseAnalysisResult(chainResult.Text);
                if (result != null)
                {
                    caption = result.Caption?.Trim() ?? "";
                    extractedWords = (result.Words ?? new())
                        .Where(w => !string.IsNullOrWhiteSpace(w.Word))
                        .Take(6)
                        .ToList();
                    _logger.LogInformation("AI 成功分析了图片并提取了 {Count} 个单词（via {Provider}）", extractedWords.Count, chainResult.Provider);
                }
                else
                {
                    _logger.LogError("AI 返回内容无法解析为 JSON，跳过本次自动标注。原始内容: {Text}", chainResult.Text);
                }
            }
            else
            {
                _logger.LogWarning("图片自动分析未成功，已跳过（不影响图片保存）。原因: {Error}", chainResult.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "调用 AI 分析图片并提取单词失败");
        }
        finally
        {
            if (System.IO.File.Exists(tempFilePath))
            {
                try { System.IO.File.Delete(tempFilePath); } catch { }
            }
        }

        ctx.Values["Caption"] = caption;
        ctx.Values["WordCount"] = extractedWords.Count;

        // BeforeAsync -> AfterAsync 之间传递，供插入 VocabWord 时使用
        ctx.Data["ExtractedWords"] = extractedWords;
        ctx.Data["Language"] = language;
        ctx.Data["NativeLanguage"] = nativeLanguage;

        return HookResult.Continue();
    }

    /// <summary>
    /// ICliChainService は生の文本しか返さない（IAntigravityCliService.PromptJsonAsync のような
    /// 汎用 JSON パースは提供しない）ため、ここで同等のロジックを実装する：
    /// markdown コードブロック記号を除去し、最初の '{' から最後の '}' までを JSON として解析する。
    /// </summary>
    private PhotoVocabAnalysisResult? ParseAnalysisResult(string text)
    {
        try
        {
            var cleaned = Regex.Replace(text, @"```(?:json)?\s*", "", RegexOptions.IgnoreCase).Trim();
            var start = cleaned.IndexOf('{');
            var end = cleaned.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                var json = cleaned.Substring(start, end - start + 1);
                return JsonSerializer.Deserialize<PhotoVocabAnalysisResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "解析 AI JSON 返回内容失败: {Text}", text);
        }
        return null;
    }

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create || ctx.Id == null)
            return;

        if (ctx.Data.GetValueOrDefault("ExtractedWords") is not List<VocabWordItem> words || words.Count == 0)
            return;

        var language = ctx.Data.GetValueOrDefault("Language")?.ToString() ?? "en-US";
        var nativeLanguage = ctx.Data.GetValueOrDefault("NativeLanguage")?.ToString() ?? "zh-CN";
        var now = DateTime.Now;

        foreach (var w in words)
        {
            try
            {
                await db.ExecuteAsync(
                    @"INSERT INTO VocabWord (PhotoEntryId, Word, Meaning, Example, Language, NativeLanguage, CreatedAt)
                      VALUES (@PhotoEntryId, @Word, @Meaning, @Example, @Language, @NativeLanguage, @CreatedAt)",
                    new
                    {
                        PhotoEntryId = ctx.Id.Value,
                        Word = w.Word.Trim(),
                        Meaning = w.Meaning?.Trim() ?? "",
                        Example = w.Example?.Trim() ?? "",
                        Language = language,
                        NativeLanguage = nativeLanguage,
                        CreatedAt = now
                    },
                    tx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "写入背单词素材失败: Word={Word}", w.Word);
            }
        }
    }
}
