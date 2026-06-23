// FormForge data access, adapted from the standalone FormForge app.
// Uses IDbConnection (project-scoped by NetYamlForge) instead of SqliteConnection directly.

using System.Data;
using System.Text.Json;
using Dapper;

namespace NetYamlForge.Projects.FormForge;

public class FormForgeRepository(IDbConnection db)
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<FfForm>> GetAllAsync()
    {
#pragma warning disable DCS001
        var rows = await db.QueryAsync("""
            SELECT f.id, f.title, f.description, f.theme_color, f.is_published, f.accepts_responses,
                   f.created_at, f.updated_at,
                   (SELECT COUNT(*) FROM responses r WHERE r.form_id = f.id) AS response_count
            FROM forms f ORDER BY f.created_at DESC
            """);
#pragma warning restore DCS001
        return rows.Select(MapForm).ToList();
    }

    public async Task<FfForm?> GetByIdAsync(string id)
    {
        var rows = await db.QueryAsync("SELECT * FROM forms WHERE id = @id", new { id });
        var row = rows.FirstOrDefault();
        if (row == null) return null;
        var form = MapForm(row);

        var qrows = await db.QueryAsync(
            "SELECT * FROM questions WHERE form_id = @id ORDER BY order_index", new { id });
        form.Questions = qrows.Select(MapQuestion).ToList();
        return form;
    }

    public async Task<FfForm> CreateAsync()
    {
        var form = new FfForm
        {
            CreatedAt = DateTime.UtcNow.ToString("o"),
            UpdatedAt = DateTime.UtcNow.ToString("o")
        };
        await db.ExecuteAsync("""
            INSERT INTO forms (id, title, description, theme_color, is_published, accepts_responses, created_at, updated_at)
            VALUES (@Id, @Title, @Description, @ThemeColor, @IsPublished, @AcceptsResponses, @CreatedAt, @UpdatedAt)
            """, new
        {
            form.Id, form.Title, form.Description, form.ThemeColor,
            IsPublished = form.IsPublished ? 1 : 0,
            AcceptsResponses = form.AcceptsResponses ? 1 : 0,
            form.CreatedAt, form.UpdatedAt
        });
        return form;
    }

    public async Task SaveAsync(string id, FfSaveFormRequest req)
    {
        var now = DateTime.UtcNow.ToString("o");
        await db.ExecuteAsync("""
            UPDATE forms SET title=@Title, description=@Description, theme_color=@ThemeColor,
                accepts_responses=@AcceptsResponses, updated_at=@now WHERE id=@id
            """, new
        {
            req.Title, req.Description, req.ThemeColor,
            AcceptsResponses = req.AcceptsResponses ? 1 : 0,
            now, id
        });

        await db.ExecuteAsync("DELETE FROM questions WHERE form_id=@id", new { id });
        for (int i = 0; i < req.Questions.Count; i++)
        {
            var q = req.Questions[i];
            if (string.IsNullOrEmpty(q.Id)) q.Id = Guid.NewGuid().ToString("N");
            q.FormId = id;
            q.OrderIndex = i;
            var optJson = q.Options.Count > 0 ? JsonSerializer.Serialize(q.Options) : null;
            await db.ExecuteAsync("""
                INSERT INTO questions (id, form_id, order_index, type, title, description, required,
                    options, scale_min, scale_max, scale_min_label, scale_max_label)
                VALUES (@Id, @FormId, @OrderIndex, @Type, @Title, @Description, @Required,
                    @Options, @ScaleMin, @ScaleMax, @ScaleMinLabel, @ScaleMaxLabel)
                """, new
            {
                q.Id, q.FormId, q.OrderIndex, q.Type, q.Title, q.Description,
                Required = q.Required ? 1 : 0,
                Options = optJson, q.ScaleMin, q.ScaleMax, q.ScaleMinLabel, q.ScaleMaxLabel
            });
        }
    }

    public async Task PublishAsync(string id, bool published)
    {
        await db.ExecuteAsync(
            "UPDATE forms SET is_published=@p, updated_at=@now WHERE id=@id",
            new { p = published ? 1 : 0, now = DateTime.UtcNow.ToString("o"), id });
    }

    public async Task DeleteAsync(string id)
    {
        await db.ExecuteAsync("DELETE FROM forms WHERE id=@id", new { id });
    }

    private static FfForm MapForm(dynamic r) => new()
    {
        Id = r.id, Title = r.title, Description = r.description,
        ThemeColor = r.theme_color ?? "#7C3AED",
        IsPublished = (long)r.is_published == 1L,
        AcceptsResponses = (long)r.accepts_responses == 1L,
        CreatedAt = r.created_at, UpdatedAt = r.updated_at,
        ResponseCount = r.response_count == null ? 0 : (int)(long)r.response_count
    };

    private static FfQuestion MapQuestion(dynamic r)
    {
        List<string> opts = [];
        if (!string.IsNullOrEmpty((string?)r.options))
        {
            try { opts = JsonSerializer.Deserialize<List<string>>((string)r.options, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? []; }
            catch { }
        }
        return new FfQuestion
        {
            Id = r.id, FormId = r.form_id, OrderIndex = (int)(long)r.order_index,
            Type = r.type, Title = r.title, Description = r.description,
            Required = (long)r.required == 1L, Options = opts,
            ScaleMin = (int)(long)r.scale_min, ScaleMax = (int)(long)r.scale_max,
            ScaleMinLabel = r.scale_min_label, ScaleMaxLabel = r.scale_max_label
        };
    }
}

