# NetYamlForge 文档索引

欢迎来到 NetYamlForge 框架的文档库。为了方便开发和维护，所有文档已按照功能和类别进行了整理。

---

## 🧭 当前核心路线图

* 🎯 **[当前生效路线图 (Roadmap CURRENT)](file:///home/ubuntu/ws/NetYamlForge/docs/roadmap/CURRENT.md)**
  * 包含了 2026-07 优化重构计划的最新状态，以及从历史版本继承的待办事项。

---

## 🏗️ 框架设计与参考 (docs/framework/)

本目录包含 NetYamlForge 框架本身的架构设计、配置规范以及各核心组件的说明。

* **[框架详细说明](file:///home/ubuntu/ws/NetYamlForge/docs/framework/NetYamlForge框架详细说明.md)** — 框架的基本概念与运行机制。
* **[配置参考手册 (Configuration Reference)](file:///home/ubuntu/ws/NetYamlForge/docs/framework/configuration-reference.md)** — YAML 文件编写与属性指南。
* **[热重载设计 (Hot Reload)](file:///home/ubuntu/ws/NetYamlForge/docs/framework/hotreload.md)** — 动态热编译与加载机制。
* **[核心瘦身重构设计 (Core Slimming)](file:///home/ubuntu/ws/NetYamlForge/docs/framework/CORE-SLIMMING-REFACTOR-DESIGN.md)** — 框架瘦身方案。
* **[CLI 进程池管理 (CLI Process Pool)](file:///home/ubuntu/ws/NetYamlForge/docs/framework/CLI_PROCESS_POOL.md)** — CLI 执行性能优化与连接池管理。
  * [进程池快速参考](file:///home/ubuntu/ws/NetYamlForge/docs/framework/CLI_PROCESS_POOL_QUICK_REFERENCE.md)
* **[连接池 Phase 2 设计](file:///home/ubuntu/ws/NetYamlForge/docs/framework/CONNECTION_POOL_PHASE2.md)** — 数据库连接与多租户策略。

以及其他各个核心特性（Email, Line 集成，Daemon CLI 模式，PostgreSQL，安全等）的具体文档，请参考 [docs/framework/](file:///home/ubuntu/ws/NetYamlForge/docs/framework/)。

---

## 🧪 2026-07 优化重构文档集 (docs/refactor-2026-07/)

* 📖 **[重构主 README 与计划](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/README.md)**
* [01 — ProjectHookLoader 按职责拆分](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/01-split-projecthookloader.md)
* [02 — SlotFillingManager 状态机解耦](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/02-split-slotfillingmanager.md)
* [03 — EntityMetadata / PageDefinition 拆文件](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/03-split-models.md)
* [04 — SqlExpressionParser 拆文件 + 补测试](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/04-split-sqlexpressionparser.md)
* [05 — ApiEntityController 控制器业务下沉](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/05-thin-apientitycontroller.md)
* [06 — catch 异常统一结构化日志约定](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/06-exception-observability.md)
* [07 — docs/ 文档治理与归档](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/07-docs-governance.md)
* [08 — 跨数据库方言一致性契约测试](file:///home/ubuntu/ws/NetYamlForge/docs/refactor-2026-07/08-dialect-contract-tests.md)

---

## 📂 项目应用文档 (docs/projects/)

存放使用 NetYamlForge 框架开发的特定示范或业务系统文档。

* **[Auto-Dealer (汽车销售示范系统)](file:///home/ubuntu/ws/NetYamlForge/docs/projects/auto-dealer/)** — 包含业务流、全自主测试报告、UX 角色系统、AI 驱动规范等。
* **[Blog (个人博客系统)](file:///home/ubuntu/ws/NetYamlForge/docs/projects/blog/)** — 博客系统升级报告。

---

## 🗃️ 历史文档归档 (docs/archive/)

包含历史已过期的规划、建议书等。请勿据此实施，仅作参考。

* **[Superseded Plans (被取代的旧路线图)](file:///home/ubuntu/ws/NetYamlForge/docs/archive/superseded-plans/)**
