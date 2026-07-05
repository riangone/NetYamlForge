# 框架改进缺口补完指示（2026-07 第二批）

> 本文档是 `docs/IMPROVEMENT-DESIGN-2026-07.md`（下称"原设计"）的续篇。
> 原设计的 WP1/WP2/WP3/WP5 已实现并通过验收（见下方现状表），本文档只覆盖**剩余缺口**。
> 格式同原设计：每个缺口（G1–G5）含 背景 / 设计 / 改动文件清单 / 验收标准，
> 可直接交给实现 Agent 按「实施顺序」逐项执行，每项独立提交。
> 基线：`nyf` 分支，2026-07-04 工作区（原设计各 WP 的实现尚未提交，与本文档改动同属一批）。

## 现状与缺口总览

已验证完成（勿重做）：

| 原设计 WP | 状态 | 证据 |
|---|---|---|
| WP1 Hook 语义校验 | ✅ | `Services/Project/HookSecurityValidator.cs`；清空 `cache/ProjectHooks` 后全量重编译 0 违规；17 个单测 |
| WP2 SQL 表达式解析器 | ✅ | `Services/SqlExpressionParser.cs`；`SqlSafetyGuard.EnsureExpression` 已委托 |
| WP3 数据迁移主体 | ✅ | `Services/Project/ProjectDataMigrationRunner.cs` + Program.cs CLI/启动挂载 + .db 出库 + 种子 |
| WP5 控制器拆分 | ✅ | 7 个 partial 文件，27 个 action 各出现一次 |
| WP7.1 密码播种 | ✅（代码侧） | `DefaultAdminSeeder` 已实现 环境变量 > 配置 > 随机 优先级 |

**重要勘误（关于原设计 WP4）**：原设计假设"核心 CRUD 管线基本无测试"，经复核不成立。
以下测试**早已存在**，实现 Agent 不得重复编写同等内容：

- `NetYamlForge.Tests/Integration/YamlPipelineEndToEndTests.cs` —— HTTP 层完整
  CRUD（List/Create/Edit/Delete/Detail/自定义 Action，真实 YAML + 租户 SQLite，
  基于 `NetYamlForgeWebApplicationFactory`，样板项目 blog）≈ 原 WP4-C。
- `NetYamlForge.Tests/Services/DynamicEntity/DynamicEntitySchemaMigrationServiceTests.cs`
  —— BuildPlan/GenerateSql/Apply/Rollback/DryRun 含 PostgreSQL 方言 ≈ 原 WP4-B。
- `NetYamlForge.Tests/CrudMainPathTests.cs` + `EntityCrudExecutionServiceTests.cs`
  —— Hook 链顺序/Abort/事务回滚/项目 Hook 优先 ≈ 原 WP4-D。
- `SqlGenerationSnapshotTests.cs`、`DynamicCrudRepositorySecurityTests.cs`、
  `DynamicEntityControllerTests.cs`、`DynamicEntityListQueryServiceTests.cs` 等
  —— SQL 生成快照、字段级权限、控制器分支、列表查询。

因此 WP4 收缩为本文档的 G5（小增量），不再是"大"工作包。

待办缺口与实施顺序：**G1 → G2 → G4 → G3 → G5**。

| # | 标题 | 来源 | 规模 |
|---|---|---|---|
| G1 | QueueStepHandlerBase 试点迁移 + 基类测试 | 原 WP6 后半 | 中 |
| G2 | ProjectDataMigrationRunner 测试 + README 数据迁移章节 | 原 WP3 尾巴 | 小 |
| G3 | YAML JSON Schema 导出全套 | 原 WP7.2 | 小 |
| G4 | 文档/配置修正（README 凭据、kb-forge jobs.yml） | 原 WP7.1 尾巴 + 验证时发现 | 小 |
| G5 | CRUD 测试小增量（仓储往返 + BulkDelete/CSV） | 原 WP4 收缩后 | 小 |

通用约束（同原设计）：

- 每项完成后 `dotnet build --configuration Release` 0 error、`dotnet test` 全绿。
- 启动验证方法（G1/G4 需要）：**必须先清缓存再启动**，否则 Hook 走缓存不经过校验器：
  ```bash
  rm -rf cache/ProjectHooks/* NetYamlForge/cache/ProjectHooks/*
  cd NetYamlForge && ASPNETCORE_URLS=http://localhost:52xx timeout 150 dotnet bin/Release/net10.0/NetYamlForge.dll > /tmp/startup.log 2>&1
  ```
  注意：必须以 `NetYamlForge/`（项目目录）为工作目录启动，否则找不到 `projects/`；
  全量启动需 2 分钟以上，日志断言 `HOOK_SECURITY_VIOLATION`、`Unsafe expression`、
  `ERR]`（kb-forge 修复后应为 0 条）。
