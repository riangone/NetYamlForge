# 批处理作业实现原理

## 概述

NetYamlForge 的批处理作业功能使用 **纯 C# 实现**的调度系统，不依赖操作系统的 cron 服务。这使得应用可以跨平台（Windows/Linux/macOS）运行，并作为后台服务持续执行定期任务。

---

## 架构设计

```
┌─────────────────────────────────────────────────────────────────┐
│                    ASP.NET Core Web 应用                         │
│                                                                 │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │           BatchJobHostedService (IHostedService)          │ │
│  │                                                           │ │
│  │  ┌─────────────────────────────────────────────────────┐ │ │
│  │  │  ExecuteAsync() - 后台循环                           │ │ │
│  │  │    ├─ LoadJobsAsync()                               │ │ │
│  │  │    │   └─ 读取 projects/*/jobs/*.yml                │ │ │
│  │  │    └─ while (!stoppingToken)                        │ │ │
│  │  │        ├─ CheckAndRunJobsAsync()                    │ │ │
│  │  │        └─ Task.Delay(1 分钟)                         │ │ │
│  │  └─────────────────────────────────────────────────────┘ │ │
│  │                                                           │ │
│  │  ┌─────────────────────────────────────────────────────┐ │ │
│  │  │  CronParser (静态类)                                │ │ │
│  │  │    └─ GetNextOccurrence()                           │ │ │
│  │  │        └─ 计算下次执行时间                           │ │ │
│  │  └─────────────────────────────────────────────────────┘ │ │
│  │                                                           │ │
│  │  ┌─────────────────────────────────────────────────────┐ │ │
│  │  │  ScheduledJob (并发字典)                            │ │ │
│  │  │    └─ 存储已注册的作业和下次执行时间                 │ │ │
│  │  └─────────────────────────────────────────────────────┘ │ │
│  └───────────────────────────────────────────────────────────┘ │
│                                                                 │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │           BatchJobExecutor (作用域服务)                   │ │
│  │    └─ ExecuteAsync() - 执行单个作业                       │ │
│  │        ├─ Before フック                                   │ │
│  │        ├─ SQL 执行/CSV 输出                                │ │
│  │        ├─ After フック                                    │ │
│  │        └─ 结果记录                                        │ │
│  └───────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

---

## 核心组件

### 1. BatchJobHostedService

**文件**: `Services/BatchJob/BatchJobHostedService.cs`

继承自 `BackgroundService`，作为 ASP.NET Core 的后台服务运行。

#### 生命周期

```csharp
public class BatchJobHostedService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1. 启动时加载所有作业定义
        await LoadJobsAsync();

        // 2. 每分钟检查一次
        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckAndRunJobsAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
