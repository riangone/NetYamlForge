using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.EnglishLearning.Hooks;

/// <summary>
/// Custom action handler to dynamically generate today's lesson.
/// Based on the user's current level, it selects vocabulary words and exercises,
/// then schedules a DailyLesson record for today.
/// </summary>
public class GenerateDailyLessonHandler : ICustomActionHandler
{
    public string Name => "generate_daily_lesson";

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId))
            return ActionHandlerResult.Failure("User profile ID is missing.");

        if (!int.TryParse(ctx.RecordId, out var profileId))
            return ActionHandlerResult.Failure("Invalid profile ID.");

        // 1. Get user profile
        var profile = await db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM LearningProfile WHERE Id = @id",
            new { id = profileId },
            tx);

        if (profile == null)
            return ActionHandlerResult.Failure("User profile not found.");

        string username = profile.UserName;
        int level = (int)profile.CurrentLevel;
        string todayStr = DateTime.Today.ToString("yyyy-MM-dd");

        // 2. Check if a lesson is already generated for today
        var existingLesson = await db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM DailyLesson WHERE UserName = @username AND LessonDate = @todayStr",
            new { username, todayStr },
            tx);

        if (existingLesson != null)
            return ActionHandlerResult.Failure("Today's lesson has already been generated.");

        // 3. Select words for this level
        var wordIdsList = await db.QueryAsync<int>(
            "SELECT Id FROM EnglishWord WHERE Difficulty = @level ORDER BY RANDOM() LIMIT 5",
            new { level },
            tx);

        if (!wordIdsList.Any())
        {
            // Fallback: grab any words if none exist for this level
            wordIdsList = await db.QueryAsync<int>(
                "SELECT Id FROM EnglishWord ORDER BY RANDOM() LIMIT 5",
                null,
                tx);
        }

        // 4. Select exercises for this level
        var exerciseIdsList = await db.QueryAsync<int>(
            "SELECT Id FROM EnglishExercise WHERE Difficulty = @level ORDER BY RANDOM() LIMIT 4",
            new { level },
            tx);

        if (!exerciseIdsList.Any())
        {
            // Fallback: grab any exercises if none exist for this level
            exerciseIdsList = await db.QueryAsync<int>(
                "SELECT Id FROM EnglishExercise ORDER BY RANDOM() LIMIT 4",
                null,
                tx);
        }

        string wordIds = string.Join(",", wordIdsList);
        string exerciseIds = string.Join(",", exerciseIdsList);

        // 5. Insert DailyLesson record
        await db.ExecuteAsync(
            @"INSERT INTO DailyLesson (UserName, LessonDate, TargetLevel, WordIds, ExerciseIds, Status, Score)
              VALUES (@username, @todayStr, @level, @wordIds, @exerciseIds, 'pending', 0)",
            new { username, todayStr, level, wordIds, exerciseIds },
            tx);

        return ActionHandlerResult.Success();
    }
}