- **不要触碰**工作区里三处与本批无关的未提交改动：
  `Views/Shared/_Layout.cshtml`、`projects/blog/views/PostDetail.cshtml`、
  `Services/BatchJob/AutomatedBlogGeneratorExecutor.cs`。它们是另一条工作线，保持原样。

---

## G1 — QueueStepHandlerBase 试点迁移 + 基类测试

### 背景与现状

基类 `Services/BatchJob/QueueStepHandlerBase.cs`（112 行）已创建，但**没有任何 executor
继承它**（全仓 grep 只有定义处一个引用），也没有基类测试——目前是死代码。
原设计 WP6 要求迁移两个试点：`PhotoAnnotatorExecutor`（775 行）与 `AiAnnotatorExecutor`（611 行）。

已实现的基类 API（以实际代码为准，与原设计草案有一处差异——**没有 `MarkProcessingAsync`**）：

```csharp
public abstract class QueueStepHandlerBase<TRow> : IBatchStepHandler
{
    public abstract string StepType { get; }
    protected virtual int DefaultBatchSize => 5;
    protected ILogger Logger { get; set; }   // 派生の ctor で設定

    protected abstract Task<IReadOnlyList<TRow>> FetchPendingAsync(
        BatchJobDefinition job, string? projectName, IDbConnection db, IDbTransaction tx,
        int batchSize, CancellationToken ct);
    protected abstract Task<RowOutcome> ProcessRowAsync(
        TRow row, BatchJobDefinition job, string? projectName, IDbConnection db, IDbTransaction tx,
        CancellationToken ct);
    protected abstract Task WriteOutcomeAsync(TRow row, RowOutcome outcome, IDbConnection db, IDbTransaction tx);
}
```

基类 `ExecuteAsync` 的既有语义（迁移时必须保持）：空队列 → `Success=true, RowsAffected=0`；
逐行 try/catch，异常转 `RowOutcome.Fail(ex.Message)` 且继续处理后续行；
`result.Success = failed == 0 || done > 0`；`RowsAffected = done`。

### 设计

#### 1. 基类补一个虚方法（先于试点迁移）

两个试点都在处理前把行标记为 `processing`（`PhotoAnnotatorExecutor.cs:86-88`、
`AiAnnotatorExecutor.cs:130`）。基类缺这一步。在 `QueueStepHandlerBase` 中增加：

```csharp
/// <summary> 行を processing 状態にマークする（既定は何もしない）。</summary>
protected virtual Task MarkProcessingAsync(TRow row, IDbConnection db, IDbTransaction tx)
    => Task.CompletedTask;
```

在 `ExecuteAsync` 的每行 try 块内、`ProcessRowAsync` 之前调用（`MarkProcessingAsync` 抛异常
时与 `ProcessRowAsync` 抛异常同样处理：Fail + 继续下一行）。

#### 2. 迁移 `PhotoAnnotatorExecutor`

改为 `QueueStepHandlerBase<PhotoAnnotatorExecutor.QueueRow>`，对照现有代码逐块搬移：

| 现有代码（行号基于当前文件） | 迁往 |
|---|---|
| 拉取 SQL（`ExecuteAsync` 内 `SELECT q.queue_id, ... WHERE q.status='queued' AND q.provider=@Provider ... LIMIT @Batch`，:62-69） | `FetchPendingAsync`（provider 解析 `job.AiProvider ?? "antigravity"` 一并移入） |
| `UPDATE processing_queue SET status='processing', started_at=@Now`（:86-88） | `MarkProcessingAsync` |
| 文件存在性检查、`AnnotateAsync` 调用、photos 表写回、`status='done'` 更新（:90-210 的主体） | `ProcessRowAsync`：成功路径返回 `RowOutcome.Ok()`（done 状态更新留在 ProcessRow 内做完）；`File not found` / `AI returned empty...` 返回 `RowOutcome.Fail(原文案)` |
| `FailRow`（:727，retry_count 加一、超限置 failed） | `WriteOutcomeAsync`：`outcome.Status == Failed` 时调用现有 `FailRow` 逻辑；`Ok` 时无操作（done 更新已在 ProcessRow 完成） |

约束：所有 SQL 文本、状态字段名、retry 上限、错误消息**逐字保持**；批次结束后
`result` 的语义变化仅限基类既定行为（原实现 `Success` 恒 true 的话，改为基类的
`failed==0 || done>0` 是可接受偏差，需在 commit 正文注明）。删除类内被基类取代的骨架代码。

