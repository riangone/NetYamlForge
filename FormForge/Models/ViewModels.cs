namespace FormForge.Models;

public class FormBuilderViewModel
{
    public Form Form { get; set; } = new();
}

public class FormFillViewModel
{
    public Form Form { get; set; } = new();
    public string? Error { get; set; }
}

public class FormResultsViewModel
{
    public Form Form { get; set; } = new();
    public List<FormResponse> Responses { get; set; } = [];
    public List<QuestionSummary> Summary { get; set; } = [];
}

public class QuestionSummary
{
    public Question Question { get; set; } = new();
    public int TotalAnswers { get; set; }
    public Dictionary<string, int> OptionCounts { get; set; } = new();
    public List<string> TextAnswers { get; set; } = [];
    public double? Average { get; set; }
}

public class SaveFormRequest
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string ThemeColor { get; set; } = "#7C3AED";
    public bool AcceptsResponses { get; set; } = true;
    public List<Question> Questions { get; set; } = [];
}