/// <summary>
/// Custom action handler to submit lesson results.
/// Calculates XP, updates the DailyLesson status, records study history,
/// updates user profile (XP, level up, daily streak).
/// </summary>
public class SubmitLessonResultsHandler : ICustomActionHandler
{
    public string Name => "submit_lesson_results";

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId))
            return ActionHandlerResult.Failure("Lesson ID is missing.");

        if (!int.TryParse(ctx.RecordId, out var lessonId))
            return ActionHandlerResult.Failure("Invalid lesson ID.");

        // 1. Retrieve the lesson
        var lesson = await db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM DailyLesson WHERE Id = @id",
            new { id = lessonId },
            tx);

        if (lesson == null)
            return ActionHandlerResult.Failure("Lesson not found.");

        string status = lesson.Status;
        if (status.Equals("completed", StringComparison.OrdinalIgnoreCase))
            return ActionHandlerResult.Failure("This lesson has already been completed.");

        // 2. Read inputs
        if (!ctx.Inputs.TryGetValue("CorrectAnswers", out var correctObj) || correctObj == null ||
            !ctx.Inputs.TryGetValue("TotalQuestions", out var totalObj) || totalObj == null)
        {
            return ActionHandlerResult.Failure("Correct and Total questions counts are required.");
        }

        int correctAnswers, totalQuestions;
        try
        {
            correctAnswers = ToInt32Flexible(correctObj);
            totalQuestions = ToInt32Flexible(totalObj);
        }
        catch (Exception)
        {
            return ActionHandlerResult.Failure("Correct and Total questions counts must be valid integers.");
        }

        if (totalQuestions <= 0)
            return ActionHandlerResult.Failure("Total questions must be greater than zero.");
        if (correctAnswers < 0 || correctAnswers > totalQuestions)
            return ActionHandlerResult.Failure("Correct answers must be between 0 and total questions.");

        // 3. Calculate XP (e.g. 10 XP per correct answer)
        int xpEarned = correctAnswers * 10;
        string todayStr = DateTime.Today.ToString("yyyy-MM-dd");
        string nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string username = lesson.UserName;

        // 4. Update DailyLesson
        await db.ExecuteAsync(
            "UPDATE DailyLesson SET Status = 'completed', Score = @xpEarned WHERE Id = @id",
            new { xpEarned, id = lessonId },
            tx);

        // 5. Record study history
        await db.ExecuteAsync(
            @"INSERT INTO LearningHistory (UserName, LessonId, CompletedAt, TotalQuestions, CorrectAnswers, XpEarned)
              VALUES (@username, @lessonId, @nowStr, @totalQuestions, @correctAnswers, @xpEarned)",
            new { username, lessonId, nowStr, totalQuestions, correctAnswers, xpEarned },
            tx);

        // 6. Update user profile (XP, Streak, Level Up)
        var profile = await db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM LearningProfile WHERE UserName = @username",
            new { username },
            tx);

        if (profile == null)
        {
            // Create user profile if missing
            int initialLevel = (int)lesson.TargetLevel;
            await db.ExecuteAsync(
                @"INSERT INTO LearningProfile (UserName, CurrentLevel, TotalXp, Streak, LastActiveDate, CompletedLessonsCount)
                  VALUES (@username, @initialLevel, @xpEarned, 1, @todayStr, 1)",
                new { username, initialLevel, xpEarned, todayStr },
                tx);
        }
        else
        {
            int currentLevel = (int)profile.CurrentLevel;
            int totalXp = (int)profile.TotalXp + xpEarned;
            int completedCount = (int)profile.CompletedLessonsCount + 1;
            int currentStreak = (int)profile.Streak;
            string lastActiveDateStr = profile.LastActiveDate;

            // Calculate Streak
            if (!string.IsNullOrWhiteSpace(lastActiveDateStr) && DateTime.TryParse(lastActiveDateStr, out var lastActiveDate))
            {
                var diffDays = (DateTime.Today - lastActiveDate.Date).Days;
                if (diffDays == 1)
                {
                    currentStreak += 1;
                }
                else if (diffDays > 1)
                {
                    currentStreak = 1; // reset streak if gap is more than 1 day
                }
                // if diffDays == 0, keep streak same
            }
            else
            {
                currentStreak = 1;
            }

            // Level up threshold (e.g. Level 1: 0-100 XP, Level 2: 101-300 XP, Level 3: 301-600 XP, Level 4: 601-1000 XP, Level 5: 1001+ XP)
            int newLevel = currentLevel;
            if (totalXp > 1000) newLevel = 5;
            else if (totalXp > 600) newLevel = 4;
            else if (totalXp > 300) newLevel = 3;
            else if (totalXp > 100) newLevel = 2;

            if (newLevel > currentLevel)
            {
                // Trigger Level up info in system / logs
                newLevel = Math.Min(newLevel, 5); // cap at level 5
            }

            await db.ExecuteAsync(
                @"UPDATE LearningProfile
                  SET CurrentLevel = @newLevel,
                      TotalXp = @totalXp,
                      Streak = @currentStreak,
                      LastActiveDate = @todayStr,
                      CompletedLessonsCount = @completedCount
                  WHERE UserName = @username",
                new { newLevel, totalXp, currentStreak, todayStr, completedCount, username },
                tx);
        }

        return ActionHandlerResult.Success();
    }

    /// <summary>
    /// Converts an action input value to Int32, tolerating both the string-typed values that arrive
    /// from the entity-grid form submission path and the System.Text.Json.JsonElement values that
    /// arrive from the generic JSON REST API (/api/{project}/{entity}/{id}/actions/{actionKey}),
    /// which Convert.ToInt32 cannot handle directly (JsonElement does not implement IConvertible).
    /// </summary>
    private static int ToInt32Flexible(object value)
    {
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.Number => je.GetInt32(),
                JsonValueKind.String when int.TryParse(je.GetString(), out var parsed) => parsed,
                _ => throw new FormatException($"Cannot convert JSON value of kind '{je.ValueKind}' to Int32.")
            };
        }
        return Convert.ToInt32(value);
    }
}
