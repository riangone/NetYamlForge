# NetYamlForge 优化改造 —— 详细设计与实施说明

> 面向对象：**执行代码实现的 AI / 工程师**。本文档给出精确到文件、行号、验收标准的可执行任务，实施者无需再做需求判断，只需按 WI（Work Item）逐项落地并跑通验证。
>
> 分支：`nyf` ｜ 代码根：`/home/ubuntu/ws/NetYamlForge` ｜ 主工程目录：`NetYamlForge/`
> 生成日期：2026-07-06（行号基于此刻 HEAD，实施前请以 `grep` 复核，代码若已变动以实际为准）

---

## 0. 通用约定（所有 WI 必读）

- **不改变对外行为**：除非 WI 明确要求，重构不得改变 API 契约、返回结构、错误码。
- **每个 WI 独立成 PR/commit**，互不阻塞，可并行分派给不同实施者。
- **完成定义（DoD）统一为**：
  1. `dotnet build NetYamlForge.slnx` 0 error；
  2. `dotnet test`（`NetYamlForge.Tests`）全绿，且相关新增测试通过；
  3. WI 内列出的"验收判据"逐条满足；
  4. 变更文件顶部若有职责说明注释需同步更新。
- **行号复核命令**：每个 WI 首行给出，实施前先跑一次确认锚点未漂移。
- **优先级**：P0 = 隐患/正确性，P1 = 结构可维护性，P2 = 组织优化。建议顺序 P0 → P1 → P2。

---

## WI-01 [P0] 消除 sync-over-async（4 处）

### 复核命令
```bash
grep -rn "GetAwaiter().GetResult()" --include=*.cs NetYamlForge/ | grep -v -E "/bin/|/obj/"
```

### 现状（4 个站点）
| # | 文件 | 行 | 调用 |
|---|------|----|------|
| 1 | `NetYamlForge/Services/PdfFontLoader.cs` | 69 | `fontService.GetRegularFontPathAsync().GetAwaiter().GetResult()` |
| 2 | `NetYamlForge/Services/PdfFontLoader.cs` | 104 | `fontService.GetBoldFontPathAsync().GetAwaiter().GetResult()` |
| 3 | `NetYamlForge/Services/BatchJob/BatchJobExecutor.cs` | 274 | `_connectionManager.GetConnectionAsync(projectName).GetAwaiter().GetResult()` |
| 4 | `NetYamlForge/Extensions/ServiceCollectionExtensions.cs` | 157 | `connectionManager.GetConnectionAsync(scope.Current.Name).GetAwaiter().GetResult()` |

### 风险
在 ASP.NET Core 请求线程池中同步阻塞等待异步任务，可能导致线程饥饿与死锁，高并发下尤甚。

### 改造方案（按站点）

**站点 1、2 — PdfFontLoader（同步构造/加载路径）**
- 根因：字体加载发生在同步路径（无法直接 `await`）。
- 首选方案：**启动时预加载 + 缓存**。新增 `Task InitializeAsync()`，在 DI 启动阶段（`IHostedService` 或 `ServiceCollectionExtensions` 的初始化钩子）异步解析一次字体路径并缓存为字段，运行期只读缓存字段，彻底移除请求路径上的阻塞。
- 次选方案（若预加载不可行）：将 `PdfFontLoader` 的消费方法链改为 `async`，把 `GetAwaiter().GetResult()` 换成 `await`，一路上溯至 `DocumentPdfService`。

**站点 3 — BatchJobExecutor:274（已在 async 上下文中）**
- 该方法几乎肯定已是 `async`（批处理执行器）。直接改为：
  ```csharp
  var connection = await _connectionManager.GetConnectionAsync(projectName);
  ```
- 确认方法签名为 `async Task`，如否则将所在方法提升为 async 并向上传播 `await`。

**站点 4 — ServiceCollectionExtensions:157（DI factory 委托）**
- 这是 DI 注册的工厂委托，签名同步，不能直接 `await`。
- 方案：将该服务的**连接获取延迟到首次使用时**（注册为返回一个持有 `IConnectionManager` 引用的包装/惰性代理），或改为注册 `async` 工厂（若容器支持），避免在解析期同步阻塞。
- 若架构不允许惰性化，则**在文件顶部记录技术债 TODO**，并确保此调用只在应用启动（非请求线程）执行——需在 WI 说明中明确它是否处于请求热路径。

### 验收判据
- 上述复核命令输出为空（0 处 `GetAwaiter().GetResult()`），或剩余站点附带明确的"仅启动期执行"技术债注释与理由。
- 新增/修改后 `dotnet test` 全绿；补一条针对字体预加载的单测（缓存命中不触发异步解析）。

---

## WI-02 [P1] 拆分 PageController（1052 行）

