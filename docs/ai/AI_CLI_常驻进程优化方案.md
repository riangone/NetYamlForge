# AI CLI 常驻进程优化方案

## 问题分析

### 当前架构问题

**每次调用 AI 时的流程：**
```
用户请求 → BaseChatService → CliFirstLlmProvider → QwenCodeCLIService 
         → ProcessExecutor → 启动新 qwen 进程 → 执行 prompt → 进程退出
```

**性能瓶颈：**
1. **进程启动开销**：每次调用都要启动新的 Node.js 进程（Qwen Code 基于 Node.js）
2. **初始化开销**：Qwen CLI 需要加载模型、初始化上下文
3. **冷启动延迟**：首次调用尤其慢（约 2-5 秒）
4. **资源浪费**：频繁创建/销毁进程

### 实测数据（预估）

| 阶段 | 耗时 |
|------|------|
| 进程启动 | 500-1500ms |
| CLI 初始化 | 1000-3000ms |
| 实际 AI 推理 | 取决于响应长度 |
| **总延迟** | **2-5 秒（不含推理）** |

---

## 解决方案

### 方案 1：常驻进程池（推荐）⭐

**核心思路**：维护一个 CLI 进程池，复用已启动的进程。

#### 架构设计

```
┌─────────────────────────────────────────────┐
│           CliProcessPool                    │
│  ┌───────────┐  ┌───────────┐  ┌─────────┐ │
│  │ Process 1 │  │ Process 2 │  │Process N│ │
│  │ (idle)    │  │ (busy)    │  │ (idle)  │ │
│  └───────────┘  └───────────┘  └─────────┘ │
│       ↑              ↑              ↑       │
│       └──────────────┴──────────────┘       │
│           异步队列调度                       │
└─────────────────────────────────────────────┘
```

#### 实现要点

**1. 进程池管理器**

```csharp
public class CliProcessPool : IDisposable
{
    private readonly List<CliWorker> _workers = new();
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger _logger;
    private readonly int _poolSize;
    
    public CliProcessPool(int poolSize = 3, ILogger logger)
    {
        _poolSize = poolSize;
        _semaphore = new SemaphoreSlim(poolSize, poolSize);
        _logger = logger;
        
        // 预启动工作进程
        for (int i = 0; i < poolSize; i++)
        {
            _workers.Add(new CliWorker(i, logger));
        }
    }
    
    public async Task<string> ExecuteAsync(string prompt, CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct);
        var worker = _workers.First(w => !w.IsBusy);
        
        try
        {
            worker.IsBusy = true;
            return await worker.ExecuteAsync(prompt, ct);
        }
        finally
        {
            worker.IsBusy = false;
            _semaphore.Release();
        }
    }
}
```

**2. 常驻工作进程**

```csharp
public class CliWorker : IDisposable
{
    private Process? _process;
    private readonly int _id;
    private readonly ILogger _logger;
    private bool _isHealthy;
    
    public bool IsBusy { get; set; }
    
    public async Task InitializeAsync(string command, CancellationToken ct)
    {
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        
        _process.Start();
        _isHealthy = true;
        
        // 启动后台读取任务
        _ = Task.Run(() => ReadOutputLoop(ct), ct);
    }
    
    public async Task<string> ExecuteAsync(string prompt, CancellationToken ct)
    {
        if (!_isHealthy || _process?.HasExited == true)
        {
            await RestartAsync(ct);
        }
        
        // 发送 prompt
        await _process!.StandardInput.WriteLineAsync(prompt);
        await _process.StandardInput.FlushAsync(ct);
        
        // 等待响应（通过事件或 TaskCompletionSource）
        return await WaitForResponse(ct);
    }
}
```

#### 优势
- ✅ **性能提升 70-90%**：避免重复启动进程
- ✅ **资源复用**：保持模型加载状态
- ✅ **并发支持**：多进程池支持并行请求
- ✅ **向后兼容**：不影响现有代码结构

#### 劣势
- ⚠️ 需要管理进程生命周期
- ⚠️ 内存占用略高（常驻进程）

---

### 方案 2：直接 API 调用（替代方案）

**核心思路**：跳过 CLI，直接调用 DashScope API。

#### 架构对比

```
当前：Web → CLI 进程 → API → 响应
优化：Web → HTTP Client → API → 响应
```

#### 实现示例

