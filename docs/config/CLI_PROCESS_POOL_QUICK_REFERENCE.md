# CLI 进程池快速参考

## 🚀 快速开始

### 1. 确认配置

在 `appsettings.json` 中:

```json
{
  "AICli": {
    "ProcessPool": {
      "EnableDaemonMode": true,
      "MaxPoolSize": 3,
      "IdleTimeoutMinutes": 10
    }
  }
}
```

### 2. 启动应用

```bash
dotnet run --project NetYamlForge
```

### 3. 查看日志

```bash
# 查看进程池初始化日志
grep "\[进程池\]" logs/*.log

# 应该看到类似输出:
# [进程池] 进程池管理器初始化: MaxPoolSize=3, IdleTimeout=10分钟, HealthCheck=30秒
```

## 📊 监控进程池

### API 端点

```bash
# 获取进程池统计信息
curl http://localhost:5000/api/ai/pool/stats
```

### 响应示例

```json
{
  "claude": {
    "poolSize": 2,
    "healthyCount": 2,
    "busyCount": 0,
    "totalRequests": 15,
    "maxPoolSize": 3,
    "processes": [
      {
        "provider": "claude",
        "pid": 12345,
        "healthy": true,
        "busy": false,
        "requestCount": 8,
        "lifetimeMinutes": 15.5,
        "idleMinutes": 2.3
      }
    ]
  }
}
```

## 🔧 配置调优

### 开发环境 (低资源)

```json
{
  "ProcessPool": {
    "EnableDaemonMode": false,
    "MaxPoolSize": 1,
    "IdleTimeoutMinutes": 5,
    "HealthCheckIntervalSeconds": 60
  }
}
```

### 生产环境 (高吞吐)

```json
{
  "ProcessPool": {
    "EnableDaemonMode": true,
    "MaxPoolSize": 5,
    "IdleTimeoutMinutes": 15,
    "HealthCheckIntervalSeconds": 30,
    "MaxLifetimeMinutes": 120,
    "MaxRequestsPerProcess": 200
  }
}
```

### 测试环境

```json
{
  "ProcessPool": {
    "EnableDaemonMode": false,
    "MaxPoolSize": 2,
    "IdleTimeoutMinutes": 1,
    "HealthCheckIntervalSeconds": 0
  }
}
```

## 🐛 故障排查

### 问题 1: 进程池未生效

**症状**: 每次执行任务都启动新进程

**检查清单**:
```bash
# 1. 确认配置
cat appsettings.json | jq .AICli.ProcessPool

# 2. 查看日志
grep "\[进程池\]" logs/*.log | tail -20

# 3. 检查服务注册
grep -A 10 "PooledCLIService" NetYamlForge/Program.cs
```

**解决方案**:
- 确保 `EnableDaemonMode = true`
- 检查 `PooledCLIService` 是否正确注册

### 问题 2: 进程启动失败

**症状**: 日志中出现 "进程启动失败"

**检查**:
```bash
# 查看错误日志
grep "进程启动失败" logs/*.log

# 检查 CLI 工具是否安装
which claude
which qwen
```

**解决方案**:
- 安装对应的 CLI 工具
- 检查 API Key 配置
- 增加 `MaxStartRetries`

### 问题 3: 内存占用过高

**症状**: 系统内存使用持续增长

**检查**:
```bash
# 查看活跃进程
ps aux | grep -E "claude|qwen" | grep -v grep

# 查看进程池统计
curl http://localhost:5000/api/ai/pool/stats | jq
```

**解决方案**:
- 减少 `MaxPoolSize`
- 减少 `IdleTimeoutMinutes`
- 设置 `MaxLifetimeMinutes` 限制进程存活时间

### 问题 4: 进程池统计为空

**症状**: `/api/ai/pool/stats` 返回空对象 `{}`

**原因**: 还没有执行过 AI 任务

**解决方案**:
```bash
# 执行一个 AI 任务
curl -X POST http://localhost:5000/api/ai/task \
  -H "Content-Type: application/json" \
  -d '{"message": "Hello", "cliTool": "claude"}'

# 再次查看统计
curl http://localhost:5000/api/ai/pool/stats
```

## 📈 性能基准

### 测试脚本

```bash
# 测试无进程池
# 1. 禁用进程池
# 2. 执行 10 个任务
# 3. 记录总时间

# 测试有进程池
# 1. 启用进程池
# 2. 执行 10 个任务
# 3. 记录总时间
```

### 预期结果

| 指标 | 无进程池 | 有进程池 | 提升 |
|------|---------|---------|------|
| 总耗时 | 45 秒 | 28 秒 | **38%** |
| 平均响应 | 4.5 秒 | 2.8 秒 | **38%** |
| 启动次数 | 10 | 3 | **70%** |

## 📝 最佳实践

### 1. 监控告警

```bash
# 设置告警: 进程池复用率 < 50%
curl -s http://localhost:5000/api/ai/pool/stats | \
  jq '.[].totalRequests / (.[] | .poolSize) | select(. < 0.5)' && \
  echo "ALERT: Low pool reuse rate!"
```

### 2. 定期清理

```bash
# 重启应用释放所有进程
systemctl restart netyamlforge

# 或调用清理 API (如果有)
curl -X POST http://localhost:5000/api/ai/pool/clear
```

### 3. 容量规划

根据并发任务数调整 `MaxPoolSize`:

| 并发任务数 | 推荐 MaxPoolSize |
|----------|-----------------|
| 1-2 | 2 |
| 3-5 | 3-5 |
| 5-10 | 5-8 |
| 10+ | 10+ |

## 🔗 相关链接

- [详细文档](docs/CLI_PROCESS_POOL.md)
- [实施总结](CLI_PROCESS_POOL_IMPLEMENTATION_SUMMARY.md)
- [配置示例](ai-process-pool-config.example.json)

---

*快速参考 | 版本 1.0 | 2026-04-10*