#### 3. 迁移 `AiAnnotatorExecutor`

同样模式。其特殊点：队列表/主键/字段名来自 YAML 配置（`cfg.QueueTable`、`cfg.PrimaryKey`
等，`AiAnnotatorExecutor.cs:103-130`），`TRow` 用现有的 `QueueRow`（:589 附近）。
失败路径是 `status = CASE WHEN retry_count >= 3 THEN 'failed' ELSE 'queued' END`（:572-574）
—— 归入 `WriteOutcomeAsync`。cfg 的解析在 `FetchPendingAsync` 之前完成（可缓存到字段，
或在每个抽象方法内重新解析——选择与现有代码结构最贴近的做法）。

#### 4. 基类测试 `NetYamlForge.Tests/Services/BatchJob/QueueStepHandlerBaseTests.cs`

用测试内 `FakeQueueHandler : QueueStepHandlerBase<FakeRow>`（内存 List 当队列，
IDbConnection 可传 null 或内存 SQLite，基类本身不触库）断言：

- 空队列 → `Success=true`、`RowsAffected=0`、不调用 `ProcessRowAsync`。
- 3 行中第 2 行 `ProcessRowAsync` 抛异常 → 第 3 行仍被处理；`RowsAffected=2`；
  `ErrorMessage` 含 "1 row(s) failed"；第 2 行的 `WriteOutcomeAsync` 收到 `Failed` 结果。
- `WriteOutcomeAsync` 本身抛异常 → 不中断批次（覆盖基类 :95-102 的双重 catch）。
- 取消令牌在第 1 行后触发 → 第 2 行起不处理。
- `RowOutcome.Skip` → 不计入 done 也不计入 failed，`Success=true`（若全 Skip）。
- `MarkProcessingAsync` 抛异常 → 该行 Fail、后续行继续（新增虚方法的行为）。
- `job.BatchSize=0` 时 `FetchPendingAsync` 收到 `DefaultBatchSize`。

### 改动文件清单

| 文件 | 操作 |
|---|---|
| `NetYamlForge/Services/BatchJob/QueueStepHandlerBase.cs` | 增加 `MarkProcessingAsync` 虚方法及调用 |
| `NetYamlForge/Services/BatchJob/PhotoAnnotatorExecutor.cs` | 继承基类、删除骨架 |
| `NetYamlForge/Services/BatchJob/AiAnnotatorExecutor.cs` | 同上 |
| `NetYamlForge.Tests/Services/BatchJob/QueueStepHandlerBaseTests.cs` | 新建 |

### 验收标准

1. 上述测试全绿；`Tests/Services/BatchJob/` 现有测试无回归。
2. 两个试点 executor 不再直接实现 `IBatchStepHandler.ExecuteAsync`；
   `grep -rn "QueueStepHandlerBase" --include='*.cs'` 至少出现 3 处（基类 + 两试点）。
3. 迁移前后各跑一次 photo-vocab 的标注批次（或以集成/单测方式驱动一次
   `ExecuteAsync`），done/failed 计数与状态流转一致。
4. commit 正文列出与旧实现的行为差异（预期仅 `result.Success` 语义一条，若有更多需逐条列出）。

---

## G2 — ProjectDataMigrationRunner 测试 + README 数据迁移章节

### 背景

Runner（`Services/Project/ProjectDataMigrationRunner.cs`，283 行）已实现并挂载，
但没有任何测试；README 也没有数据迁移的用法说明（原设计 WP3 的验收项）。

### 设计

#### 1. `NetYamlForge.Tests/Services/ProjectDataMigrationRunnerTests.cs`

用临时目录构造 `<tempRoot>/database/migrations/`，连接串指向临时 SQLite 文件，
直接实例化 Runner（构造函数只要 `ILogger<ProjectDataMigrationRunner>`，
可用 `NullLogger`）。用例（对照原设计 WP3 测试计划）：

- `ApplyPendingAsync`：`001_a.sql`、`002_b.sql` 按序应用，`_nyf_data_migrations`
  记录 2 行且 `applied_at` 非空。
- 幂等：再次调用不重复执行（可在 SQL 中用 `INSERT` 计数表验证只插一次）。
- 失败中止：`002` 含非法 SQL → `001` 保持已应用、`002` 无记录、`003` 不执行；
  Runner 不抛异常（启动容错语义），返回的 summary 反映失败。
