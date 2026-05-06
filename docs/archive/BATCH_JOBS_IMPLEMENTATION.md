# 批处理作业功能实现总结

## 概述

已成功为 NetYamlForge 框架添加了批处理作业功能，支持通过 YAML 配置文件定义定时任务，如夜间数据统计、CSV 文件生成等。

## 实现的功能

### 1. 核心组件

| 文件 | 说明 |
|------|------|
| `Services/BatchJob/BatchJobDefinition.cs` | 批处理作业定义模型（JobDefinition, JobSchedule, JobSettings 等） |
| `Services/BatchJob/BatchJobLoader.cs` | YAML 作业定义加载器 |
| `Services/BatchJob/BatchJobExecutor.cs` | 作业执行引擎（SQL 执行、CSV 输出） |
| `Services/BatchJob/BatchJobHostedService.cs` | 后台调度服务（Cron 解析、定时执行） |
| `Services/Cli/BatchJobScaffolder.cs` | CLI 脚手架生成器 |

### 2. YAML Schema

```yaml
jobs:
  nightly_stats:
    displayName: 夜间统计作业
    description: "每日 2 点に前日の商品販売統計を CSV 出力します"
    enabled: true
    
    schedule:
      cron: "0 2 * * *"  # 毎日 2:00
      timezone: "Asia/Tokyo"
    
    type: sql_to_csv
    
    settings:
      sqlFile: jobs/sql/nightly_stats.sql
      outputFile: jobs/output/stats_{date:yyyyMMdd}.csv
      includeHeader: true
      delimiter: ","
    
    onFailure:
      retryCount: 3
      retryInterval: 300
```

### 3. 支持的作业类型

- **sql_to_csv**: SQL 查询结果输出为 CSV
- **sql_command**: 执行 SQL 命令（INSERT/UPDATE/DELETE）
- **stored_procedure**: 执行存储过程（待实现）

### 4. Cron 表达式支持

支持标准 5 字段 Cron 表达式：
- `0 2 * * *` - 每天 2:00
- `0 */6 * * *` - 每 6 小时
- `30 9 * * 1-5` - 工作日 9:30
- `0 0 1 * *` - 每月 1 日

### 5. 功能特点

- ✅ YAML 配置驱动
- ✅ Cron 调度
- ✅ 多项目支持
- ✅ 时区支持
- ✅ 失败重试
- ✅ 钩子集成（beforeRun/afterRun）
- ✅ 日期占位符（`{date:yyyyMMdd}`）
- ✅ CSV/JSON/XML 输出（CSV 已实现）
- ✅ 执行历史记录（内存存储）
- ✅ Serilog 日志记录

## 使用方法

### 创建批处理作业

```bash
# 生成作业模板
dotnet run --project NetYamlForge -- --scaffold-batch-job \
  --project=shop \
  --name=nightly_stats
```

### 编辑生成的文件

1. `projects/shop/jobs/nightly_stats.yml` - 作业定义
2. `projects/shop/jobs/sql/nightly_stats.sql` - SQL 查询

### 运行应用程序

```bash
dotnet run --project NetYamlForge
```

作业将根据 Cron 表达式自动调度执行。

## 示例作业

已创建示例作业 `projects/shop/jobs/nightly_stats.yml`：

- **功能**: 每日商品销售统计
- **执行时间**: 每天 2:00（日本时间）
- **输出**: CSV 文件（包含商品 ID、名称、订单数、销售数量、金额等）

## 文件输出位置

- 作业定义：`projects/<project>/jobs/*.yml`
- SQL 文件：`projects/<project>/jobs/sql/*.sql`
- 输出文件：`projects/<project>/jobs/output/*.csv`

## 日志示例

```
[INF] ジョブ実行開始：nightly_stats (試行 1/3)
[INF] SQL->CSV ジョブ完了：nightly_stats, Rows: 150, File: .../stats_20250115.csv
[INF] ジョブ成功：nightly_stats, Duration: 234ms, Rows: 150
```

## 扩展点

### 添加新的作业类型

实现 `BatchJobExecutor.ExecuteAsync` 中的新 case：

```csharp
case "custom_type":
    await ExecuteCustomTypeAsync(job, db, tx, result, cancellationToken);
    break;
```

### 添加通知功能

在 `BatchJobHostedService.RunJobAsync` 中实现邮件通知：

```csharp
if (!lastResult.Success && job.OnFailure?.Notify != null)
{
    // TODO: 发送邮件通知
    await _emailService.SendAsync(...);
}
```

### 持久化执行历史

实现 `IBatchJobHistoryStore` 接口：

```csharp
public class DatabaseBatchJobHistoryStore : IBatchJobHistoryStore
{
    // 将历史记录保存到数据库
}
```

## 技术细节

### Cron 解析器

`CronParser.GetNextOccurrence` 方法支持：
- `*` - 所有值
- `,` - 枚举（如 `1,3,5`）
- `-` - 范围（如 `1-5`）
- `/` - 步长（如 `*/15`）

### 数据库连接工厂

`DbConnectionFactory` 根据项目配置自动选择数据库类型：
- SQLite
- SQL Server
- MySQL/MariaDB
- PostgreSQL

### CSV 转义

自动处理特殊字符：
- 分隔符
- 换行符
- 双引号

## 注意事项

1. **时区**: 确保服务器时区或配置中的时区正确
2. **文件权限**: 输出目录需要写权限
3. **内存历史**: 当前执行历史存储在内存中，重启后清除
4. **并发执行**: 同一作业不会并发执行

## 待实现功能

- [ ] 邮件通知
- [ ] 数据库持久化执行历史
- [ ] Web 管理界面（执行状态查看、手动触发）
- [ ] 存储过程类型支持
- [ ] JSON/XML 输出格式支持
- [ ] 作业依赖关系
- [ ] 分布式锁（多实例部署时）

## 相关文档

- [批处理作业指南](docs/guides/batch-jobs.md)

## 测试

主程序构建验证通过：
```bash
dotnet build -c Release NetYamlForge/NetYamlForge.csproj
# Build succeeded
```
