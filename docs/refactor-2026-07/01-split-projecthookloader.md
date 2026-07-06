# 01 — ProjectHookLoader 按职责拆分

> 目标文件: `NetYamlForge/Services/Project/ProjectHookLoader.cs`（721 行）
> 类型: 结构重构（行为不变） · 风险: 中 · 依赖: 无

## 1. 现状分析（实测方法地图）

`ProjectHookLoader` 单类承担了 **4 类互不相同的职责**，是复杂度集中点：

| 职责 | 相关成员（行号） | 说明 |
|------|------------------|------|
| A. 加载编排（对外接口） | `LoadProjectHooksAsync`(92)、`LoadProjectBusinessLogicAsync`(186)、`LoadProjectActionHandlersAsync`(626)、`UnloadProjectAssemblyAsync`(588) | 4 个 public 入口，遍历目录→编译→注册 |
| B. Roslyn 编译 | `CompileHooksAsync`(429)、`GetMetadataReferences`(342)、`_cachedReferences`/`_refLock`(63-64)、`CalculateSourceHash`(324) | 源码→Assembly，含引用缓存与源码哈希 |
| C. 程序集生命周期 | `CollectibleAssemblyLoadContext`(614)、`UnloadAlcInternal`(549)、`TrackUnload`(565)、`_assemblyContexts`/`_loadedAssemblies`(60-61) | 可回收 ALC、卸载、GC 追踪 |
| D. 诊断/并发 | `GetHookCompileHint`(710)、`GetProjectLock`(87)、`_projectLocks`(62) | 编译错误提示、每项目锁 |

**问题**：加载/编译/缓存/安全校验/卸载混在一个类里，测试困难、改一处影响全局。

## 2. 目标结构

保持 `IProjectHookLoader` 与 `ProjectHookLoader` 的**公共签名完全不变**（DI 注册不动），把 B/C/D 抽成**内部协作类**，由 `ProjectHookLoader` 组合调用。

```
Services/Project/
  ProjectHookLoader.cs              // 仅保留 A：编排 + 4 个 public 入口（目标 ~220 行）
  Loading/
    HookAssemblyCompiler.cs         // B：Roslyn 编译 + 引用缓存 + 源码哈希
    HookMetadataReferenceCache.cs   // B 子项：GetMetadataReferences + _cachedReferences（静态缓存）
    CollectibleAssemblyManager.cs   // C：ALC 创建/卸载/GC 追踪
    HookCompileDiagnostics.cs       // D：GetHookCompileHint（可做成 static）
    ProjectLoadLockRegistry.cs      // D：GetProjectLock + _projectLocks
```

> 命名空间统一 `NetYamlForge.Services.Project`（或 `.Project.Loading`，二选一，全文件一致）。

## 3. 详细拆分映射

### 3.1 `HookMetadataReferenceCache`（新，internal）
- 迁入：`GetMetadataReferences`(342-428)、`_cachedReferences`、`_refLock`。
- 暴露：`IReadOnlyList<MetadataReference> Get()`；保留 double-checked lock 语义不变。
- 单例语义：因原为 `static` 缓存，建议注册为 DI 单例，或保持内部 static 字段。**保持"进程级只算一次"的行为**。

### 3.2 `HookAssemblyCompiler`（新，internal）
- 迁入：`CompileHooksAsync`(429-548)、`CalculateSourceHash`(324-341)。
- 构造注入：`HookMetadataReferenceCache`、`ILogger<HookAssemblyCompiler>`。
- 方法签名：`Task<Assembly?> CompileAsync(string projectName, IEnumerable<string> sourceFiles, AssemblyLoadContext alc)`。
- **注意**：编译产物需加载进指定 ALC——把"加载进 ALC"的动作留在 compiler 还是 manager，需保持与原代码一致的顺序（见 429-548 原实现）。

### 3.3 `CollectibleAssemblyManager`（新，internal）
- 迁入：`CollectibleAssemblyLoadContext`(614-625)、`UnloadAlcInternal`(549-564)、`TrackUnload`(565-587)、`_assemblyContexts`、`_loadedAssemblies`。
- 暴露：
  - `AssemblyLoadContext GetOrCreate(string projectName)`
  - `void Register(string projectName, Assembly asm)`
  - `Task UnloadAsync(string projectName)`（含 `TrackUnload` 的 GC 等待逻辑，保留 `await Task.Delay(1000)` 与 GC 收集次数不变）
- **关键**：`TrackUnload` 的 `Task.Run` + `WeakReference` GC 追踪逻辑**逐行保留**，这是内存正确性敏感区，不要"顺手优化"。

### 3.4 `ProjectLoadLockRegistry`（新，internal）
- 迁入：`GetProjectLock`(87-91)、`_projectLocks`。
- 暴露：`SemaphoreSlim For(string projectName)`。

### 3.5 `HookCompileDiagnostics`（新，internal static）
- 迁入：`GetHookCompileHint`(710-721)，改 `internal static string Hint(string diagnosticId)`。

### 3.6 `ProjectHookLoader`（瘦身后）
- 保留 4 个 public 方法作为**编排**：目录扫描 → `_lockRegistry.For()` → `_compiler.CompileAsync()` → 注册到各 registry → `_assemblyManager.Register()`。
- 构造函数改为注入上述协作类（在 DI 里补注册）。
- 原对 registry（`IProjectHookRegistry` 等）的注册逻辑**留在编排层**。

## 4. DI 注册变更

定位当前注册 `ProjectHookLoader` 的扩展方法（`grep -rn "IProjectHookLoader" NetYamlForge/**/ServiceCollection*`、`AddProject*`）。新增：

```csharp
services.AddSingleton<HookMetadataReferenceCache>();
services.AddSingleton<CollectibleAssemblyManager>();
services.AddSingleton<ProjectLoadLockRegistry>();
services.AddSingleton<HookAssemblyCompiler>();
// ProjectHookLoader 保持原有生命周期不变（很可能是 Singleton，核对现状）
```
> 核对原 `ProjectHookLoader` 生命周期，新协作类须与之**同或更长**生命周期（避免持有的 ConcurrentDictionary 状态被意外重建）。

## 5. 测试策略

- 现有集成测试（HotReload / 项目加载路径）作为回归护栏——**拆分前后必须同样全绿**。
- 新增可选单元测试：
  - `HookCompileDiagnosticsTests`：给定诊断 ID → 期望提示文本（纯函数，易测）。
  - `ProjectLoadLockRegistryTests`：同名返回同一实例、异名不同实例。

## 6. 验收标准

- [ ] `ProjectHookLoader.cs` ≤ 250 行，其余职责在独立文件
- [ ] `IProjectHookLoader` public 签名零变化
- [ ] `TrackUnload` GC 逻辑逐行保留
- [ ] build + test 全绿；HotReload 集成测试通过