```

#### 作业加载流程

```csharp
private async Task LoadJobsAsync()
{
    using var scope = _serviceProvider.CreateScope();
    var projectManager = scope.ServiceProvider.GetRequiredService<ProjectManager>();
    var jobLoader = scope.ServiceProvider.GetRequiredService<IBatchJobLoader>();

    // 遍历所有项目
    foreach (var project in projectManager.GetAll())
    {
        // 读取 projects/<project>/jobs/*.yml
        var jobs = await jobLoader.LoadJobsAsync(project.ProjectDir);
        
        foreach (var job in jobs.Values)
        {
            if (job.Enabled && !string.IsNullOrEmpty(job.Schedule.Cron))
            {
                RegisterJob(job, project.Name);
            }
        }
    }
}
```

---

### 2. CronParser

**文件**: `Services/BatchJob/BatchJobHostedService.cs`

纯 C# 实现的 Cron 表达式解析器，计算下次执行时间。

#### Cron 表达式格式

```
分 时 日 月 曜
0  2  *  *  *   → 每天 2:00
```

#### 解析算法

```csharp
public static DateTime? GetNextOccurrence(string cron, DateTime baseTime, string timezone = "UTC")
{
    // 1. 解析 Cron 字段
    var parts = cron.Trim().Split(' ');
    var minute = ParseCronField(parts[0], 0, 59);      // 分
    var hour = ParseCronField(parts[1], 0, 23);        // 时
    var dayOfMonth = ParseCronField(parts[2], 1, 31);  // 日
    var month = ParseCronField(parts[3], 1, 12);       // 月
    var dayOfWeek = ParseCronField(parts[4], 0, 6);    // 曜

    // 2. 时区转换
    var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
    var currentTime = TimeZoneInfo.ConvertTime(baseTime, tz);
    var nextTime = currentTime.AddMinutes(1);

    // 3. 线性搜索（最多 1 年 = 525600 分钟）
    for (int i = 0; i < 525600; i++)
    {
        if (month.Contains(nextTime.Month) &&
            dayOfMonth.Contains(nextTime.Day) &&
            dayOfWeek.Contains((int)nextTime.DayOfWeek) &&
            hour.Contains(nextTime.Hour) &&
            minute.Contains(nextTime.Minute))
        {
            return TimeZoneInfo.ConvertTime(nextTime, tz, TimeZoneInfo.Utc);
        }
        nextTime = nextTime.AddMinutes(1);
    }

    return null;
}
```

#### 字段解析规则

```csharp
private static HashSet<int> ParseCronField(string field, int min, int max)
{
    var result = new HashSet<int>();

    if (field == "*")
    {
        // 所有值
        for (int i = min; i <= max; i++) result.Add(i);
    }
    else if (field.Contains('/'))
    {
        // 步长：*/15 → 0,15,30,45
        var values = field.Split('/');
        var start = values[0] == "*" ? min : int.Parse(values[0]);
        var step = int.Parse(values[1]);
        for (int i = start; i <= max; i += step) result.Add(i);
    }
    else if (field.Contains('-'))
    {
        // 范围：1-5 → 1,2,3,4,5
        var values = field.Split('-');
        for (int i = int.Parse(values[0]); i <= int.Parse(values[1]); i++) result.Add(i);
    }
    else
    {
        // 枚举：1,3,5 → 1,3,5
        foreach (var part in field.Split(',')) result.Add(int.Parse(part));
    }

    return result;
}
```

---

### 3. 作业检查与执行

**文件**: `Services/BatchJob/BatchJobHostedService.cs`

#### 检查循环

```csharp
private async Task CheckAndRunJobsAsync(CancellationToken cancellationToken)
{
    var now = DateTime.UtcNow;

    foreach (var kvp in _scheduledJobs)
    {
        var scheduledJob = kvp.Value;
        var nextRun = scheduledJob.NextRunTime;

        // 到达执行时间
        if (nextRun.HasValue && nextRun.Value <= now)
        {
            // 异步执行（不阻塞检查循环）
            _ = RunJobAsync(scheduledJob, cancellationToken);

            // 计算下次执行时间
            if (!string.IsNullOrEmpty(scheduledJob.Job.Schedule.Cron))
            {
                scheduledJob.NextRunTime = CronParser.GetNextOccurrence(
                    scheduledJob.Job.Schedule.Cron, 
                    now, 
                    scheduledJob.Job.Schedule.Timezone);
            }
        }
    }
}
```

#### 执行流程（带重试）

```csharp
private async Task RunJobAsync(ScheduledJob scheduledJob, CancellationToken cancellationToken)
{
    using var scope = _serviceProvider.CreateScope();
    var executor = scope.ServiceProvider.GetRequiredService<IBatchJobExecutor>();
    var job = scheduledJob.Job;
    
    // 重试逻辑
    var maxAttempts = (job.OnFailure?.RetryCount ?? 0) + 1;
    
    for (int attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            var result = await executor.ExecuteAsync(job, scheduledJob.ProjectName, cancellationToken);
            
            if (result.Success) break;  // 成功则退出
            
            // 失败时等待重试
            if (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(job.OnFailure?.RetryInterval ?? 60));
            }
        }
        catch (Exception ex)
        {
            // 异常时等待重试
            if (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(job.OnFailure?.RetryInterval ?? 60));
            }
        }
    }
}
```

---

### 4. BatchJobExecutor

**文件**: `Services/BatchJob/BatchJobExecutor.cs`

实际执行作业逻辑的服务。

#### 执行流程

```csharp
public async Task<BatchJobResult> ExecuteAsync(
    BatchJobDefinition job, 
    string? projectName, 
    CancellationToken cancellationToken = default)
{
    var result = new BatchJobResult { JobId = job.Id, StartedAt = DateTime.UtcNow };

    try
    {
        using var db = _dbConnectionFactory.CreateConnection(projectName);
        db.Open();
        using var tx = db.BeginTransaction();

        // 1. 执行 Before フック
        if (job.BeforeRun != null && job.BeforeRun.Count > 0)
        {
            var hookResult = await _hookExecutionService.RunBeforeAsync(
                job.BeforeRun, hookContext, projectName, db, tx);
            
            if (hookResult.Cancel)
            {
                result.Success = false;
                result.ErrorMessage = hookResult.CancelMessage;
                return result;
            }
        }

        // 2. 根据作业类型执行
        switch (job.Type.ToLowerInvariant())
        {
            case "sql_to_csv":
                await ExecuteSqlToCsvAsync(job, db, tx, result, cancellationToken);
                break;
            case "sql_command":
                await ExecuteSqlCommandAsync(job, db, tx, result, cancellationToken);
                break;
        }

        // 3. 执行 After フック
        if (job.AfterRun != null && job.AfterRun.Count > 0)
        {
            await _hookExecutionService.RunAfterAsync(
                job.AfterRun, hookContext, projectName, db, tx);
        }

        tx.Commit();
        result.Success = true;
    }
    catch (Exception ex)
    {
        result.Success = false;
        result.ErrorMessage = ex.Message;
    }
    finally
    {
        result.EndedAt = DateTime.UtcNow;
    }

    return result;
}
```

#### SQL 到 CSV 转换

```csharp
private async Task ExecuteSqlToCsvAsync(
    BatchJobDefinition job,
    IDbConnection db,
    IDbTransaction tx,
    BatchJobResult result,
    CancellationToken cancellationToken)
{
    var sql = await GetSqlAsync(job);
    var outputFile = ResolveOutputPath(job.Settings.OutputFile);

    // 确保输出目录存在
    var directory = Path.GetDirectoryName(outputFile);
    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
    {
        Directory.CreateDirectory(directory);
    }

    // 执行查询
    using var reader = db.ExecuteReader(sql, transaction: tx);
    
    // 写入 CSV
    using var writer = new StreamWriter(outputFile, false, Encoding.UTF8);
    
    // 写入表头
    if (job.Settings.IncludeHeader)
    {
        var headers = new List<string>();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            headers.Add(EscapeCsvValue(reader.GetName(i), job.Settings.Delimiter));
        }
        await writer.WriteLineAsync(string.Join(delimiter, headers));
    }

    // 写入数据行
    var rowCount = 0;
    while (reader.Read())
    {
        var values = new List<string>();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            values.Add(EscapeCsvValue(reader.GetValue(i)?.ToString() ?? "", job.Settings.Delimiter));
        }
        await writer.WriteLineAsync(string.Join(delimiter, values));
        rowCount++;
    }

    result.RowsAffected = rowCount;
    result.OutputFile = outputFile;
}
```

---

## 时间线示例

假设配置了以下作业：

```yaml
jobs:
  nightly_stats:
    schedule:
      cron: "0 2 * * *"  # 每天 2:00
      timezone: "Asia/Tokyo"
