using Microsoft.Data.Sqlite;

namespace FormForge.Services;

public static class DbInitializer
{
    public static async Task InitAsync(SqliteConnection conn)
    {
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;

            CREATE TABLE IF NOT EXISTS forms (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL DEFAULT 'Untitled Form',
                description TEXT,
                theme_color TEXT NOT NULL DEFAULT '#7C3AED',
                is_published INTEGER NOT NULL DEFAULT 0,
                accepts_responses INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS questions (
                id TEXT PRIMARY KEY,
                form_id TEXT NOT NULL,
                order_index INTEGER NOT NULL DEFAULT 0,
                type TEXT NOT NULL DEFAULT 'short_text',
                title TEXT NOT NULL DEFAULT 'Question',
                description TEXT,
                required INTEGER NOT NULL DEFAULT 0,
                options TEXT,
                scale_min INTEGER NOT NULL DEFAULT 1,
                scale_max INTEGER NOT NULL DEFAULT 5,
                scale_min_label TEXT,
                scale_max_label TEXT,
                FOREIGN KEY (form_id) REFERENCES forms(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS responses (
                id TEXT PRIMARY KEY,
                form_id TEXT NOT NULL,
                submitted_at TEXT NOT NULL,
                respondent_email TEXT,
                FOREIGN KEY (form_id) REFERENCES forms(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS answers (
                id TEXT PRIMARY KEY,
                response_id TEXT NOT NULL,
                question_id TEXT NOT NULL,
                value TEXT,
                FOREIGN KEY (response_id) REFERENCES responses(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_questions_form ON questions(form_id);
            CREATE INDEX IF NOT EXISTS idx_responses_form ON responses(form_id);
            CREATE INDEX IF NOT EXISTS idx_answers_response ON answers(response_id);
            CREATE INDEX IF NOT EXISTS idx_answers_question ON answers(question_id);
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
