using Dapper;
using FormForge.Models;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace FormForge.Services;

public class FormRepository(SqliteConnection db)
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<Form>> GetAllAsync()
    {
        if (db.State != System.Data.ConnectionState.Open) await db.OpenAsync();
        var rows = await db.QueryAsync("""
            SELECT f.id, f.title, f.description, f.theme_color, f.is_published, f.accepts_responses,
                   f.created_at, f.updated_at,
                   (SELECT COUNT(*) FROM responses r WHERE r.form_id = f.id) AS response_count
            FROM forms f ORDER BY f.created_at DESC
            """);
        return rows.Select(MapForm).ToList();
    }

    public async Task<Form?> GetByIdAsync(string id)
    {
        if (db.State != System.Data.ConnectionState.Open) await db.OpenAsync();
        var rows = await db.QueryAsync("SELECT * FROM forms WHERE id = @id", new { id });
        var row = rows.FirstOrDefault();
        if (row == null) return null;
        var form = MapForm(row);

        var qrows = await db.QueryAsync(
            "SELECT * FROM questions WHERE form_id = @id ORDER BY order_index", new { id });
        form.Questions = qrows.Select(MapQuestion).ToList();
        return form;
    }

    public async Task<Form> CreateAsync()
    {
        if (db.State != System.Data.ConnectionState.Open) await db.OpenAsync();
        var form = new Form { CreatedAt = DateTime.UtcNow.ToString("o"), UpdatedAt = DateTime.UtcNow.ToString("o") };
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

    public async Task SaveAsync(string id, SaveFormRequest req)
    {
        if (db.State != System.Data.ConnectionState.Open) await db.OpenAsync();
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
        if (db.State != System.Data.ConnectionState.Open) await db.OpenAsync();
        await db.ExecuteAsync(
            "UPDATE forms SET is_published=@p, updated_at=@now WHERE id=@id",
            new { p = published ? 1 : 0, now = DateTime.UtcNow.ToString("o"), id });
    }

    public async Task DeleteAsync(string id)
    {
        if (db.State != System.Data.ConnectionState.Open) await db.OpenAsync();
        await db.ExecuteAsync("DELETE FROM forms WHERE id=@id", new { id });
    }

    private static Form MapForm(dynamic r) => new()
    {
        Id = r.id, Title = r.title, Description = r.description,
        ThemeColor = r.theme_color ?? "#7C3AED",
        IsPublished = (long)r.is_published == 1L,
        AcceptsResponses = (long)r.accepts_responses == 1L,
        CreatedAt = r.created_at, UpdatedAt = r.updated_at,
        ResponseCount = r.response_count == null ? 0 : (int)(long)r.response_count
    };

    private static Question MapQuestion(dynamic r)
    {
        List<string> opts = [];
        if (!string.IsNullOrEmpty((string?)r.options))
        {
            try { opts = JsonSerializer.Deserialize<List<string>>((string)r.options) ?? []; }
            catch { }
        }
        return new Question
        {
            Id = r.id, FormId = r.form_id, OrderIndex = (int)(long)r.order_index,
            Type = r.type, Title = r.title, Description = r.description,
            Required = (long)r.required == 1L, Options = opts,
            ScaleMin = (int)(long)r.scale_min, ScaleMax = (int)(long)r.scale_max,
            ScaleMinLabel = r.scale_min_label, ScaleMaxLabel = r.scale_max_label
        };
    }
}
