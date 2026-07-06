# NetYamlForge 当前生效路线图 (Roadmap CURRENT)

> 状态: **执行中** · 最后更新: 2026-07-06

本文档汇总了自 `FRAMEWORK-IMPROVEMENTS-PLAN.md`、`-V2.md`、`IMPROVEMENT-PLAN-2026-06.md` 等历史计划以来所有**尚未完成**或**当前正在进行**的框架优化与重构任务。

---

## 🏃 当前进行中任务 (2026-07 优化改造)

根据 [docs/refactor-2026-07/README.md](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/README.md) 设计，目前正按顺序执行以下 8 项优化重构：

### 1. ProjectHookLoader 按职责拆分 (Refactor 01)
- **目标**: 将 721 行的 `ProjectHookLoader` 拆分为编译、ALC 生命周期管理、锁和诊断四大职责，减少代码复杂度。
- **参考**: [01-split-projecthookloader.md](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/01-split-projecthookloader.md)

### 2. SlotFillingManager 状态机与存储解耦 (Refactor 02)
- **目标**: 解除 `SlotFillingManager` 的状态机硬编码，将其解耦，抽离场景，并移除 `"auto-dealer-demo"` 硬编码。
- **参考**: [02-split-slotfillingmanager.md](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/02-split-slotfillingmanager.md)

### 3. EntityMetadata / PageDefinition 配置模型拆文件 (Refactor 03)
- **目标**: 纯拆文件，将巨型配置模型类按类型分拆到独立文件中，保持 Models 纯粹性。
- **参考**: [03-split-models.md](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/03-split-models.md)

### 4. SqlExpressionParser 拆文件并补全安全回归测试 (Refactor 04)
- **目标**: 将 SqlExpressionParser 的 Tokenizer 与 Parser 拆分，补齐安全敏感路径的单元测试。
- **参考**: [04-split-sqlexpressionparser.md](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/04-split-sqlexpressionparser.md)

### 5. ApiEntityController 控制器业务下沉 (Refactor 05)
- **目标**: 瘦身控制器，把其中的业务逻辑下沉到相应的 Service 层，遵循“控制器不包含业务逻辑”的原则。
- **参考**: [05-thin-apientitycontroller.md](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/05-thin-apientitycontroller.md)

### 6. catch 异常统一约定与结构化日志改造 (Refactor 06)
- **目标**: 统一 catch 块处理，接入结构化日志与 RequestId/CorrelationId 关联，提升低代码框架排障可观测性。
- **参考**: [06-exception-observability.md](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/06-exception-observability.md)

### 7. 文档治理与归档 (Refactor 07)
- **目标**: 整理 `docs/` 目录，将单项目文档与框架设计隔离，规范路线图。
- **参考**: [07-docs-governance.md](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/07-docs-governance.md)

### 8. 跨数据库方言一致性契约测试 (Refactor 08)
- **目标**: 新建跨 MySQL/SQL Server/PostgreSQL/SQLite 的方言契约测试矩阵，防止语法差异引起线上故障。
- **参考**: [08-dialect-contract-tests.md](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/08-dialect-contract-tests.md)

---

## 📅 历史未尽事项与中长期路线图

以下是自历史计划中继承、仍需在中长期予以关注的低优先级或架构演进事项：

1. **自定义连接池与驱动内置连接池的深度整合**
   - *源自*: `FRAMEWORK-IMPROVEMENTS-PLAN.md` 问题 6
   - *说明*: 目前双层连接池实现较为稳定，但中长期需考虑剥离自定义池以完全复用 ADO.NET 驱动内置的高性能连接池，简化连接管理架构。
2. **AI 场景配置的完全 YAML 驱动**
   - *源自*: `FRAMEWORK-IMPROVEMENTS-PLAN.md` 问题 7
   - *说明*: 当前 SlotFilling 对话场景的配置已逐步 YAML 化，后续需实现全场景、全意图的声明式驱动，减少 C# 端开发。
