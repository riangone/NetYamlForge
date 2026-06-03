# NetYamlForge 框架深层设计隐患与安全漏洞重构方案

> 作成日: 2026-06-04  
> 角色: 系统架构师 (Architect) - Hyperion  
> 目标: 针对多租户路径穿越、SQL 注入及 AI Tool 越权等深层安全与设计隐患提供完整的重构方案。

---

## 核心安全漏洞与设计隐患分析

### 漏洞 1: 批处理文件操作路径穿越 (Path Traversal) 漏洞
* **涉及组件**：[SqlToCsvHandler.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/BatchJob/SqlBatchStepHandlers.cs#L10)、[SqlCommandHandler.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/BatchJob/SqlBatchStepHandlers.cs#L85) 以及相关文件操作。
* **现状与隐患**：
  在多租户批处理任务中，YAML 配置文件可以由租户定制。如果作业定义的 `Settings.OutputFile` 或 `Settings.SqlFile` 包含相对路径（如 `../../`）或绝对路径，批处理执行引擎在读取或写入文件时将允许跨越项目根目录限制，从而篡改或泄露其他租户的私密数据库（如 `.db` 文件）、甚至覆盖系统的二进制文件和源码。这在多租户云托管环境中是极为严重的 **任意文件读取/写入 (Arbitrary File Read/Write)** 漏洞。
* **重构方案**：
  引入 `PathSafetyGuard` 安全辅助类，强制对所有批处理任务中涉及的文件路径进行租户根目录绑定校验。非特权租户的任何读写行为仅能局限在其 `ProjectDir` 或系统的临时安全目录下。

### 漏洞 2: AI Tool 数据查询 SQL 注入防范强化
* **涉及组件**：[SqlSafetyGuard.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/SqlSafetyGuard.cs)、[AiToolRegistryInitializer.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/AI/AiToolRegistryInitializer.cs#L46) 的 `query_data`
* **现状与隐患**：
  虽然对 `query_data` 工具进行了第一阶段的 `EnsureIdentifier` 检查，但在面对更复杂的输入时（例如携带 SQLite 的括号转义如 `[field name]` 或通过空格绕过过滤的复杂语句），若没有对字段、实体和表达式进行强类型闭环限制，依然容易在高危注入场景下被绕过。
* **重构方案**：
  在 `SqlSafetyGuard` 中提供更加严密的 `EnsureIdentifier` 与 `EnsureExpression` 校验；在 `query_data` 工具中更精细地规范过滤算子及字段校验逻辑，并为多租户实体访问增设白名单审查，彻底截断 AI 生成 SQL 的漏洞链。

### 隐患 3: 多租户 AI 会话与 Tool 执行的越权防护
* **涉及组件**：[AiToolOrchestrator.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/AI/AiToolOrchestrator.cs)、[SlotFillingManager.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/AI/SlotFillingManager.cs)
* **现状与隐患**：
  虽然对会话的槽位状态存储加上了 `ProjectId` 前缀，但仍然存在以下隐患：
  1. 会话状态在更新和流转时，缺少对当前租户上下文与请求参数的双向强一致性校验；
  2. 若 `ProjectScope` 为空（例如在 CLI 离线模式或异步后台工作流中），可能因为降级退回 `default` 导致租户间上下文数据发生串话。
* **重构方案**：
  重构 `AiToolOrchestrator` 与 `SlotFillingManager`，强制在接口调用中增加 `projectId` 参数显式传递；若上下文不匹配则立即中断执行并记录安全审计日志。

---

## 详细实施步骤与修改清单

### 步骤 1: 新增 `PathSafetyGuard` 路径安全校验工具类
在 `Services/` 下新建 [PathSafetyGuard.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/PathSafetyGuard.cs)，提供以下接口：
* `string NormalizeAndValidatePath(string rawPath, string baseDirectory, string context)`：将路径标准化，并确保该路径是 `baseDirectory` 的子路径（杜绝 `..` 等路径穿越）。如果校验失败，直接抛出 `UnauthorizedAccessException`。

### 步骤 2: 重构 `SqlToCsvHandler` 与 `SqlCommandHandler`
修改 [SqlBatchStepHandlers.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/BatchJob/SqlBatchStepHandlers.cs)：
1. 注入 `ProjectManager` 从而获取项目的基目录 `ProjectDir`；
2. 在 `ExecuteAsync` 内部获取租户的项目配置，使用 `PathSafetyGuard` 验证 `OutputFile` 和 `SqlFile`；
3. 对于没有指定项目名的系统级 Job，使用临时安全目录或严格受限的输出范围。

### 步骤 3: 优化 AI `query_data` 工具的安全标识符验证
修改 [AiToolRegistryInitializer.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/AI/AiToolRegistryInitializer.cs)：
1. 在 `query_data` 的 `filters` 解析中，对每个字段使用 `SqlSafetyGuard.EnsureIdentifier` 进行最严格的词法匹配；
2. 对操作符 `op` 严格限制在 `["=", "!=", "<", "<=", ">", ">=", "like"]`；
3. 使用 `SqlSafetyGuard` 防御潜在的 SQL 绕过危险。

---

## 重构代码预览与验证

### 1. `PathSafetyGuard.cs` 伪代码
```csharp
public static class PathSafetyGuard
{
    public static string NormalizeAndValidatePath(string? rawPath, string baseDir, string context)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            throw new ArgumentException("Path cannot be empty", nameof(rawPath));
            
        var fullBase = Path.GetFullPath(baseDir);
        var resolvedPath = rawPath;
        
        // 如果是相对路径，结合 baseDir 解析
        if (!Path.IsPathRooted(resolvedPath))
        {
            resolvedPath = Path.Combine(fullBase, resolvedPath);
        }
        
        var fullTarget = Path.GetFullPath(resolvedPath);
        
        if (!fullTarget.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Path traversal detected in '{context}': '{rawPath}' is outside base directory '{baseDir}'");
        }
        
        return fullTarget;
    }
}
```

### 2. `SqlBatchStepHandlers.cs` 重构示例
```csharp
// 在 SqlToCsvHandler.cs 中
var projectInfo = _projectManager.Get(projectName);
var baseDir = projectInfo?.ProjectDir ?? Path.Combine(_env.ContentRootPath, "projects", projectName ?? "default");
var safeOutputFile = PathSafetyGuard.NormalizeAndValidatePath(job.Settings.OutputFile, baseDir, "OutputFile");
```

---

## 验证与发布规范
1. **编译验证**：确保重构后代码 `dotnet build` 编译成功，0 错误；
2. **测试验证**：运行 `dotnet test`，确保原有的单元测试和集成测试全部通过，且新增路径安全拦截的单元测试；
3. **安全审查**：确认没有在任何租户配置中硬编码绝对路径。
