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

## 🔴 当前最高优先级：关闭 SECURITY.md 已知缺口

**AI 会话 / Tool 越权防护 — `ProjectScope` 为空时退化为 `default` scope**

- 现状：`SECURITY.md` "Known gaps" 明确记录此项未关闭；这是唯一一条"承诺了护栏但实际
  还没做到"的缺口，直接影响"safe by construction"这个核心卖点的可信度。
- 涉及组件：`AiToolOrchestrator`、`SlotFillingManager`（会话槽位状态在 CLI 离线模式 /
  异步后台工作流下，`ProjectScope` 可能为空，导致跨租户上下文串话风险）。
- 设计方案：`docs/FRAMEWORK-SECURITY-REFACTOR-PLAN.md` 隐患 3（该文档漏洞 1/2 已落地，
  仅隐患 3 未关闭，其余内容已历史化，不要按文档标题误判为"全部未做"）。
- 建议动作：强制 `projectId` 显式传递，上下文不匹配时立即中断并记录安全审计日志；补充
  并发多租户场景下的回归测试（参考 `SqlBatchStepHandlersSecurityTests.cs` 的测试风格）。

---

## ✅ 已完成并归档的改造轮次

以下两轮设计文档中的工作已通过后续提交落地（结构拆分、异常可观测性、方言契约测试、
Schema 启动 fail-fast、OpenTelemetry、Hook SDK 化、性能基线、安全回归套件等），不再是
"进行中"状态，已归档到 `docs/archive/superseded-plans/`，避免继续在路线图里显示为待办：

- `docs/archive/superseded-plans/refactor-2026-07/`（原"2026-07 优化改造"8 项，结构复杂度治理）
- `docs/archive/superseded-plans/refactor-2026-07-r2/`（原"R2"5 项，配置护栏/可观测性/Hook SDK/性能基线/安全回归）

落地证据见 `git log`：`54dfe7f`、`50830f2`、`460f8b8`、`8680f33`、`b3b44d4`、`95b1a04`、
`708034a`、`a2d53e5`、`41c9788` 等提交；`NetYamlForge.Benchmarks/`、
`NetYamlForge/Services/Diagnostics/ForgeTelemetry.cs`、
`NetYamlForge/Services/Validation/YamlConfigStartupValidator.cs` (`FailFastOnStartup`) 均已在库。

---

## 🧹 进行中：项目 / 文档断舍离（2026-09-14 启动）

目标：让仓库结构匹配 README 已经声明的"4 个 maintained showcase + 其余均为内部测试夹具"
的事实，减少新贡献者/AI 协作者的认知负担。

**本次已执行（git mv，保留历史，未删除任何数据）：**
- 11 个确认为技术 spike 的项目从 `projects/` 移入 `projects/_sandbox/`：
  `ai-card`、`ai-doc-processor`、`dungeon-forge`、`form-forge`、`framework`、
  `golden-template`、`kb-forge`、`memo-app`、`memo2`、`ui-showcase`、`userhome`。
  （`SystemDatabaseInitializer` 已原生支持"迁入 `_sandbox` = 从 projects 表下线"的语义，
  这些项目不会再作为可加载项目出现在 `/UserHome`。）
- 同步修正了因此路径变化而会失效的引用：`docker-compose.yml` 的 `ui_showcase_sqlite`
  卷路径、`NetYamlForge.Tests/AiDocProcessorTests.cs` 的硬编码项目路径。

**待人工决策，本次未动（原因见下，不应在无人确认的情况下删除/迁移）：**
- `projects/jpcs`（"JPiere Contract Service"，198M / 1170 个受 git 追踪的文件）——体量和
  内容都不像"技术验证 demo"，更像一个被提交进框架仓库的真实业务项目。是否属于框架仓库、
  是否应拆分为独立仓库，需要产品/数据归属层面的决策，不是纯技术清理可以单方面执行的。
- `projects/inventory`、`projects/task-management`、`projects/todo-app` ——中等体量，
  README 按"非四个 showcase"的字面标准可以归类为非核心，但未逐一确认是否被其他文档/测试
  作为参考案例引用，暂缓移动，留待下一轮确认。
- 顶层独立目录 `FormForge/`（67M，独立 `.csproj`，直接用 SQLite，不经过 NetYamlForge 核心/
  YAML 管线）——与框架定位无关，建议独立仓库或删除，但删除一个可能仍在使用的独立应用
  风险较高，需要用户明确指示。

**尚未处理（下一轮）：**
- `docs/` 下仍有 ~27 个 `docs/framework/*.md` 与若干 `detailed_design_for_*.md` 未做"是否仍
  有效"的标注；建议按"仍指导当前实现" vs "历史设计稿，应归档"逐一过一遍，比照本次
  refactor-2026-07(-r2) 的归档处理。
- 尚无第三方安全审计（`SECURITY.md` 已自曝这一点）；不属于文档整理范畴，但应作为独立
  路线图项排期。

---

## 📅 中长期低优先级事项（未变更，沿用历史记录）

1. **自定义连接池与驱动内置连接池的深度整合**
   - *源自*: `FRAMEWORK-IMPROVEMENTS-PLAN.md` 问题 6（现已归档于 `docs/archive/superseded-plans/`）
   - *说明*: 目前双层连接池实现较为稳定，但中长期需考虑剥离自定义池以完全复用 ADO.NET 驱动内置的高性能连接池，简化连接管理架构。
2. **AI 场景配置的完全 YAML 驱动**
   - *源自*: `FRAMEWORK-IMPROVEMENTS-PLAN.md` 问题 7（现已归档于 `docs/archive/superseded-plans/`）
   - *说明*: 当前 SlotFilling 对话场景的配置已逐步 YAML 化，后续需实现全场景、全意图的声明式驱动，减少 C# 端开发。
