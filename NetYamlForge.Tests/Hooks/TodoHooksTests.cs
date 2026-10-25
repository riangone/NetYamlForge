using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using NetYamlForge.Projects.TodoApp.Hooks;
using NetYamlForge.Services.Hooks;
using Xunit;

namespace NetYamlForge.Tests.Hooks;

public class TodoHooksTests
{
    [Fact]
    public async Task TodoValidationHook_EmptyTitle_ReturnsAbort()
    {
        var hook = new TodoValidationHook();
        var ctx = new EntityHookContext
        {
            Entity = "todo",
            Operation = CrudOperation.Create,
            Values = new Dictionary<string, object?>
            {
                ["title"] = "   ",
                ["priority"] = "medium"
            }
        };

        using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var result = await hook.BeforeAsync(ctx, conn, null);

        Assert.True(result.Cancel);
        Assert.Contains("待办标题不能为空", result.CancelMessage);
    }

    [Fact]
    public async Task TodoValidationHook_InvalidPriority_ReturnsAbort()
    {
        var hook = new TodoValidationHook();
        var ctx = new EntityHookContext
        {
            Entity = "todo",
            Operation = CrudOperation.Create,
            Values = new Dictionary<string, object?>
            {
                ["title"] = "Buy milk",
                ["priority"] = "urgent"
            }
        };

        using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var result = await hook.BeforeAsync(ctx, conn, null);

        Assert.True(result.Cancel);
        Assert.Contains("优先级必须为 low, medium 或 high", result.CancelMessage);
    }

    [Fact]
    public async Task TodoValidationHook_ValidInput_PopulatesDefaultsAndContinues()
    {
        var hook = new TodoValidationHook();
        var ctx = new EntityHookContext
        {
            Entity = "todo",
            Operation = CrudOperation.Create,
            Values = new Dictionary<string, object?>
            {
                ["title"] = "Buy groceries"
            }
        };

        using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var result = await hook.BeforeAsync(ctx, conn, null);

        Assert.False(result.Cancel);
        Assert.Equal("medium", ctx.Values["priority"]);
        Assert.Equal(0, ctx.Values["is_completed"]);
        Assert.True(ctx.Values.ContainsKey("created_at"));
        Assert.True(ctx.Values.ContainsKey("updated_at"));
    }

    [Fact]
    public async Task TodoToggleCompleteHandler_ValidRecord_TogglesState()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        await conn.ExecuteAsync(@"
            CREATE TABLE todo (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                description TEXT,
                is_completed INTEGER NOT NULL DEFAULT 0,
                due_date TEXT,
                priority TEXT NOT NULL DEFAULT 'medium',
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            INSERT INTO todo (title, is_completed) VALUES ('Task 1', 0);
        ");

        var handler = new TodoToggleCompleteHandler();
        var ctx = new CustomActionContext
        {
            Project = "todo-app",
            Entity = "todo",
            Action = "toggle_complete",
            RecordId = "1"
        };

        // 1st toggle: 0 -> 1
        var result1 = await handler.ExecuteAsync(ctx, conn, null);
        Assert.True(result1.Ok);
        Assert.Equal(1, ctx.Data["newState"]);

        var status1 = await conn.ExecuteScalarAsync<int>("SELECT is_completed FROM todo WHERE id = 1");
        Assert.Equal(1, status1);

        // 2nd toggle: 1 -> 0
        var result2 = await handler.ExecuteAsync(ctx, conn, null);
        Assert.True(result2.Ok);
        Assert.Equal(0, ctx.Data["newState"]);

        var status2 = await conn.ExecuteScalarAsync<int>("SELECT is_completed FROM todo WHERE id = 1");
        Assert.Equal(0, status2);
    }

    [Fact]
    public async Task TodoToggleCompleteHandler_NonExistentRecord_ReturnsFailure()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        await conn.ExecuteAsync(@"
            CREATE TABLE todo (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                is_completed INTEGER NOT NULL DEFAULT 0
            );
        ");

        var handler = new TodoToggleCompleteHandler();
        var ctx = new CustomActionContext
        {
            Project = "todo-app",
            Entity = "todo",
            Action = "toggle_complete",
            RecordId = "999"
        };

        var result = await handler.ExecuteAsync(ctx, conn, null);
        Assert.False(result.Ok);
        Assert.Contains("未找到该待办事项", result.ErrorMessage);
    }
}