### 复核命令
```bash
wc -l NetYamlForge/Controllers/PageController.cs
ls NetYamlForge/Controllers/ | grep -i Actions   # 现有 partial 先例
```

### 现状
`NetYamlForge/Controllers/PageController.cs` = 1052 行。仓库已有 partial 拆分先例：`Controllers/DynamicEntityController.Actions.cs`。**沿用同一模式**。

### 改造方案
1. 保持类名 `PageController` 与 `partial class` 声明不变，按职责域拆为多个 partial 文件（同目录、同命名空间）：
   - `PageController.cs`：类声明、构造函数、字段、DI 依赖（"骨架"）。
   - `PageController.Query.cs`：页面/数据查询类 action。
   - `PageController.Render.cs`：渲染/视图组装类 action。
   - `PageController.Actions.cs`：写操作/动作触发类 action。
   （按实际 action 分布调整分组，目标每文件 ≤ ~350 行。）
2. **纯移动，不改逻辑**：只把方法搬到对应 partial 文件，签名、特性（`[HttpGet]` 等）、路由完全保留。
3. 私有 helper 跟随其唯一调用方所在的 partial；被多方共用的 helper 留在骨架文件。

### 验收判据
- 每个 partial 文件 ≤ 400 行；骨架文件只含字段/构造/共享成员。
- 路由表不变：拆分前后 `dotnet build` 后 Swagger/路由数量一致。
- 无行为变更，现有针对 Page 的测试全绿。

---

## WI-03 [P1] EntityMetadata 模型贫血化（919 行）

### 复核命令
```bash
wc -l NetYamlForge/Models/EntityMetadata.cs
grep -nE "public .*\(|void |Task<|bool Validate|Parse" NetYamlForge/Models/EntityMetadata.cs | head -50
```

### 现状
`NetYamlForge/Models/EntityMetadata.cs` = 919 行。一个"模型"近千行，通常已混入解析/校验行为逻辑，违反"模型即数据"。

### 改造方案
1. 先审计：列出该文件中所有**非纯数据成员**（解析方法、校验方法、派生计算、YAML 映射逻辑）。
2. 将行为逻辑抽到独立服务：
   - `Services/DynamicEntity/EntityMetadataParser.cs`（解析/构建 metadata）。
   - `Services/Validation/EntityMetadataValidator.cs`（校验规则；若已有 Validation 服务则并入）。
3. `EntityMetadata` 回归 POCO：属性 + 极简不可变逻辑（如只读派生属性可保留）。
4. 通过 DI 注册新服务，调用点从 `metadata.DoX()` 改为 `_metadataService.DoX(metadata)`。

### 注意 / 风险
- 这是**行为语义迁移**，比 WI-02 风险高。必须：先为现有解析/校验路径补齐特征测试（characterization tests）锁定当前行为，再迁移。
- 分两步提交：① 加测试；② 迁移逻辑。

### 验收判据
- `EntityMetadata.cs` ≤ ~400 行，且无 I/O、无 YAML 解析、无复杂校验分支。
- 新服务有单测覆盖迁移出的每条规则；迁移前后特征测试输出一致。

---

## WI-04 [P1] 后续巨型文件拆分（候选清单，逐个立项）

### 复核命令
```bash
for f in Services/DynamicEntity/DynamicCrudRepository.cs Services/DocumentPdfService.cs Services/Auth/UserAuthService.cs; do printf "%6s  %s\n" "$(wc -l < NetYamlForge/$f)" "$f"; done
```

| 文件 | 行 | 建议切分维度 |
|------|----|-------------|
| `Services/DynamicEntity/DynamicCrudRepository.cs` | 924 | 按 CRUD 动词分 partial（Read / Write / Query-Build / Mapping）；SQL 构造逻辑抽到独立 `*QueryBuilder` |
| `Services/DocumentPdfService.cs` | 782 | 按文档区块（布局 / 表格 / 字体&样式 / 导出）拆；与 WI-01 字体预加载协同 |
| `Services/Auth/UserAuthService.cs` | 766 | 按职责拆：认证(登录/令牌) / 用户管理(CRUD) / 密码&凭据；**安全敏感，务必先补测试** |

- 每个文件单独立为 WI（WI-04a/b/c），沿用 WI-02（partial 纯移动）或 WI-03（行为抽服务）的模式，取决于内容是"多 action/多方法同类"还是"混入异构逻辑"。
- **UserAuthService 特别提示**：涉及安全，任何拆分前必须有认证/授权路径的集成测试兜底；不得在拆分中顺手改动密码哈希、令牌校验逻辑。

### 验收判据
- 每文件拆后 ≤ 400 行；行为不变；相关测试全绿。

---

## WI-05 [P2] 依赖注入按功能模块聚合

