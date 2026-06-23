namespace FormForge.Models;

public class Question
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

    public bool HasOptions => Type is "multiple_choice" or "checkboxes" or "dropdown";
    public bool IsScale => Type == "linear_scale";
    public bool IsText => Type is "short_text" or "paragraph";
}