public class FormForgeResponseRepository(IDbConnection db)
{
    public async Task<string> SubmitAsync(string formId, Dictionary<string, string> answers)
    {
        var responseId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow.ToString("o");

        await db.ExecuteAsync(
            "INSERT INTO responses (id, form_id, submitted_at) VALUES (@id, @formId, @now)",
            new { id = responseId, formId, now });

        foreach (var (questionId, value) in answers)
        {
            await db.ExecuteAsync(
                "INSERT INTO answers (id, response_id, question_id, value) VALUES (@id, @responseId, @questionId, @value)",
                new { id = Guid.NewGuid().ToString("N"), responseId, questionId, value });
        }
        return responseId;
    }

    public async Task<List<FfResponse>> GetByFormAsync(string formId)
    {
        var responseRows = await db.QueryAsync(
            "SELECT * FROM responses WHERE form_id=@formId ORDER BY submitted_at DESC", new { formId });
        var responses = responseRows.Select(r => new FfResponse
        {
            Id = r.id, FormId = r.form_id,
            SubmittedAt = r.submitted_at,
            RespondentEmail = r.respondent_email
        }).ToList();

        if (responses.Count == 0) return responses;

        var dp = new DynamicParameters();
        var placeholders = new List<string>();
        for (int i = 0; i < responses.Count; i++)
        {
            var key = $"p{i}";
            dp.Add(key, responses[i].Id);
            placeholders.Add($"@{key}");
        }
#pragma warning disable DCS001
        var answerRows = await db.QueryAsync(
            $"SELECT * FROM answers WHERE response_id IN ({string.Join(",", placeholders)})", dp);
#pragma warning restore DCS001

        var answersByResponse = answerRows
            .Select(a => new FfAnswer { Id = a.id, ResponseId = a.response_id, QuestionId = a.question_id, Value = a.value })
            .GroupBy(a => a.ResponseId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var r in responses)
            r.Answers = answersByResponse.GetValueOrDefault(r.Id, []);

        return responses;
    }

    public async Task<List<FfQuestionSummary>> GetSummaryAsync(FfForm form)
    {
        var responses = await GetByFormAsync(form.Id);
        var summaries = new List<FfQuestionSummary>();

        foreach (var q in form.Questions)
        {
            var allAnswers = responses.SelectMany(r => r.Answers)
                .Where(a => a.QuestionId == q.Id && !string.IsNullOrEmpty(a.Value))
                .ToList();

            var summary = new FfQuestionSummary { Question = q, TotalAnswers = allAnswers.Count };

            if (q.HasOptions)
            {
                foreach (var opt in q.Options) summary.OptionCounts[opt] = 0;
                foreach (var a in allAnswers)
                {
                    var values = a.Value!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var v in values)
                        if (summary.OptionCounts.ContainsKey(v)) summary.OptionCounts[v]++;
                }
            }
            else if (q.IsScale)
            {
                var nums = allAnswers
                    .Select(a => double.TryParse(a.Value, out var n) ? (double?)n : null)
                    .Where(n => n.HasValue).Select(n => n!.Value).ToList();
                if (nums.Count > 0) summary.Average = Math.Round(nums.Average(), 2);
            }
            else
            {
                summary.TextAnswers = allAnswers.Select(a => a.Value!).ToList();
            }
            summaries.Add(summary);
        }
        return summaries;
    }
}