### 复核命令
```bash
ls NetYamlForge/Extensions/
grep -nE "AddScoped|AddSingleton|AddTransient|services\.Add" NetYamlForge/Program.cs
grep -rnE "public static IServiceCollection Add" NetYamlForge/Extensions/
```

### 现状
`Program.cs` 仅少量注册，其余分散在 `Extensions/ServiceCollectionExtensions.cs`、`HotReloadServiceCollectionExtensions.cs`。对声明式/可插拔框架而言，DI 应按 **feature 模块**聚合。

### 改造方案
提供一组 feature-based 扩展方法，每个封装本模块全部注册：
```csharp
services
  .AddNetYamlForgeCore()
  .AddNetYamlForgeAuth()
  .AddNetYamlForgeValidation()
  .AddNetYamlForgeDynamicEntity()
  .AddNetYamlForgeApi()
  .AddNetYamlForgeBatchJob()
  .AddNetYamlForgeAI()
  .AddNetYamlForgeConnection();
```
- 按 `Services/` 下现有子目录（Auth / Validation / Api / DynamicEntity / BatchJob / AI / Connection / Webhook）划分模块。
- 每个扩展方法放独立文件 `Extensions/{Module}ServiceCollectionExtensions.cs`。
- `Program.cs` 只保留对这些聚合方法的调用，成为"可读的装配清单"。
- **纯搬迁注册代码**，不改生命周期（Scoped/Singleton/Transient 保持原样）。

### 验收判据
- `Program.cs` 中不再出现零散 `AddScoped/AddSingleton`，全部走 feature 扩展方法。
- 应用启动成功，DI 解析无缺失（跑一次冒烟：启动 + 命中一个各模块端点）。

---

## WI-06 [P2] 异常处理可观测性与统一错误出口

### 复核命令
```bash
grep -rn "catch (Exception" --include=*.cs NetYamlForge/ | grep -v -E "/bin/|/obj/" | wc -l
ls NetYamlForge/Services/CommandErrorCodes.cs NetYamlForge/Services/CommandErrorHttpMapper.cs
```

### 现状
约 143 处 `catch (Exception)`，**无纯吞异常（空 catch 为 0）**——基础健康。已存在 `Services/CommandErrorCodes.cs` 与 `Services/CommandErrorHttpMapper.cs`。目标是让所有 catch 出口统一走这套错误码/HTTP 映射，并带结构化日志。

### 改造方案
1. **审计**：抽查全部 `catch (Exception)` 出口，标注两类问题：
   - (a) 未记录结构化日志，或日志缺上下文（缺项目名/实体名/操作名）；
   - (b) 直接 `return 500` / 直接抛出，未经 `CommandErrorHttpMapper` 映射。
2. **统一模式**：为控制器层引入统一异常处理（`ExceptionFilter` 或中间件），把未分类异常经 `CommandErrorCodes` → `CommandErrorHttpMapper` 输出为一致的 `CommandResult` 结构。
3. **日志规范**：每个保留的业务 catch 记录 `logger.LogError(ex, "{Operation} failed for project={Project} entity={Entity}", ...)`，用结构化字段而非字符串拼接。
4. 输出一份《catch 审计表》（附在 PR 描述）：文件:行 → 现状 → 处理决定（已统一 / 保留原样+理由）。

### 注意
- 这是**扫描+规范化**类工作，范围大。建议先做控制器层统一中间件（覆盖面最大、收益最高），Service 内层 catch 分批处理。
- 不得吞掉原本会向上传播的异常语义；统一出口不等于"全部降级为 200"。

### 验收判据
- 存在统一异常中间件/过滤器，未分类异常不再直出裸 500。
- 抽查 20 处 catch：均有结构化日志 + 上下文字段。
- 错误响应结构与现有 `CommandResult` 契约一致，测试全绿。

---

## 实施顺序建议

```
P0: WI-01（sync-over-async，隐患，先做）
P1: WI-02（PageController，低风险纯搬迁，快速见效）
    WI-04a/b/c（其余巨型文件，可并行）
    WI-03（EntityMetadata，需特征测试，风险中）
P2: WI-05（DI 聚合，结构收益）
    WI-06（异常出口统一，范围大，分批）
```

## 全局验收（合入主干前）
1. `dotnet build NetYamlForge.slnx` → 0 error / 0 warning 新增；
2. `dotnet test` 全绿（当前基线：80 文件 / 437 test）；
3. 应用冒烟启动通过，各模块端点各命中一次无 500；
4. 无 WI 引入对外契约破坏。

---

*本文档由架构分析生成，所有路径/行号均经真实代码核验（HEAD@nyf, 2026-07-06）。实施者若发现锚点漂移，以复核命令的实际输出为准并在 PR 中标注。*
