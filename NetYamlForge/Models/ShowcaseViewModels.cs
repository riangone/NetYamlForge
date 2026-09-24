using System.Collections.Generic;

namespace NetYamlForge.Models;

/// <summary>
/// 样板间场景项模型
/// </summary>
public class ShowcaseItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string CategoryLabel { get; set; } = string.Empty;
    public string Badge { get; set; } = string.Empty;
    public string BadgeClass { get; set; } = "badge-primary";
    public string Icon { get; set; } = string.Empty;
    public string Tagline { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TargetAudience { get; set; } = string.Empty;
    public string ArchitectInsight { get; set; } = string.Empty;
    public List<string> Highlights { get; set; } = new();
    public List<string> TechTags { get; set; } = new();
    public string PrimaryActionUrl { get; set; } = string.Empty;
    public string PrimaryActionLabel { get; set; } = "进入体验";
    public string? SecondaryActionUrl { get; set; }
    public string? SecondaryActionLabel { get; set; }
    public string CodeSnippetTitle { get; set; } = string.Empty;
    public string CodeLanguage { get; set; } = "yaml";
    public string CodeSnippet { get; set; } = string.Empty;
    public bool IsBlueprintOnly { get; set; } = false;
}

/// <summary>
/// 样板间体验馆主页面视图模型
/// </summary>
public class ShowcaseIndexViewModel
{
    public List<ShowcaseItemViewModel> Showcases { get; set; } = new();
    public string? CurrentCategory { get; set; }
    public bool IsAuthenticated { get; set; }
    public string? CurrentUserProject { get; set; }
    public string PromotionalCopyMarkdown { get; set; } = string.Empty;
}
