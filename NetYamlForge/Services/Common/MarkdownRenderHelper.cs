using System.Net;
using System.Text.RegularExpressions;

namespace NetYamlForge.Services.Common;

/// <summary>
/// 服务端天生安全 (Secure-by-Construction) 的轻量级 Markdown 渲染器。
/// 先行 HtmlEncode 消解注入风险，再通过受控正则映射为 Tailwind / daisyUI 样式 HTML。
/// </summary>
public static class MarkdownRenderHelper
{
    private static readonly Regex CodeBlockRegex = new(
        @"```([a-zA-Z0-9_\-#+]*)\r?\n([\s\S]*?)```",
        RegexOptions.Compiled);

    private static readonly Regex BlockquoteRegex = new(
        @"^(?:&gt;|>)\s?(.*)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex H4Regex = new(@"^####\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex H3Regex = new(@"^###\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex H2Regex = new(@"^##\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex H1Regex = new(@"^#\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex BoldRegex = new(@"\*\*([^*]+)\*\*", RegexOptions.Compiled);
    private static readonly Regex InlineCodeRegex = new(@"`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex UnorderedListRegex = new(@"^\s*[-*]\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex OrderedListRegex = new(@"^\s*(\d+)\.\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// 将原生 Markdown 渲染为安全的 HTML 字符串
    /// </summary>
    public static string Render(string? rawText, string emptyFallback = "（无内容）")
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return $"<span class=\"text-base-content/40 italic\">{WebUtility.HtmlEncode(emptyFallback)}</span>";
        }

        // 1. 全局先进行 HTML 安全转义，彻底防御 XSS
        var text = WebUtility.HtmlEncode(rawText);

        // 2. 占位保护代码块，避免代码块内部的 markdown 标记被替换
        var codeBlocks = new List<string>();
        text = CodeBlockRegex.Replace(text, match =>
        {
            var lang = match.Groups[1].Value.Trim();
            var content = match.Groups[2].Value;
            var langBadge = string.IsNullOrEmpty(lang) ? "" : $"<div class=\"text-[11px] font-mono font-bold text-base-content/60 uppercase tracking-wider mb-1\">{WebUtility.HtmlEncode(lang.ToUpperInvariant())}</div>";
            var blockHtml = $"<div class=\"my-3 rounded-2xl bg-base-300/80 p-3.5 border border-base-content/10 font-mono text-xs overflow-x-auto shadow-inner\">{langBadge}<pre class=\"leading-relaxed\"><code>{content}</code></pre></div>";
            var placeholder = $"__CODE_BLOCK_{codeBlocks.Count}__";
            codeBlocks.Add(blockHtml);
            return placeholder;
        });

        // 3. 引用块
        text = BlockquoteRegex.Replace(text, "<blockquote class=\"my-2 border-l-4 border-primary/70 bg-base-200/50 rounded-r-xl px-3 py-1.5 text-xs text-base-content/80 italic\">$1</blockquote>");

        // 4. 标题解析
        text = H4Regex.Replace(text, "<h5 class=\"text-sm font-bold text-base-content mt-2.5 mb-1\">$1</h5>");
        text = H3Regex.Replace(text, "<h4 class=\"text-base font-bold text-primary mt-3 mb-1.5\">$1</h4>");
        text = H2Regex.Replace(text, "<h3 class=\"text-lg font-extrabold text-base-content mt-4 mb-2 pb-1 border-b border-base-300\">$1</h3>");
        text = H1Regex.Replace(text, "<h2 class=\"text-xl font-black text-base-content mt-5 mb-2.5 pb-1.5 border-b-2 border-primary/30\">$1</h2>");

        // 5. 行内粗体与行内代码
        text = BoldRegex.Replace(text, "<strong class=\"font-bold text-base-content\">$1</strong>");
        text = InlineCodeRegex.Replace(text, "<code class=\"px-1.5 py-0.5 rounded bg-base-200 text-primary font-mono text-xs font-semibold\">$1</code>");

        // 6. 列表
        text = UnorderedListRegex.Replace(text, "<div class=\"flex items-start gap-2 my-0.5 text-sm\"><span class=\"text-primary font-bold leading-5\">•</span><span class=\"leading-5\">$1</span></div>");
        text = OrderedListRegex.Replace(text, "<div class=\"flex items-start gap-2 my-0.5 text-sm\"><span class=\"font-mono text-primary font-bold text-[11px] bg-primary/10 px-1 rounded leading-4 mt-0.5\">$1</span><span class=\"leading-5\">$2</span></div>");

        // 7. 还原代码块
        for (int i = 0; i < codeBlocks.Count; i++)
        {
            text = text.Replace($"__CODE_BLOCK_{i}__", codeBlocks[i]);
        }

        // 8. 换行与连续断行转换
        text = text.Replace("\r\n", "\n").Replace("\n", "<br/>");

        // 优化已有的块级容器后面的多余 <br/>
        text = text.Replace("</div><br/>", "</div>");
        text = text.Replace("</h2><br/>", "</h2>");
        text = text.Replace("</h3><br/>", "</h3>");
        text = text.Replace("</h4><br/>", "</h4>");
        text = text.Replace("</h5><br/>", "</h5>");
        text = text.Replace("</blockquote><br/>", "</blockquote>");

        return text;
    }
}
