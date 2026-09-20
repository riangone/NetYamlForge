using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace NetYamlForge.Projects.KbForge;

public class KbArticle
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "Untitled";
    public string Content { get; set; } = "";
    public string? Summary { get; set; }
    public string Status { get; set; } = "draft";
    public string Author { get; set; } = "";
    public int ViewCount { get; set; }
    public string EmbeddingStatus { get; set; } = "pending";
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public List<string> Tags { get; set; } = [];

    [JsonIgnore]
    public bool IsPublished => Status == "published";

    [JsonIgnore]
    public string StatusBadgeClass => Status switch
    {
        "published" => "badge-success",
        "archived"  => "badge-neutral",
        _           => "badge-warning"
    };
}

public class KbTag
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public int ArticleCount { get; set; }
}

public class KbRevision
{
    public string Id { get; set; } = "";
    public string ArticleId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string ChangedBy { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public class KbSearchResult
{
    public KbArticle Article { get; set; } = default!;
    public float Score { get; set; }
    public bool IsSemantic { get; set; }
}

// ── View-models ──

public class KbArticleListViewModel
{
    public List<KbArticle> Articles { get; set; } = [];
    public List<KbTag> Tags { get; set; } = [];
    public string? FilterTag { get; set; }
    public string? FilterStatus { get; set; }
    public string ProjectName { get; set; } = "";
}

public class KbArticleEditViewModel
{
    public KbArticle Article { get; set; } = default!;
    public List<KbTag> AllTags { get; set; } = [];
    public bool IsNew { get; set; }
    public string ProjectName { get; set; } = "";
}

public class KbArticleViewViewModel
{
    public KbArticle Article { get; set; } = default!;
    public List<KbRevision> Revisions { get; set; } = [];
    public string ProjectName { get; set; } = "";
}

public class KbSearchViewModel
{
    public string? Query { get; set; }
    public bool Semantic { get; set; }
    public List<KbSearchResult> Results { get; set; } = [];
    public string ProjectName { get; set; } = "";
}

// ── API request types ──

public class KbSaveArticleRequest
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string? Summary { get; set; }
    public string Status { get; set; } = "draft";
    public string TagsCsv { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public List<string> Tags => TagsCsv
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();
}
