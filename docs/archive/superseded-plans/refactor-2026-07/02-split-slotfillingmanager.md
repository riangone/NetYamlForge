# 02 — SlotFillingManager 状态机 / 存储解耦

> 目标文件: `NetYamlForge/Services/AI/SlotFillingManager.cs`（717 行）
> 类型: 结构重构（行为不变） · 风险: 中 · 依赖: 无

## 1. 现状分析（实测）

单文件同时承载了 **DTO 定义 + 会话存储 + FSM 状态 + 场景加载 + 业务默认值**：

| 职责 | 成员（行号） |
|------|--------------|
| DTO/模型 | `SlotSession`(78)、`SlotInfo`(102)、`SlotRequest`(116)、`ScenarioDefinition`(126) |
| 会话存储 | `_sessions`(139) ConcurrentDictionary、`GetSessionKey`(170)、`CreateNewSession`(327)、`EnsureSessionsLoadedAsync`(378) |
| FSM 状态 | `_fsmStates`(140)、`GetFsmKey`(176)、`UpdateFsmStateAsync`、`GetCurrentFsmStateAsync`、`GetAllowedToolsAsync`、`IsToolAllowedAsync` |
| Slot 编排 | `GetSessionAsync`(183)、`UpdateSlotAsync`(204)、`IsCompleteAsync`(231)、`GetNextRequiredSlotAsync`(242)、`ResetAsync`(266)、`GetCollectedSlotsAsync`(301) |
| 场景解析 | `DetectScenarioFromMessage`(362, static)、`_aiScenarioYamlLoader` |
| **业务硬编码** | `DefaultProjectId = "auto-dealer-demo"`(144) ⚠️ |

**两个红点**：
1. 会话存储与 FSM 存储都是**进程内 `ConcurrentDictionary`**——耦合在 manager 里，无法替换为分布式存储、无法单测。
2. `DefaultProjectId = "auto-dealer-demo"` 是**业务专属值硬编码进框架 core**（与既有"core 不应含业务名词"的重构方向冲突）。

## 2. 目标结构

```
Services/AI/SlotFilling/
  SlotFillingModels.cs          // SlotSession / SlotInfo / SlotRequest / ScenarioDefinition（纯 DTO）
  ISlotSessionStore.cs          // 会话存储抽象
  InMemorySlotSessionStore.cs   // 现有 ConcurrentDictionary 实现（行为不变）
  IConversationFsmStore.cs      // FSM 状态存储抽象
  InMemoryConversationFsmStore.cs
  ScenarioResolver.cs           // DetectScenarioFromMessage + YAML 场景加载封装
  SlotFillingManager.cs         // 仅保留编排，组合上述依赖（目标 ~250 行）
```

## 3. 详细拆分映射

### 3.1 `SlotFillingModels.cs`
- 迁入 `SlotSession`/`SlotInfo`/`SlotRequest`/`ScenarioDefinition`（78-135），**只搬不改**，含 `IsComplete`、`GetMissingSlots`、`GetCollectedValues` 等计算属性。

### 3.2 `ISlotSessionStore` + `InMemorySlotSessionStore`
```csharp
public interface ISlotSessionStore {
    bool TryGet(string sessionKey, out SlotSession session);
    void Set(string sessionKey, SlotSession session);
    void Remove(string sessionKey);
    IEnumerable<SlotSession> ForConversation(string conversationId); // GetActiveScenario 需要
}
```
- 实现内迁 `_sessions` ConcurrentDictionary，保持并发语义。
- `GetSessionKey`/`GetFsmKey` 的 key 组装逻辑随存储走或留在 manager——**保持 key 格式字符串完全一致**（否则会话丢失）。

### 3.3 `IConversationFsmStore` + `InMemoryConversationFsmStore`
- 迁入 `_fsmStates` 与 FSM 读写（`UpdateFsmStateAsync`/`GetCurrentFsmStateAsync`/`GetAllowedToolsAsync`/`IsToolAllowedAsync` 中涉及存储的部分）。
- FSM 的转移规则若来自 `IConversationFsm`，保留其接口，仅把"存储"抽走。

### 3.4 `ScenarioResolver`
- 迁入 `DetectScenarioFromMessage`(362) 与对 `_aiScenarioYamlLoader` 的调用封装。
- `DetectScenarioFromMessage` 是纯函数，改为 `ScenarioResolver` 的方法或保持 static，便于单测。

### 3.5 消除业务硬编码 `DefaultProjectId`
- **不允许**保留 `"auto-dealer-demo"` 字面量在 core。改为：
  - 从配置读取：`SlotFillingOptions.DefaultProjectId`（`IOptions<SlotFillingOptions>`），默认值为 `null` 或空。
  - `GetResolvedProjectId(string?)`(161) 改为：入参为空 → 取 options 默认 → 仍为空则抛出明确异常或走 `ProjectScope`。
- 在 `appsettings`/项目配置里为 auto-dealer 显式设置该默认值，**把业务值移出代码**。
- ⚠️ 这是唯一一处"行为可能变化"点：确认没有测试/调用依赖该硬编码 fallback；若有，配置里补齐等价值。

### 3.6 `SlotFillingManager`（瘦身后）
- 构造注入 `ISlotSessionStore`、`IConversationFsmStore`、`ScenarioResolver`、`IOptions<SlotFillingOptions>`、`ILogger`、`IServiceScopeFactory`。
- 保留 `ISlotFillingManager` 全部 public 方法签名不变，方法体改为委托给上述依赖。

## 4. DI 注册

```csharp
services.AddSingleton<ISlotSessionStore, InMemorySlotSessionStore>();
services.AddSingleton<IConversationFsmStore, InMemoryConversationFsmStore>();
services.AddSingleton<ScenarioResolver>();
services.Configure<SlotFillingOptions>(config.GetSection("SlotFilling"));
// ISlotFillingManager 生命周期保持不变（核对现状，很可能 Singleton，因持有内存状态）
```
> **生命周期一致性**：原 manager 持内存 dict，多半是 Singleton；两个 store 也须 Singleton，否则会话状态丢失。

## 5. 测试策略

- 新增 `InMemorySlotSessionStoreTests`、`ScenarioResolverTests`（`DetectScenarioFromMessage` 纯函数用例）。
- 回归：现有 AI/slot 相关测试全绿；重点验证 key 格式与"默认 projectId"路径。

## 6. 验收标准

- [ ] core 中不再出现 `"auto-dealer-demo"` 字面量（`grep -rn "auto-dealer-demo" NetYamlForge/` 仅命中配置/文档）
- [ ] 存储可替换（接口就位），默认实现行为与原一致
- [ ] `SlotFillingManager.cs` ≤ 280 行
- [ ] build + test 全绿
