namespace FormForge.Models;

public class FormResponse
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FormId { get; set; } = "";
    public string SubmittedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public string? RespondentEmail { get; set; }
    public List<Answer> Answers { get; set; } = [];
}

public class Answer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ResponseId { get; set; } = "";
    public string QuestionId { get; set; } = "";
    public string? Value { get; set; }
}
