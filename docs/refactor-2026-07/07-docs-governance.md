# 07 — docs/ 文档治理与归档

> 范围: `docs/`（实测 60+ 个 md，框架文档与单项目文档混杂） · 风险: 极低 · 依赖: 无
> 建议**第一个执行**（零代码风险，先理清工作区）。

## 1. 现状分析（实测）

`docs/` 根目录混放了三类内容：

| 类别 | 示例文件 | 问题 |
|------|----------|------|
| A. 框架级设计（当前有效） | `NetYamlForge框架详细说明.md`、`CORE-SLIMMING-REFACTOR-DESIGN.md`、`configuration-reference.md`、`HOTRELOAD.md` | 应留在框架文档区 |
| B. 单项目文档（auto-dealer 等） | `AUTO-DEALER-*.md`(4)、`auto-dealer-*.md`(10+)、`汽车销售系统全业务自动化集成测试报告.md`、`BLOG_UPGRADE_REPORT.md` | 与框架无关，污染框架文档区 |
| C. 过期/迭代计划 | `FRAMEWORK-IMPROVEMENTS-PLAN.md` + `-V2.md`、`IMPROVEMENT-PLAN-2026-06.md`、`IMPROVEMENT-DESIGN-2026-07*.md`、`设计1.md`/`设计2.md`/`fangan1.md`、`EVOLUTION_PLAN.md` | 多份并存，无法判断哪份"当前生效" |

已存在 `docs/archive/`，但未系统使用。

## 2. 目标目录结构

```
docs/
  README.md                     // 【新增】文档索引：分类 + "当前生效"指针
  framework/                    // 框架级设计与参考（A 类）
    architecture/
    configuration-reference.md
    hotreload.md
    ...
  projects/                     // 单项目文档（B 类），按项目分子目录
    auto-dealer/                // 已有 docs/auto-dealer/，合并所有 AUTO-DEALER-*/auto-dealer-*
    blog/
  roadmap/
    CURRENT.md                  // 【新增】唯一"当前生效"路线图（合并自 PLAN v1/v2）
  archive/                      // 过期文档（C 类历史版本），只进不出
    2026-06/
    superseded-plans/
```

## 3. 具体迁移清单（可直接执行）

> 用 `git mv` 保留历史。**只移动/合并，不删除内容**（过期的进 archive）。

### 3.1 B 类 → `docs/projects/auto-dealer/`
```
AUTO-DEALER-COMPREHENSIVE-REDESIGN.md, AUTO-DEALER-IMPROVEMENT-PLAN.md,
AUTO-DEALER-ROLE-BASED-UX-DESIGN.md, AUTO-DEALER-SYSTEM-ORGANIZED.md,
auto-dealer-*.md (全部), 汽车销售系统全业务自动化集成测试报告.md
→ git mv 到 docs/projects/auto-dealer/（与既有 docs/auto-dealer/ 合并）
BLOG_UPGRADE_REPORT.md → docs/projects/blog/
```

### 3.2 C 类过期计划 → 合并 or 归档
- 新建 `docs/roadmap/CURRENT.md`：把 `FRAMEWORK-IMPROVEMENTS-PLAN.md`、`-V2.md`、`IMPROVEMENT-PLAN-2026-06.md`、`IMPROVEMENT-DESIGN-2026-07*.md` 中**仍未完成**的条目汇总为一份。
- 原始文件 → `docs/archive/superseded-plans/`，并在每个文件顶部加一行：`> ⚠️ 已归档，当前生效版本见 docs/roadmap/CURRENT.md`。
- `设计1.md`/`设计2.md`/`fangan1.md`（命名无意义）→ 判定归属：属 auto-dealer 则进 projects，属历史则进 archive，并在 `CURRENT.md`/项目 README 里补上下文。

### 3.3 A 类 → `docs/framework/`
- 框架级设计文档迁入，`docs/refactor-2026-07/`（本文档集）作为 `docs/framework/` 的子项或保留独立，二选一并在 `docs/README.md` 索引。

## 4. `docs/README.md` 索引模板

```markdown
# NetYamlForge 文档索引
## 框架设计（framework/）— 当前有效
- 架构总览: framework/architecture/...
- 配置参考: framework/configuration-reference.md
## 路线图
- ✅ 当前生效: roadmap/CURRENT.md   ← 唯一权威
## 项目文档（projects/）
- auto-dealer: projects/auto-dealer/
## 归档（archive/）— 仅历史，勿据此实施
```

## 5. 验收标准

- [ ] `docs/` 根目录不再直接堆放单项目 md（B 类全部进 `projects/`）
- [ ] 存在唯一 `docs/roadmap/CURRENT.md`，旧计划均加"已归档"指针
- [ ] `docs/README.md` 索引可在 30 秒内定位"当前有效"文档
- [ ] 全部用 `git mv`，历史可追溯，无内容丢失