```

### 执行时间线

```
时间 (JST)          系统行为
────────────────────────────────────────────────────
2025-01-15 10:00   应用启动
                   ├─ LoadJobsAsync() 读取 YAML
                   ├─ CronParser 计算下次执行时间
                   └─ NextRunTime = 2025-01-16 02:00:00 JST

2025-01-15 10:01   CheckAndRunJobsAsync() 检查
                   └─ 10:01 < 02:00 → 不执行

2025-01-15 10:02   CheckAndRunJobsAsync() 检查
                   └─ 10:02 < 02:00 → 不执行

... (每分钟检查)

2025-01-16 01:59   CheckAndRunJobsAsync() 检查
                   └─ 01:59 < 02:00 → 不执行

2025-01-16 02:00   CheckAndRunJobsAsync() 检查
                   ├─ 02:00 >= 02:00 → 执行!
                   ├─ RunJobAsync(nightly_stats)
                   ├─ 计算下次执行时间
                   └─ NextRunTime = 2025-01-17 02:00:00 JST

2025-01-16 02:01   CheckAndRunJobsAsync() 检查
                   └─ 02:01 < 02:00(次日) → 不执行
```

---

## 并发控制

### 防止重复执行

```csharp
// 使用异步执行但不等待完成
_ = RunJobAsync(scheduledJob, cancellationToken);

