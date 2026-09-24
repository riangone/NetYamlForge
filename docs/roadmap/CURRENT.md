# NetYamlForge 当前生效路线图 (Roadmap CURRENT)

> 状态: **执行中** · 最后更新: 2026-09-14
>
> **维护规则**：每次合入一轮 refactor/feature 后，随手更新本文件（挪动条目到"已完成"、更新"进行中"状态），
> 不要让它变成只在改造开始时写一次、之后再也不碰的静态文档——那是它在 2026-07-06 之后两个月失修的直接原因。

---

## 🎯 项目定位（2026-09 起生效）

NetYamlForge 定位为 **"security-hardened scaffold"**：为 AI 生成的 YAML/SQL/Hook 代码提供
编译期 + 运行期护栏，让"AI 写坏配置"在构建或运行早期就被拦截，而不是依赖人工 review。
详见 `README.md` 与 `SECURITY.md`。日常提交（尤其是涉及 Services/BatchJob、AI Tool、
多租户路径的改动）应优先服务于这个定位，而不是继续铺新的业务垂类 demo。

---

## 🔴 已关闭的高优先级安全缺口（2026-09 已闭环）

**AI 会话 / Tool 越权防护 — `ProjectScope` 为空时退化为 `default` scope [已完成]**

- 现状：已彻底关闭；移除 `DefaultProjectId` 隐式回退降级，改为强类型 Fail-Closed 熔断（`TenantContextMissingException`），并在 `AiToolOrchestrator` 中统一记录 `AI_SECURITY_VIOLATION` 审计日志；配套完成 `AiSessionTenantIsolationSecurityTests` 与 `RateLimitingAndBruteForceSecurityTests` 回归验证。

---

## ✅ 已完成并归档的改造轮次

- **Phase 1: 核心安全与多租户隔离护栏闭环（P0）**：
  - AI 会话租户强绑定与 Fail-Closed 熔断护栏（杜绝租户串话）。
  - 生产级速率限制与防爆破中间件（`GlobalRateLimitingMiddleware` 与 `FailedLoginTracker`）流水线正式接入。
- **Phase 2: AI 引擎声明式驱动演进（P1）**：
  - 声明式角色定义与提供者（`IRoleDefinitionProvider` / `roles.yaml`），彻底移除 C# switch-case 硬编码。
  - 声明式意图路由（`IIntentRouterService` / `AiScenarioConfig.Intents`）。
  - MCP（Model Context Protocol）标准服务接入端点与适配层落地。
- **Phase 3: 运行时基础设施治理与简化（P2）**：
  - 剥离自研双层连接池，直连 ADO.NET 原生连接池（`DbConnectionFactory` 作为 Thin Facade 门面，动态映射 `ConnectionPoolOptions`）。
  - 分布式租约服务（`IDistributedLeaseService` / `DistributedLeaseService`）落地，支持原子抢占、心跳续期与主动释放。
  - 批处理调度器 Master 租约（Leader Election）与基于时间窗口的确定性幂等入队（`idempotency_key`）。
  - 持久化 Outbox 队列 Worker 租约超时锁定与僵死任务自动接管回收。
- **Demo 演示项目全量清理与核心瘦身**：
  - 彻底清理历史业务 demo 与测试辅助脚本，删除 `security-demo` 冗余实例并将其测试用例资产归置至测试夹具（`NetYamlForge.Tests/TestProjects`），清理演示上传文件与临时产物，聚焦纯粹的核心框架与 AgentStudio 自举演进工作台。
- 历史架构改造轮次（结构拆分、异常可观测性、方言契约测试、Schema 启动 fail-fast、OpenTelemetry、Hook SDK 化、性能基线、安全回归套件等）：
  - `docs/archive/superseded-plans/refactor-2026-07/`
  - `docs/archive/superseded-plans/refactor-2026-07-r2/`
