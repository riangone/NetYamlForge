using System.Collections.Concurrent;
using TaskStatus = NetYamlForge.AI.Models.TaskStatus;
using Microsoft.AspNetCore.SignalR;
using NetYamlForge.AI.Hubs;
using NetYamlForge.AI.Models;

namespace NetYamlForge.AI.Services;

/// <summary>
/// 进度追踪器（配合 SignalR 推送）
/// </summary>
public class ProgressTracker
{
    private readonly ConcurrentDictionary<string, AITask> _tasks = new();
    private readonly IHubContext<AIProgressHub> _hub;

    public ProgressTracker(IHubContext<AIProgressHub> hub)
    {
        _hub = hub;
    }
    
    public void Add(AITask task) => _tasks[task.Id] = task;

    public void UpdateStatus(string taskId, TaskStatus status, int progress)
    {
        if (_tasks.TryGetValue(taskId, out var task))
        {
            task.Status = status;
            task.Progress = progress;
            task.UpdatedAt = DateTime.UtcNow;

            _ = SendProgressAsync(task);
        }
    }

    public void UpdateProgress(string taskId, ProgressUpdate update)
    {
        if (_tasks.TryGetValue(taskId, out var task))
        {
            task.Progress = update.Progress;
            if (update.Logs.Count > 0)
            {
                task.Logs.AddRange(update.Logs);
            }
            task.UpdatedAt = DateTime.UtcNow;

            _ = SendProgressAsync(task, update);
        }
    }

    public void Complete(string taskId, string? result = null)
    {
        if (_tasks.TryGetValue(taskId, out var task))
        {
            task.Status = TaskStatus.Completed;
            task.Progress = 100;
            task.Result = result;
            task.CompletedAt = DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;

            _ = SendProgressAsync(task);
        }
    }

    public void Fail(string taskId, string error)
    {
        if (_tasks.TryGetValue(taskId, out var task))
        {
            task.Status = TaskStatus.Failed;
            task.Error = error;
            task.CompletedAt = DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;

            _ = SendProgressAsync(task);
        }
    }

    public void Cancel(string taskId)
    {
        if (_tasks.TryGetValue(taskId, out var task))
        {
            task.Status = TaskStatus.Cancelled;
            task.CompletedAt = DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;

            _ = SendProgressAsync(task);
        }
    }
    
    public AITask? GetTask(string taskId) => _tasks.TryGetValue(taskId, out var task) ? task : null;
    
    public IEnumerable<AITask> GetAllTasks() => _tasks.Values;
    
    public IEnumerable<AITask> GetUserTasks(string userId) => 
        _tasks.Values.Where(t => t.UserId == userId);
    
    public bool TryGetTask(string taskId, out AITask? task) => _tasks.TryGetValue(taskId, out task);
    
    private async Task SendProgressAsync(AITask task, ProgressUpdate? update = null)
    {
        try
        {
            await _hub.Clients.All.SendAsync("ReceiveProgress", new
            {
                task.Id,
                task.Status,
                task.Progress,
                task.Logs,
                task.Result,
                task.Error,
                task.UpdatedAt,
                StreamData = update?.StreamData
            });
        }
        catch (Exception)
        {
            // SignalR 送信失敗はメインフローに影響させない
        }
    }
}