- `-- +up` / `-- +down` 分段解析正确；无标记文件整体为 up。
- `RollbackAsync`：有 down 的版本回滚后 `rolled_back_at` 置位，且数据变更被撤销；
  无 down 的版本回滚时报错或拒绝（以现实现行为为准，测试固化该行为）。
- checksum：已应用文件内容被篡改后再次 Apply → LogWarning 且不重放
  （断言记录的 checksum 未变、SQL 未重执行）。
- 文件名过滤：`01_x.sql`（位数不足）、`abc.sql` 被跳过。

注意先读 Runner 实测其公开签名（`ApplyPendingAsync(projectName, projectDir,
connectionString, ct)` 等），测试按实际签名写，不要按本文档草案猜。

#### 2. README / README-ja 增补

在两个 README 中各加一节「Data Migrations」：迁移目录约定
（`projects/<name>/database/migrations/NNN_description.sql`）、`-- +up`/`-- +down`
分段、三个 CLI 子命令（`--migrate-data`、`--migrate-data-status`、
`--migrate-data-rollback --version=<n>`，均需 `--project=<name>`）、
启动时自动应用的说明。各 20 行以内，风格对齐 README 现有段落。

### 验收标准

1. 新测试全绿。
2. README 两个语言版本均含该章节，命令与实际 CLI 参数一致（照抄 `Program.cs` 实现处的参数名核对）。

---

## G3 — YAML JSON Schema 导出（原 WP7.2，未动工）

### 背景

`Services/YamlSchemaValidator.cs` 有 4 个校验入口（ValidateProjectYaml /
ValidateUiPageYaml / ValidateEntityYaml / ValidateDashboardYaml），但无编辑器侧支持。
本项全新实现，按原设计 WP7.2 执行，此处只补充实现细节约定。

### 设计

1. **生成器**：新建 `NetYamlForge/Services/Cli/JsonSchemaExporter.cs`，
   反射遍历配置模型生成 draft-07 Schema。模型入口（先读文件确认根类型名再动手）：
   - `entities.schema.json` ← `Models/EntityMetadata.cs` 的实体定义根类型
   - `pages.schema.json` ← `Models/PageDefinition.cs`
   - `dashboard.schema.json` ← dashboard 配置模型（从 `DashboardConfigProvider` 反查）
   - `project.schema.json` ← `Services/Project/ProjectInfo.cs` 或 project.yaml 对应模型
   生成规则：属性名转 camelCase（与 YamlDotNet 的命名约定一致——先确认仓库用的
   NamingConvention，以其为准）；枚举 → `enum`；不可空值类型/`[Required]` → `required`；
   `List<T>` → `array`；`Dictionary<string,T>` → `additionalProperties`；递归类型做
   `$defs` + `$ref` 防止无限展开；未知属性策略用 `additionalProperties: true`
   （现有 YAML 里存在模型未覆盖的自由字段时不误报，宁松勿紧）。
   不引入新 NuGet 依赖（手写生成器，预计 ≤300 行）。
2. **CLI**：`--export-json-schema [--out=<dir>]`（默认 `docs/schemas/`），
   挂在 `Program.cs` 现有 CLI 参数区（模式照抄 `--migrate-data` 的实现）。
3. **仓库配置**：`.vscode/settings.json` 增加 `yaml.schemas` 映射（原设计 WP7.2 给出的
   四条 glob 原样使用；文件已存在时合并勿覆盖）。
4. **防漂移**：`.github/workflows/build-and-test.yml` 增加一步——export 后
   `git diff --exit-code docs/schemas`；`docs/schemas/*.json` 提交进仓库。
5. **回归测试**：`NetYamlForge.Tests/JsonSchemaExportTests.cs`——生成 schema 后，
   遍历 `NetYamlForge/projects/*/`（entities/pages/dashboard/project 四类 YAML 全部文件），
   逐一转 JSON 后用 schema 校验，断言 0 错误。YAML→JSON 转换用测试项目已有的
   YamlDotNet；schema 校验器可用测试项目已引用的包，若无则此一处允许给
   **Tests 项目**（非主项目）加轻量校验依赖（如 `JsonSchema.Net`）。

### 验收标准

1. `dotnet run --project NetYamlForge -- --export-json-schema` 生成 4 个 schema 文件。
2. 回归测试全绿（全部现存项目 YAML 通过校验）。
3. VS Code 打开任一 `projects/*/entities/*.yml` 有字段补全（人工抽查一次即可）。
4. CI 含防漂移步骤。

---

## G4 — 文档与存量配置修正

### 4.1 README 移除固定凭据

