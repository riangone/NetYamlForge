# NetYamlForge 优化改造 — 实现设计文档集（2026-07）

> 版本: v1.0 · 日期: 2026-07-06 · 面向对象: 负责代码实现的 AI / 工程师
> 前置基线: 前几轮重构已完成（sync-over-async 基本清零、Services 无空吞异常的核心路径、DI 已分模块、441 测试）。
> 本文档集针对**剩余的、按性价比排序的**优化项，给出可直接落地的详细设计。

---

## 0. 总原则（所有实现必须遵守）

1. **行为不变（behavior-preserving）**：除"异常可观测性"一项会新增日志字段外，其余全部为**纯结构重构**，不得改变任何对外 HTTP/YAML/运行时行为。
2. **公共 API 签名不变**：`public` 接口、方法签名、命名空间保持不变（除非文档明确要求）。拆分优先用 `partial class` 或"同命名空间多文件"，使调用方零改动。
3. **小步提交**：每个文档 = 一个独立 PR/提交，可单独回滚。禁止跨文档大合并。
4. **测试先行/同行**：每次拆分后 `dotnet build` + `dotnet test` 必须全绿；新增能力（Dialect 契约测试）必须带测试。
5. **命名空间**：拆出的新文件沿用原文件命名空间，不引入新命名空间层级，除非文档指定。

## 1. 度量基线（实测 2026-07-06）

| 目标 | 路径 | 行数 |
|------|------|------|
| ProjectHookLoader | `NetYamlForge/Services/Project/ProjectHookLoader.cs` | 721 |
| SlotFillingManager | `NetYamlForge/Services/AI/SlotFillingManager.cs` | 717 |
| EntityMetadata | `NetYamlForge/Models/EntityMetadata.cs` | 637 |
| PageDefinition | `NetYamlForge/Models/PageDefinition.cs` | 561 |
| SqlExpressionParser | `NetYamlForge/Services/SqlExpressionParser.cs` | 555 |
| ApiEntityController | `NetYamlForge/Controllers/ApiEntityController.cs` | 529 |
| Dialect 实现 | `NetYamlForge/Services/Dialect/*.cs` | 5 个方言，**0 契约测试** |
| Services catch 数 | `NetYamlForge/Services/**` | 169（部分裸 `catch`，无结构化日志） |

## 2. 文档清单与优先级

| # | 文档 | 主题 | 类型 | 性价比 | 风险 |
|---|------|------|------|--------|------|
| 01 | [01-split-projecthookloader.md](01-split-projecthookloader.md) | ProjectHookLoader 按职责拆分 | 结构重构 | ★★★ | 中 |
| 02 | [02-split-slotfillingmanager.md](02-split-slotfillingmanager.md) | SlotFillingManager 状态机/存储解耦 | 结构重构 | ★★★ | 中 |
| 03 | [03-split-models.md](03-split-models.md) | EntityMetadata / PageDefinition 拆文件 | 纯拆文件 | ★★ | 低 |
| 04 | [04-split-sqlexpressionparser.md](04-split-sqlexpressionparser.md) | SqlExpressionParser 拆文件 + 补测试 | 拆文件+测试 | ★★ | 低 |
| 05 | [05-thin-apientitycontroller.md](05-thin-apientitycontroller.md) | 控制器业务下沉服务层 | 结构重构 | ★★ | 中 |
| 06 | [06-exception-observability.md](06-exception-observability.md) | catch→结构化日志+关联ID 统一约定 | 增强 | ★★★ | 低 |
| 07 | [07-docs-governance.md](07-docs-governance.md) | docs/ 治理与归档 | 治理 | ★★ | 极低 |
| 08 | [08-dialect-contract-tests.md](08-dialect-contract-tests.md) | 跨方言契约测试 | 新增测试 | ★★★ | 低 |

## 3. 建议实施顺序

1. **先做 07（文档治理）**——零代码风险，先把工作区理清。
2. **06（可观测性约定）**——先定约定与 helper，后续重构顺手接入。
3. **03 / 04（纯拆文件）**——低风险，快速见效，建立"拆分 + 测试"节奏。
4. **08（Dialect 契约测试）**——把方言差异从线上 bug 变 CI 拦截。
5. **01 / 02 / 05（职责重构）**——风险最高，放最后，逐个独立 PR。

## 4. 每个 PR 的统一验收清单（Definition of Done）

- [ ] `dotnet build NetYamlForge.slnx` 无警告新增
- [ ] `dotnet test NetYamlForge.Tests` 全绿（现有 441 + 本次新增）
- [ ] 公共 API 签名 diff 为空（可用 `git diff` 人工核对 `public` 行）
- [ ] 无 `.cs` 文件超过 ~450 行（拆分类文档）
- [ ] PR 描述引用对应设计文档编号
