namespace FormForge.Models;

public class Form
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled Form";
    public string? Description { get; set; }
    public string ThemeColor { get; set; } = "#7C3AED";
    public bool IsPublished { get; set; }
    public bool AcceptsResponses { get; set; } = true;
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public List<Question> Questions { get; set; } = [];
    public int ResponseCount { get; set; }
}