`README.md:74-76` 仍写着 `admin / Admin@123`（README-ja 如有同样内容一并处理）。
替换为与 `DefaultAdminSeeder` 现行为一致的说明：首次启动时按
`NYF_ADMIN_PASSWORD` 环境变量 → `Auth:DefaultAdminPassword` 配置 → 随机生成
（输出在启动日志）的优先级确定密码。

### 4.2 修复 kb-forge 的 jobs.yml

启动时固定报错（存量问题，非本批引入）：

```
ERR ジョブファイルの読み込みに失敗しました：projects/kb-forge/jobs/jobs.yml
(Line: 2, Col: 3): Expected 'MappingStart', got 'SequenceStart'
```

原因：该文件把 `jobs:` 写成了**序列**（`- name: kb_embedding` 列表项），而
`BatchJobLoader` 期望**映射**（对照正常的 `projects/inventory/jobs/jobs.yml`：
`jobs:` 下是 `job_name:` 键）。且其字段结构（`steps:`/`schedule: "cron串"`）与
`BatchJobDefinition` 的实际 schema（`type:`/`schedule: {cron:, timezone:}`/`settings:`）
不一致——像是按臆想格式写的，从未成功加载过。

修法：先读 `Services/BatchJob/BatchJobLoader.cs` 与 `BatchJobDefinition.cs` 确认
字段全集，再把 kb-forge 的 2 个 job（`kb_embedding` 等）改写为映射格式。
`ai_embedding_generator` step type 存在（`AiEmbeddingGeneratorExecutor`），其
`embeddingConfig` 参数如何在 `settings` 下表达，以该 executor 读取配置的代码为准。
改完后按「通用约束」的启动验证：日志中该 ERR 消失，且出现
`ジョブをスケジュールしました：kb-forge/...`。

### 验收标准

1. `grep -rn "Admin@123" README.md README-ja.md docs/` 无结果（docs/ 中历史设计文档除外，
   只改 README 两个文件）。
2. 启动日志 0 条 `ERR]`（kb-forge job 正常调度）。

---

## G5 — CRUD 测试小增量（原 WP4 收缩版）

### 背景

见开头勘误：原 WP4 矩阵的 B/C/D 组已有等价覆盖。真正缺的只有 A 组的一部分：
**仓储层直连往返**与两个未覆盖的 action 路径。

### 设计

新建 `NetYamlForge.Tests/DynamicCrudRepositoryRoundtripTests.cs`（复用仓库现有测试的
SQLite 构造模式——先看 `EntityCrudExecutionServiceTests.cs` 怎么建库建元数据，照抄其风格）：

- Insert → GetById → Update → GetById → Delete 全链路（真实 SQLite 文件或内存库）。
- BulkDelete：3 行删 2 行，返回受影响行数正确（`DynamicEntityController.BulkDelete`
  的仓储路径目前无测试）。
- 外键 `displayColumn` 投影（join 生效，`DynamicCrudRepository.cs:1034` 附近逻辑）。
- filterExpression 实际过滤行（不只是 SQL 字符串快照——插入 3 行断言只回 2 行）。

再新建 2 个集成用例（追加进 `YamlPipelineEndToEndTests.cs`，复用其 factory 与 blog fixture）：

- `ExportCsv_ThroughHttpLayer_ReturnsCsvWithSeededRows()`：断言 Content-Type 与首行列头。
- `BulkDelete_ThroughHttpLayer_DeletesSelectedRows()`。

**明确不做**：不重写已有覆盖（列表分页/排序、Hook 链、schema 迁移、字段权限）；
不新建 TestInfrastructure 抽象（现有测试各自建库的模式已成惯例，跟随即可）。

### 验收标准

1. 新增用例全绿，总测试数 ≥ 895（当前 883 + 本文档各项新增）。
2. `dotnet test` 总时长仍在 CI 的 10 分钟限制内。

---

## 附：完成后的整体回归清单

全部 G1–G5 完成后，做一次原设计「附录」级别的总回归：

1. `dotnet build --configuration Release` 0 error。
2. `dotnet test` 全绿。
3. 清 Hook 缓存 + 项目目录启动 150 秒：0 `HOOK_SECURITY_VIOLATION`、0 `Unsafe expression`、
   0 `ERR]`（G4.2 修复后 kb-forge 不再报错）。
4. `git ls-files | grep '\.db$'` 为空；应用运行后 `git status` 无 .db 噪音。
5. 每个 G 独立 commit，正文引用 `docs/IMPROVEMENT-DESIGN-2026-07-GAPS.md` 对应编号。