// 立即更新下次执行时间
scheduledJob.NextRunTime = CronParser.GetNextOccurrence(...);
```

这样即使作业执行时间超过 1 分钟，也不会在下次检查时重复触发。

### 改进建议（未实现）

当前实现没有严格的并发控制。如果需要防止长时间作业的重叠执行，可以添加：

```csharp
private readonly ConcurrentDictionary<string, bool> _runningJobs = new();

private async Task RunJobAsync(...)
{
    // 如果正在运行则跳过
    if (!_runningJobs.TryAdd(job.Id, true))
    {
        _logger.LogWarning("作业正在运行，跳过本次执行：{JobId}", job.Id);
        return;
    }

    try
    {
        // 执行作业...
    }
    finally
    {
        _runningJobs.TryRemove(job.Id, out _);
    }
}
```

---

## 服务注册

**文件**: `Extensions/ServiceCollectionExtensions.cs`

```csharp
public static IServiceCollection AddDynamicCrudCore(this IServiceCollection services)
{
    // ... 其他服务

    // 批处理作业服务
    services.AddSingleton<IBatchJobLoader, BatchJobLoader>();
    services.AddScoped<IDbConnectionFactory, DbConnectionFactory>();
    services.AddScoped<IBatchJobExecutor, BatchJobExecutor>();
    services.AddSingleton<IBatchJobHistoryStore, InMemoryBatchJobHistoryStore>();
    services.AddHostedService<BatchJobHostedService>();

    return services;
}
```

### 生命周期说明

| 服务 | 生命周期 | 理由 |
|------|----------|------|
| `IBatchJobLoader` | Singleton | 无状态，启动时加载一次 |
| `IBatchJobExecutor` | Scoped | 每次执行创建新作用域 |
| `IBatchJobHistoryStore` | Singleton | 内存存储，全局共享 |
| `BatchJobHostedService` | Singleton | IHostedService 必须是 Singleton |

---

## Windows 服务集成

**文件**: `Program.cs`

```csharp
// 检测 --run-as-service 参数或环境变量
var useWindowsService = args.Any(a => a.Equals("--run-as-service", StringComparison.OrdinalIgnoreCase))
    || Environment.GetEnvironmentVariable("DOTNET_RUNNING_AS_WINDOWS_SERVICE") == "true";

var builder = WebApplication.CreateBuilder(args);

// 配置 Windows 服务
if (useWindowsService)
{
    builder.Host.UseWindowsService();
}
```

### 服务启动流程

```
Windows 服务管理器
    ↓ StartService()
NetYamlForge.exe --run-as-service
    ↓
Program.cs 检测 --run-as-service
    ↓
builder.Host.UseWindowsService()
    ↓
WindowsServiceLifetime 接管生命周期
    ↓
应用作为后台服务运行
    ↓
BatchJobHostedService 开始后台循环
```

---

## 日志记录

使用 Serilog 记录所有关键操作：

```csharp
// 作业加载
_logger.LogInformation("ジョブをスケジュールしました：{Project}/{JobId}", 
    project.Name, job.Id);

