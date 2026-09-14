// FormForge domain models, view-models, and request types compiled as part of NetYamlForge.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetYamlForge.Projects.FormForge;

public class FfForm
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled Form";
    public string? Description { get; set; }
    public string ThemeColor { get; set; } = "#7C3AED";
    public bool IsPublished { get; set; }
    public bool AcceptsResponses { get; set; } = true;
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public List<FfQuestion> Questions { get; set; } = [];
    public int ResponseCount { get; set; }
}

public class FfQuestion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FormId { get; set; } = "";
    public int OrderIndex { get; set; }
    public string Type { get; set; } = "short_text";
    public string Title { get; set; } = "Question";
    public string? Description { get; set; }
    public bool Required { get; set; }
    public List<string> Options { get; set; } = [];
    public int ScaleMin { get; set; } = 1;
    public int ScaleMax { get; set; } = 5;
    public string? ScaleMinLabel { get; set; }
    public string? ScaleMaxLabel { get; set; }

    [JsonIgnore]
    public bool HasOptions => Type is "multiple_choice" or "checkboxes" or "dropdown";
    [JsonIgnore]
    public bool IsScale => Type == "linear_scale";
}

public class FfResponse
{
    public string Id { get; set; } = "";
    public string FormId { get; set; } = "";
    public string SubmittedAt { get; set; } = "";
    public string? RespondentEmail { get; set; }
    public List<FfAnswer> Answers { get; set; } = [];
}

public class FfAnswer
{
    public string Id { get; set; } = "";
    public string ResponseId { get; set; } = "";
    public string QuestionId { get; set; } = "";
    public string? Value { get; set; }
}

public class FfQuestionSummary
{
    public FfQuestion Question { get; set; } = default!;
    public int TotalAnswers { get; set; }
    public Dictionary<string, int> OptionCounts { get; set; } = new();
    public double? Average { get; set; }
    public List<string> TextAnswers { get; set; } = [];
}

// ── View-models ──

public class FfFormListViewModel
{
    public List<FfForm> Forms { get; set; } = [];
    public string ProjectName { get; set; } = "";
}

public class FfBuilderViewModel
{
    public FfForm Form { get; set; } = default!;
    public string ProjectName { get; set; } = "";
}

public class FfResultsViewModel
{
    public FfForm Form { get; set; } = default!;
    public List<FfQuestionSummary> Summary { get; set; } = [];
    public List<FfResponse> Responses { get; set; } = [];
    public string ProjectName { get; set; } = "";
}

public class FfFillViewModel
{
    public FfForm Form { get; set; } = default!;
    public string? Error { get; set; }
    public string ProjectName { get; set; } = "";
}

// ── API request types ──

public class FfSaveFormRequest
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string ThemeColor { get; set; } = "#7C3AED";
    public bool AcceptsResponses { get; set; } = true;
    public List<FfQuestion> Questions { get; set; } = [];
}

public class FfPublishRequest
{
    public bool Published { get; set; }
}
