using NetYamlForge.Services.Common;
using Xunit;

namespace NetYamlForge.Tests.Common;

public class MarkdownRenderHelperTests
{
    [Fact]
    public void Render_NullOrEmpty_ReturnsFallbackSpan()
    {
        var result = MarkdownRenderHelper.Render(null, "暂无内容");
        Assert.Contains("暂无内容", result);
        Assert.Contains("italic", result);
    }

    [Fact]
    public void Render_HtmlInjection_IsEncodedSafely()
    {
        var malicious = "<script>alert('xss');</script><b>Bold</b>";
        var result = MarkdownRenderHelper.Render(malicious);

        Assert.DoesNotContain("<script>", result);
        Assert.Contains("&lt;script&gt;", result);
    }

    [Fact]
    public void Render_Headings_ConvertsToTailwindHeadings()
    {
        var md = "# 一级标题\n## 二级标题\n### 三级标题";
        var result = MarkdownRenderHelper.Render(md);

        Assert.Contains("<h2", result);
        Assert.Contains("一级标题", result);
        Assert.Contains("<h3", result);
        Assert.Contains("二级标题", result);
        Assert.Contains("<h4", result);
        Assert.Contains("三级标题", result);
    }

    [Fact]
    public void Render_CodeBlock_ProtectsInnerMarkdownAndOutputsCodeBox()
    {
        var md = "```csharp\nvar x = 10;\n// **not bold**\n```";
        var result = MarkdownRenderHelper.Render(md);

        Assert.Contains("CSHARP", result);
        Assert.Contains("var x = 10;", result);
        Assert.Contains("// **not bold**", result);
        Assert.DoesNotContain("<strong", result);
    }

    [Fact]
    public void Render_InlineFormatting_WorksCorrectly()
    {
        var md = "这是 **粗体** 和 `行内代码` 以及引用：\n> 敏捷开发宣言\n- 项目一\n1. 第一步";
        var result = MarkdownRenderHelper.Render(md);

        Assert.Contains("<strong", result);
        Assert.Contains("粗体</strong>", result);
        Assert.Contains("<code", result);
        Assert.Contains("行内代码</code>", result);
        Assert.Contains("<blockquote", result);
        Assert.Contains("敏捷开发宣言", result);
        Assert.Contains("项目一", result);
        Assert.Contains("第一步", result);
    }
}