```csharp
public class DashScopeDirectProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    
    public async Task<string> CompleteAsync(string prompt, CancellationToken ct)
    {
        var request = new
        {
            model = _model,
            input = new { messages = new[] {
                new { role = "user", content = prompt }
            }},
            parameters = new { result_format = "text" }
        };
        
        var response = await _http.PostAsJsonAsync(
            "https://dashscope.aliyuncs.com/api/v1/services/aigc/text-generation/generation",
            request, ct);
        
        var result = await response.Content.ReadFromJsonAsync<ApiResponse>(ct);
        return result.Output.Text;
    }
}
```

#### 优势
- ✅ **最快响应**：无进程开销
- ✅ **资源最少**：无需维护进程
- ✅ **更可靠**：减少故障点

#### 劣势
- ⚠️ 需要 API Key（可能有成本）
- ⚠️ 无法使用 CLI 工具（Read/Write/Bash）

---

### 方案 3：混合模式（最佳实践）🏆

**核心思路**：根据场景智能选择执行方式。

```csharp
public class HybridLlmProvider : ILlmProvider
{
    private readonly CliProcessPool _cliPool;
    private readonly DashScopeDirectProvider _directApi;
    private readonly IConfiguration _config;
    
    public async Task<string> CompleteAsync(string prompt, CancellationToken ct)
    {
        // 简单对话：直接用 API
        if (IsSimpleChat(prompt))
        {
            return await _directApi.CompleteAsync(prompt, ct);
        }
        
        // 需要工具调用：使用 CLI
        return await _cliPool.ExecuteAsync(prompt, ct);
    }
    
    private bool IsSimpleChat(string prompt)
    {
        // 不包含文件操作、命令执行等复杂需求
        return !prompt.Contains("读取文件") 
            && !prompt.Contains("执行命令")
            && !prompt.Contains("搜索代码");
    }
}
```

---

## 推荐实施方案

### 第一阶段：直接 API 支持（1-2 天）

**目标**：为简单对话场景提供快速响应

**步骤：**
1. 实现 `DashScopeDirectProvider`
2. 在 `CliFirstLlmProvider` 中添加 API 回退逻辑
3. 配置项：`AICli:UseDirectApi: true/false`

**预期效果：**
- 简单对话响应时间 **< 1 秒**
- 复杂任务仍使用 CLI

### 第二阶段：进程池优化（3-5 天）

**目标**：优化 CLI 工具调用性能

**步骤：**
1. 实现 `CliProcessPool` 和 `CliWorker`
2. 修改 `QwenCodeCLIService` 支持进程复用
3. 添加健康检查和自动重启

**预期效果：**
- CLI 调用响应时间 **减少 70-90%**
- 支持 3-5 个并发请求

### 第三阶段：智能路由（可选）

**目标**：根据任务类型自动选择最优执行方式

**步骤：**
1. 实现意图识别逻辑
2. 配置路由规则
3. 添加性能监控

---

## 配置示例

```json
{
  "AICli": {
    "DefaultTool": "qwen",
    "UseDirectApi": true,
    "DirectApi": {
      "BaseUrl": "https://dashscope.aliyuncs.com",
      "Model": "qwen-plus",
      "TimeoutSeconds": 30
    },
    "ProcessPool": {
      "Enabled": true,
      "PoolSize": 3,
      "IdleTimeoutMinutes": 10,
      "MaxLifetimeMinutes": 60
    }
  }
}
```

---

## 性能对比（预估）

| 方案 | 首次响应 | 后续响应 | 内存占用 | 并发支持 |
|------|---------|---------|---------|---------|
| **当前（每次启动）** | 3-5s | 3-5s | 低 | 有限 |
| **直接 API** | 0.5-1s | 0.5-1s | 极低 | 高 |
| **进程池（3个）** | 1-2s | 0.2-0.5s | 中 | 中 |
| **混合模式** | 0.5-2s | 0.2-1s | 中 | 高 |

---

## 实施建议

### 立即可做
1. ✅ 启用 Direct API 模式（简单配置变更）
2. ✅ 调整超时设置（避免长时间等待）

### 短期优化（本周）
1. 实现进程池原型
2. 性能测试对比
3. 灰度发布验证

### 长期规划
1. 智能路由策略
2. 自适应进程池大小
3. 性能监控仪表盘

---

## 风险评估

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| 进程泄漏 | 高 | 定期重启 + 健康检查 |
| 内存溢出 | 中 | 限制池大小 + 监控 |
| 进程僵死 | 中 | 超时检测 + 自动重启 |
| API 成本 | 低 | 用量监控 + 配额限制 |

---

*文档创建时间：2026-04-10*
*建议实施周期：1-2 周*
