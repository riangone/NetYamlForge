using Dapper;
using FormForge.Models;
using Microsoft.Data.Sqlite;

namespace FormForge.Services;

public class ResponseRepository(SqliteConnection db)
{
    public async Task<string> SubmitAsync(string formId, Dictionary<string, string> answers)
    {
        if (db.State != System.Data.ConnectionState.Open) await db.OpenAsync();
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

    public async Task<List<FormResponse>> GetByFormAsync(string formId)
    {
        if (db.State != System.Data.ConnectionState.Open) await db.OpenAsync();
        var responseRows = await db.QueryAsync(
            "SELECT * FROM responses WHERE form_id=@formId ORDER BY submitted_at DESC", new { formId });
        var responses = responseRows.Select(r => new FormResponse
        {
            Id = r.id, FormId = r.form_id,
            SubmittedAt = r.submitted_at,
            RespondentEmail = r.respondent_email
        }).ToList();

        if (responses.Count == 0) return responses;

        var ids = responses.Select(r => r.Id).ToArray();
        var answerRows = await db.QueryAsync(
            $"SELECT * FROM answers WHERE response_id IN ({string.Join(",", ids.Select((_, i) => $"@p{i}"))})",
            ids.Select((id, i) => new KeyValuePair<string, object>($"p{i}", id))
               .Aggregate(new DynamicParameters(), (dp, kv) => { dp.Add(kv.Key, kv.Value); return dp; }));

        var answersByResponse = answerRows
            .Select(a => new Answer { Id = a.id, ResponseId = a.response_id, QuestionId = a.question_id, Value = a.value })
            .GroupBy(a => a.ResponseId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var r in responses)
            r.Answers = answersByResponse.GetValueOrDefault(r.Id, []);

        return responses;
    }

    public async Task<List<QuestionSummary>> GetSummaryAsync(Form form)
    {
        var responses = await GetByFormAsync(form.Id);
        var summaries = new List<QuestionSummary>();

        foreach (var q in form.Questions)
        {
            var allAnswers = responses.SelectMany(r => r.Answers)
                .Where(a => a.QuestionId == q.Id && !string.IsNullOrEmpty(a.Value))
                .ToList();

            var summary = new QuestionSummary { Question = q, TotalAnswers = allAnswers.Count };

            if (q.HasOptions)
            {
                foreach (var opt in q.Options)
                    summary.OptionCounts[opt] = 0;
                foreach (var a in allAnswers)
                {
                    var values = a.Value!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var v in values)
                        if (summary.OptionCounts.ContainsKey(v)) summary.OptionCounts[v]++;
                }
            }
            else if (q.IsScale)
            {
                var nums = allAnswers.Select(a => double.TryParse(a.Value, out var n) ? (double?)n : null)
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