// 作业执行开始
_logger.LogInformation("ジョブ実行開始：{JobId} (試行 {Attempt}/{Max})", 
    job.Id, attempt, maxAttempts);

// 作业执行成功
_logger.LogInformation("ジョブ成功：{JobId}, Duration: {Duration}ms, Rows: {Rows}", 
    job.Id, lastResult.DurationMs, lastResult.RowsAffected);

// 作业执行失败
_logger.LogWarning("ジョブ失敗、リトライ待機：{JobId}, Error: {Error}, Retry in {Seconds}s", 
    job.Id, lastResult.ErrorMessage, retryInterval.TotalSeconds);

// 调度错误
_logger.LogError(ex, "ジョブスケジューリング中にエラーが発生しました");
```

---

## 性能考虑

### 1. 检查间隔

当前设置为 **1 分钟**：

```csharp
await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
```

**优点**:
- 简单可靠
- 对 Cron 精度足够（Cron 最小单位是分钟）

**缺点**:
- 最多 1 分钟的延迟
- 不适合秒级精度的任务

### 2. 内存使用

- `_scheduledJobs`: `ConcurrentDictionary<string, ScheduledJob>`
- 每个作业约 1KB 内存
- 100 个作业 ≈ 100KB

### 3. 数据库连接

每次执行作业时创建新连接：

```csharp
using var db = _dbConnectionFactory.CreateConnection(projectName);
```

**改进建议**:
- 对于高频作业，可以使用连接池
- Dapper 默认支持连接池

---

## 扩展点

### 1. 添加新的作业类型

```csharp
// BatchJobExecutor.cs
switch (job.Type.ToLowerInvariant())
{
    case "sql_to_csv":
        await ExecuteSqlToCsvAsync(...);
        break;
    case "http_request":  // 新增
        await ExecuteHttpRequestAsync(...);
        break;
}
```

### 2. 添加通知功能

```csharp
// BatchJobHostedService.RunJobAsync
if (!lastResult.Success && job.OnFailure?.Notify != null)
{
    // TODO: 发送邮件通知
    await _emailService.SendAsync(
        job.OnFailure.Notify,
        $"Job Failed: {job.Id}",
        lastResult.ErrorMessage);
}
```

### 3. 持久化执行历史

```csharp
public class DatabaseBatchJobHistoryStore : IBatchJobHistoryStore
{
    private readonly IDbConnection _db;

    public async Task SaveHistoryAsync(BatchJobHistory history)
    {
        await _db.ExecuteAsync(
            "INSERT INTO BatchJobHistory (...) VALUES (...)",
            history);
    }
}
```

---

## 故障排除

### 问题 1: 作业不执行

**检查点**:
1. YAML 中 `enabled: true`
2. Cron 表达式有效
3. 应用正在运行（后台服务状态）
4. 日志中是否有 "ジョブをスケジュールしました"

### 问题 2: 执行时间不准确

**原因**: 检查间隔为 1 分钟，最多有 1 分钟的延迟

**解决**: 这是设计行为。如需更高精度，减少 `Task.Delay` 间隔。

### 问题 3: 时区问题

**检查点**:
1. 服务器时区设置
2. YAML 中 `timezone` 配置
3. `TimeZoneInfo.FindSystemTimeZoneById` 是否能找到指定时区

---

## 相关文档

- [批处理作业使用指南](batch-jobs.md)
- [Windows 服务部署指南](batch-jobs.md#windows-サービスとしての実行)

---

## 实现文件一览

| 文件 | 说明 | 行数 |
|------|------|------|
| `Services/BatchJob/BatchJobDefinition.cs` | 数据模型 | 227 |
| `Services/BatchJob/BatchJobLoader.cs` | YAML 加载器 | 103 |
| `Services/BatchJob/BatchJobExecutor.cs` | 执行引擎 | 298 |
| `Services/BatchJob/BatchJobHostedService.cs` | 调度服务 | 389 |
| `Services/Cli/BatchJobScaffolder.cs` | CLI 脚手架 | 246 |
| **总计** | | **1,263** |

---

*最后更新：2025-03-19*
