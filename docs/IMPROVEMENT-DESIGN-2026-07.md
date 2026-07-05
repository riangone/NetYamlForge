# 框架改进设计规格（2026-07）

> 本文档为可直接交给实现 Agent 的规格：每个工作包（WP）包含 背景 / 目标 / 详细设计 /
> 改动文件清单 / 测试计划 / 验收标准。实现 Agent 应按「实施顺序」一节的顺序逐包实施，
> 每个 WP 完成后独立提交（`dotnet build` + `dotnet test` 必须全绿）。
> 文中所有 `文件:行号` 引用基于 2026-07-04 的 `nyf` 分支（HEAD = f65379d）；
> 若行号有漂移，以引用处附带的代码特征（类名/方法名）为准。

## 工作包总览与实施顺序

| WP | 标题 | 优先级 | 规模 | 依赖 |
|----|------|--------|------|------|
| WP1 | Hook 安全校验器语义化（消除误报与绕过） | P0 | 中 | 无 |
| WP2 | SQL 表达式白名单解析器（替换关键字黑名单） | P0 | 中 | 无 |
| WP3 | 数据迁移机制 + 运行时 .db 移出 git | P0 | 中 | 无 |
| WP4 | 核心 CRUD 管线测试补强 | P1 | 大 | 建议在 WP1/WP2 之后（新代码一并覆盖） |
| WP5 | DynamicEntityController 拆分（partial class） | P1 | 小 | 无（纯机械重构） |
| WP6 | BatchJob 队列执行器基类抽象 | P1 | 中 | 无 |
| WP7 | 杂项：默认密码安全化、YAML JSON Schema 导出 | P2 | 小 | 无 |

实施顺序：**WP1 → WP2 → WP3 → WP5 → WP6 → WP4 → WP7**。
WP4 放在 WP5/WP6 之后，是为了让测试直接覆盖重构后的结构，避免写两遍。

通用约束（适用于所有 WP）：

- 不改变任何现有 HTTP 路由、YAML 配置格式、CLI 参数的对外行为，除非该 WP 明确说明。
- 遵循仓库现有风格：框架核心代码注释用日文（现状如此），公开接口带 `<summary>`。
- 每个 WP 完成后必须验证：`dotnet build --configuration Release` 无 error/warning 回归；
  `dotnet test` 全绿；应用可启动且 25 个 `projects/` 租户项目全部加载无错误
  （启动日志中无 `HOOK_SECURITY_VIOLATION` / `HOOK_COMPILE_DIAGNOSTICS` / YAML 校验错误）。

---

## WP1 — Hook 安全校验器语义化

### 背景与现状

`HookSecurityValidator`（`NetYamlForge/Services/Project/ProjectHookLoader.cs:675-725`）是一个
`CSharpSyntaxWalker`，在 Roslyn 编译前对项目 Hook 源码做语法级黑名单检查，
由 `ProjectHookLoader` 在 `ProjectHookLoader.cs:452-465` 调用，命中即拒绝加载整个项目的 Hook。

两类缺陷（互为镜像）：

1. **误报**：`BannedMethods` 集合（`ProjectHookLoader.cs:687-692`）含裸标识符 `"Start"`、`"Process"`，
   而 `VisitIdentifierName` 按单个 token 匹配——任何名为 `start` 的局部变量、名为 `Process`
   的方法/属性都会被拦。commit f65379d（"重命名局部变量 start 规避误报"）即为此付出的代价。
   另外 `BannedNamespaces` 直接封禁 `using System.Diagnostics;`，连 `Stopwatch`、`Debug` 都无法使用。
2. **漏报（可绕过）**：集合中 `"Process.Start"`、`"Assembly.Load"` 这类带点的复合字符串
   永远不可能等于单个 identifier token，是死条目。攻击者只需写全限定名
   `System.Reflection.Assembly.Load(...)`（无需 `using`），逐 token 拆开为
   `System` / `Reflection` / `Assembly` / `Load`，全都不在黑名单，直接通过。

根因：**语法级 token 黑名单既不知道标识符指向什么符号，也不理解限定名**。

### 目标

- 用 Roslyn `SemanticModel` 做**符号级**检查：判断"这个调用/成员访问最终解析到哪个类型"，
  而不是"源码里出现了哪个单词"。
- 消除已知误报（局部变量 `start`、`using System.Diagnostics;` + `Stopwatch`）。
- 封死已知绕过路径（全限定名、别名 `using P = System.Diagnostics.Process;`、`typeof` 反射链）。

### 非目标

- 不追求密不透风的沙箱。Hook 源码在仓库内、由项目所有者提交，本校验的定位是
  **防误用护栏**（防止 Hook 里随手起进程/加载程序集），不是对抗恶意攻击者的安全边界。
  此定位需写入校验器的类注释。

### 详细设计

#### 1. 新建 `NetYamlForge/Services/Project/HookSecurityValidator.cs`

把校验器从 `ProjectHookLoader.cs` 中拆出为独立文件，并重写为语义检查：

```csharp
/// <summary>
/// フックソースコードの安全性検証（誤用防止ガードレール）。
/// SemanticModel でシンボルを解決し、禁止型のメンバー使用を検出します。
/// 悪意ある攻撃者向けのサンドボックスではありません（フックは信頼済みコードとして扱う）。
/// </summary>
public sealed class HookSecurityValidator
{
    // 禁止「型」リスト（名前空間ごと禁止ではなく型単位。Stopwatch/Debug 等は許可される）
    private static readonly HashSet<string> BannedTypes = new(StringComparer.Ordinal)
    {
        "System.Diagnostics.Process",
        "System.Diagnostics.ProcessStartInfo",
        "System.Reflection.Assembly",          // Load/LoadFrom/LoadFile など
        "System.Runtime.InteropServices.Marshal",
        "System.Runtime.InteropServices.DllImportAttribute",
        "System.Runtime.Loader.AssemblyLoadContext",
        "System.Activator",                    // Activator.CreateInstance(Type) 反射起動
        "System.AppDomain",
    };

    // 例外的に許可するメンバー（型は禁止だがこのメンバーだけは安全）
    private static readonly HashSet<string> AllowedMembers = new(StringComparer.Ordinal)
    {
        "System.Reflection.Assembly.GetExecutingAssembly",
        "System.Reflection.Assembly.GetName",
    };

    public IReadOnlyList<string> Validate(CSharpCompilation compilation)
    {
        var violations = new List<string>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var walker = new SemanticWalker(model, violations);
            walker.Visit(tree.GetRoot());
        }
        return violations;
    }

    private sealed class SemanticWalker : CSharpSyntaxWalker
    {
        // 実装ポイント（下記「検査対象ノード」参照）
    }
}
```

**检查对象节点与判定逻辑**（`SemanticWalker` 需覆写以下 Visit 方法）：

| 覆写方法 | 检查内容 |
|---|---|
| `VisitInvocationExpression` | `model.GetSymbolInfo(node).Symbol` 为 `IMethodSymbol` 时，取 `symbol.ContainingType.ToDisplayString()`；命中 `BannedTypes` 且 `"{型}.{メソッド名}"` 不在 `AllowedMembers` → 违规 |
| `VisitMemberAccessExpression` | 同上，覆盖属性/字段访问（如 `Process.GetCurrentProcess` 之外的静态成员） |
| `VisitObjectCreationExpression` / `VisitImplicitObjectCreationExpression` | 构造的类型 `model.GetTypeInfo(node).Type` 命中 `BannedTypes` → 违规 |
| `VisitAttribute` | `model.GetTypeInfo(node).Type` 为 `DllImportAttribute` / `UnmanagedCallersOnlyAttribute` → 违规 |
| `VisitIdentifierName`（保留但重写） | 仅当符号解析成功且指向 `BannedTypes` 中的类型本身（`ITypeSymbol`）时报告 `typeof(Process)` 之类的类型引用；符号为 null（未解析）时**不报告**——编译错误会在后续 Emit 阶段自然暴露 |

违规消息格式（供日志与用户排错）：
`{文件名}({行},{列}): 禁止 API の使用: {型完全名}.{メンバー名}`——
现状消息（`ProjectHookLoader.cs:463`）没有位置信息，这是修 f65379d 那类问题时的实际痛点。

#### 2. 修改 `ProjectHookLoader` 的调用顺序

现状是"先校验语法树 → 后创建 Compilation"（`ProjectHookLoader.cs:452-473`）。
语义模型需要 Compilation，因此调整为：

```
CSharpCompilation.Create(...)          // 先建（本来就要建，无额外成本）
  → validator.Validate(compilation)    // 语义校验
  → 违规则 LogError + return null      // 保持现有失败行为不变
  → compilation.Emit(...)              // 原有流程
```

删除 `ProjectHookLoader.cs:675-725` 的旧类；`BannedNamespaces` 的 `using` 指令检查整体废弃
（语义检查已覆盖所有使用点，封禁 `using` 反而误伤 `Stopwatch`）。

### 改动文件清单

| 文件 | 操作 |
|---|---|
| `NetYamlForge/Services/Project/HookSecurityValidator.cs` | 新建（语义校验器） |
| `NetYamlForge/Services/Project/ProjectHookLoader.cs` | 删除旧校验器类；调整校验时机到 Compilation 创建后 |
| `NetYamlForge.Tests/Hooks/HookSecurityValidatorTests.cs` | 新建 |

### 测试计划（`HookSecurityValidatorTests`，直接构造 `CSharpCompilation` 断言违规列表）

必须包含的用例：

- **误报回归**：源码含局部变量 `var start = DateTime.Now;`、方法名 `public void Start()`、
  `using System.Diagnostics;` + `Stopwatch.StartNew()`、属性名 `Process` → 0 违规。
  （可把 commit f65379d 改名前的那段代码原样作为用例。）
- **直接使用**：`new Process()`、`Process.Start("ls")` → 违规。
- **绕过路径**：全限定 `System.Diagnostics.Process.Start("ls")`（无 using）、
  别名 `using P = System.Diagnostics.Process; P.Start("ls")`、
  `System.Reflection.Assembly.LoadFile("/x.dll")`、`Activator.CreateInstance(t)`、
  `[DllImport("libc")]` → 各自违规。
- **允许例外**：`Assembly.GetExecutingAssembly().GetName()` → 0 违规。
- **违规消息含文件名与行号**。

### 验收标准

1. 上述测试全绿。
2. 全部 25 个 `projects/*/Hooks/*.cs` 在启动时编译加载成功，日志无 `HOOK_SECURITY_VIOLATION`。
3. 手工验证：在任一项目 Hook 中临时加入 `System.Diagnostics.Process.Start("ls");`
   （全限定、无 using），启动时该项目 Hook 被拒绝，日志含类型全名与源码位置；移除后恢复。

---

## WP2 — SQL 表达式白名单解析器

### 背景与现状

`SqlSafetyGuard.EnsureExpression`（`NetYamlForge/Services/SqlSafetyGuard.cs`）用
关键字黑名单（`"DELETE "`、`"UPDATE "`、`"UNION "` 等 + 分号/注释标记 + 一个宽松的字符白名单
正则）校验 YAML 中可配置的 SQL 片段。调用点：

- `DynamicCrudRepository.cs:1034-1106`（entities.yml 的 `columnExpression` / `filterExpression`）
- `DashboardController.cs:175-299`（dashboard.yml 的 `filter` / `groupExpression`）
- `Services/Hooks/DocumentNumberGeneratorHook.cs`
- `Services/AI/AiToolRegistryInitializer.cs`、`Services/AI/ToolValidation/ToolCallValidator.cs`

缺陷：

1. **误杀**：合法值如 `note = 'DELETE ME'`、`remark = 'update later'` 命中黑名单被拒。
2. **本质脆弱**：黑名单列不全。字符白名单允许引号/括号/字母，无法从结构上排除
   布尔恒真注入等畸形输入；安全性依赖"想到了所有坏词"。

### 目标

新建一个**白名单递归下降解析器**：只接受一个明确定义的布尔/标量表达式文法，
解析不通过即拒绝。安全性来自"文法里根本没有 `;`、子查询、`UNION`"，
而非"检测到了坏词"。同时消除对合法字符串字面量的误杀。

### 详细设计

#### 1. 新建 `NetYamlForge/Services/SqlExpressionParser.cs`

```csharp
/// <summary>
/// YAML 設定由来の SQL 式（WHERE 句・集計式）のホワイトリスト検証パーサー。
/// 下記文法に合致しない入力を例外で拒否します。文法にセミコロン・コメント・
/// サブクエリ・UNION が存在しないため、構造的に SQL インジェクションを排除します。
/// </summary>
public static class SqlExpressionParser
{
    /// <summary>式を検証。不正なら InvalidOperationException（context と失敗位置を含む）</summary>
    public static void Validate(string expression, string context);
}
```

**文法**（EBNF，解析器按此实现，不得私自放宽）：

```ebnf
expr        = orExpr ;
orExpr      = andExpr { "OR" andExpr } ;
andExpr     = notExpr { "AND" notExpr } ;
notExpr     = [ "NOT" ] predicate ;
predicate   = "(" expr ")"
            | operand ( compareOp operand
                      | "IS" [ "NOT" ] "NULL"
                      | [ "NOT" ] "LIKE" operand
                      | [ "NOT" ] "IN" "(" operand { "," operand } ")"
                      | "BETWEEN" operand "AND" operand )
            | operand ;                     (* 集計式用: groupExpression は述語でなくてよい *)
compareOp   = "=" | "!=" | "<>" | "<" | "<=" | ">" | ">=" ;
operand     = term { ("+"|"-"|"*"|"/"|"%"|"||") term } ;
term        = qualifiedId | literal | funcCall | "(" operand ")" ;
funcCall    = allowedFunc "(" [ operand { "," operand } ] ")" ;
qualifiedId = identifier [ "." identifier ] ;      (* 表别名.列名 *)
literal     = stringLit | numberLit | "NULL" | "CURRENT_TIMESTAMP" | "CURRENT_DATE" ;
stringLit   = "'" { anyCharExceptQuote | "''" } "'" ;   (* '' 为转义，内容任意 *)
```

- `identifier` 复用现有 `SqlSafetyGuard.IdentifierRegex`（含 Unicode 与 `[bracket quoted]` 形式）。
- `allowedFunc` 白名单（大小写不敏感，SQLite 为主，兼顾四方言公共集）：
  `LENGTH, LOWER, UPPER, TRIM, SUBSTR, REPLACE, ABS, ROUND, COALESCE, IFNULL, NULLIF,
  DATE, DATETIME, TIME, STRFTIME, JULIANDAY, MIN, MAX, SUM, COUNT, AVG, CAST`。
  `CAST` 语法特例：`CAST ( operand AS typeName )`，`typeName` 限
  `INTEGER|TEXT|REAL|NUMERIC|BLOB`。
- **关键性质**：文法中没有 `;`、`--`、`/*`、子查询（无 `SELECT`）、`UNION`。
  分词器遇到任何不在文法中的 token（含分号、注释起始）立即失败。
  字符串字面量内部内容不限——`'DELETE ME'` 是一个合法 token，从此不再误杀。
- 错误消息须包含：`context`、失败位置（字符偏移）、期望的 token 类别，便于 YAML 作者排错。

#### 2. 改造 `SqlSafetyGuard.EnsureExpression`

方法签名与调用点**全部不变**，内部实现替换为：

```csharp
public static void EnsureExpression(string? value, string context)
{
    if (string.IsNullOrWhiteSpace(value)) return;   // 现有语义保持
    SqlExpressionParser.Validate(value, context);
}
```

旧的黑名单常量、`ExpressionRegex`、`IsUnsafeToken` 中仅被 `EnsureExpression` 使用的部分删除；
`IsUnsafeToken` 若有其他调用方则保留。`EnsureIdentifier` 不动（正则白名单已足够严格）。

#### 3. 存量兼容性排查（实现 Agent 必做）

替换前，先枚举现网 YAML 中所有会流经 `EnsureExpression` 的表达式并逐一试解析：

```bash
grep -rn "expression:\|filter:\|groupExpression:" NetYamlForge/projects --include='*.yml' --include='*.yaml'
```

任何现存合法表达式解析失败 = 文法缺口，**扩文法**（在白名单函数/文法结构层面）而不是
加后门。把这批真实表达式固化为测试用例（见下）。

### 改动文件清单

| 文件 | 操作 |
|---|---|
| `NetYamlForge/Services/SqlExpressionParser.cs` | 新建（分词器 + 递归下降解析器，无第三方依赖） |
| `NetYamlForge/Services/SqlSafetyGuard.cs` | `EnsureExpression` 改为委托解析器；删除死代码 |
| `NetYamlForge.Tests/Services/SqlExpressionParserTests.cs` | 新建 |

### 测试计划

- **接受**：`status = 'active'`、`note = 'DELETE ME'`（黑名单误杀回归）、
  `price > 100 AND (stock <= 5 OR discontinued = 1)`、`name LIKE '%山田%'`（Unicode）、
  `created_at BETWEEN '2026-01-01' AND '2026-12-31'`、`STRFTIME('%Y-%m', created_at)`、
  `COALESCE(nickname, name)`、`o.total * 1.1`、`category IN ('a','b','c')`、
  `deleted_at IS NULL`、`CAST(qty AS INTEGER) > 0`、以及 §3 收集的全部存量表达式。
- **拒绝**：`1=1; DROP TABLE users`（分号）、`1=1 -- comment`（注释）、
  `id IN (SELECT id FROM users)`（子查询）、`1 UNION SELECT password FROM users`、
  `name = 'a' || (SELECT ...)`、`EXEC('...')`（非白名单函数）、空括号 `()`、
  未闭合字符串 `name = 'abc`。
- **错误消息**：断言包含 context 与失败位置。

### 验收标准

1. 上述测试全绿；现有 `NetYamlForge.Tests` 无回归。
2. 应用启动后逐一打开 25 个项目的 dashboard 与主要实体列表页，无
   `Unsafe expression` / `Invalid expression` 异常（启动日志 + 抽查 HTTP 200）。

---

## WP3 — 数据迁移机制 + 运行时 .db 移出 git

### 背景与现状

- **schema 迁移已有**：`DynamicEntitySchemaMigrationService`（Phase 4.3，
  `_nyf_migrations` 表，见 `docs/PHASE4.3-MIGRATION-DESIGN.md`）处理"YAML 定义 vs 物理表结构"
  的 DDL 差分。但**数据迁移**（修历史数据、回填新列、批量清洗）没有框架通道——
  症状：`projects/blog/database/fix_history_posts.py`（未跟踪的一次性 Python 脚本）。
- **运行时数据库进了 git**：`git ls-files` 显示 2 个被跟踪的 SQLite 文件：
  `NetYamlForge/projects/diary-companion/database/diary-companion.db`、
  `NetYamlForge/projects/photo-vocab/database/photo-vocab.db`。
  每次运行应用都会污染工作区（当前 git status 即如此），且二进制文件持续膨胀仓库。
- **种子机制已有**：`ProjectSpecificInitializer`（`Data/ProjectSpecificInitializer.cs:115-162`）
  在实体表不存在时按 `database/init.sql` → `database/init_seed.sql` 顺序初始化。

### 目标

1. 新增按项目的**版本化数据迁移**：`projects/<name>/database/migrations/` 下的编号 SQL
   在启动时按序执行一次，执行记录落库。
2. 两个被跟踪的 .db 文件移出 git，用 `init.sql` + `init_seed.sql` 承载可再现的初始状态。
3. `fix_history_posts.py` 的逻辑转写为 blog 项目的第一个数据迁移（或确认已执行完毕后删除该脚本，
   由实现 Agent 阅读脚本内容后判断——若其变更已固化在数据里且不需重放，删除即可）。

### 详细设计

#### 1. 迁移文件约定

```
projects/<name>/database/migrations/
  001_backfill_post_summary.sql
  002_normalize_tag_case.sql
```

- 文件名 `^(\d{3,})_[A-Za-z0-9_\-]+\.sql$`，数字为版本号，按数值升序执行。
- 文件体为纯 SQL，可含多条语句；支持可选的 down 段（与 Phase 4.3 的 up/down 概念对齐）：

```sql
-- +up
UPDATE posts SET summary = substr(content, 1, 200) WHERE summary IS NULL;
-- +down
UPDATE posts SET summary = NULL;
```

  无 `-- +up` 标记时整个文件视为 up、down 为空（不可回滚，仅记录）。

#### 2. 新建 `NetYamlForge/Services/Project/ProjectDataMigrationRunner.cs`

```csharp
/// <summary>
/// projects/<name>/database/migrations/ 配下の番号付き SQL を起動時に順次適用します。
/// 適用記録は各プロジェクト DB の _nyf_data_migrations に保存されます。
/// </summary>
public sealed class ProjectDataMigrationRunner
{
    public Task<DataMigrationSummary> ApplyPendingAsync(string projectName, CancellationToken ct);
    public Task<IReadOnlyList<DataMigrationRecord>> GetStatusAsync(string projectName, CancellationToken ct);
    public Task RollbackAsync(string projectName, long version, CancellationToken ct);  // down 有り時のみ
}
```

记录表（每项目库，与 `_nyf_migrations` 并列、不复用——schema 迁移是运行时生成的 SQL，
数据迁移是文件驱动，生命周期不同）：

```sql
CREATE TABLE IF NOT EXISTS _nyf_data_migrations (
    version     INTEGER PRIMARY KEY,
    name        TEXT NOT NULL,
    checksum    TEXT NOT NULL,          -- SHA256(文件内容)，防止已应用文件被篡改后静默漂移
    up_sql      TEXT NOT NULL,
    down_sql    TEXT,
    applied_at  TEXT NOT NULL,
    rolled_back_at TEXT
);
```

执行规则：

- 单个迁移 = 单事务；失败即回滚该事务并**中止后续版本**，启动日志 LogError，
  但不阻止应用启动（与现有 `ProjectSpecificInitializer` 的容错基调一致）。
- 已应用版本的文件 checksum 与记录不符 → LogWarning（提示文件被改过），不重放。
- 挂载点：在 `ProjectSpecificInitializer` 完成 init/seed 之后调用（同一启动初始化链路中，
  实现 Agent 在 `Program.cs` / 项目初始化处找到 `ProjectSpecificInitializer` 的调用点接在其后）。

#### 3. CLI 子命令（沿用现有 `--init-project` 风格的参数解析处）

```
--migrate-data           --project=<name>            # 手动应用待执行迁移
--migrate-data-status    --project=<name>            # 列出各版本状态
--migrate-data-rollback  --project=<name> --version=<n>
```

#### 4. .db 移出 git

```bash
git rm --cached NetYamlForge/projects/diary-companion/database/diary-companion.db \
               NetYamlForge/projects/photo-vocab/database/photo-vocab.db
```

`.gitignore` 追加：

```gitignore
NetYamlForge/projects/*/database/*.db
NetYamlForge/projects/*/database/*.db-wal
NetYamlForge/projects/*/database/*.db-shm
```

移除前，为这两个项目生成可再现的初始状态（若其 `database/` 下尚无 init.sql / init_seed.sql
或内容过期）：用 `sqlite3 <db> .schema` 生成/校对 `init.sql`，
用 `sqlite3 <db> .dump` 提取必要的**种子数据**（演示内容、配置行；不含用户上传的运行时数据）
写入 `init_seed.sql`。完成后删除本地 .db 并启动应用，确认两项目从零初始化可用。

### 改动文件清单

| 文件 | 操作 |
|---|---|
| `NetYamlForge/Services/Project/ProjectDataMigrationRunner.cs` | 新建 |
| `NetYamlForge/Program.cs`（或项目初始化链路所在处） | 挂载 Runner + CLI 子命令 |
| `.gitignore` | 追加 .db 规则 |
| `NetYamlForge/projects/{diary-companion,photo-vocab}/database/init*.sql` | 生成/更新种子 |
| `NetYamlForge/projects/blog/database/fix_history_posts.py` | 转写为迁移或删除（读脚本后定） |
| `NetYamlForge.Tests/Services/ProjectDataMigrationRunnerTests.cs` | 新建 |
| `README.md` / `README-ja.md` | 补一节"数据迁移"用法 |

### 测试计划

- 临时目录 + 内存/临时 SQLite：按序应用 001、002；重启（再次调用）不重复应用；
  002 故意报错时 001 保持已应用、002 无记录、后续 003 不执行；
  down 回滚后 `rolled_back_at` 置位且可重新应用；checksum 变更告警；
  文件名不合法（`01_x.sql`、`001 x.sql`）被跳过并 LogWarning。

### 验收标准

1. 测试全绿。
2. `git status` 在应用运行后不再出现 .db 修改；`git ls-files | grep '\.db$'` 为空。
3. 删除本地 `diary-companion.db` / `photo-vocab.db` 后启动，两项目页面可正常打开（种子生效）。
4. `--migrate-data-status --project=blog` 能列出迁移状态。

---

## WP4 — 核心 CRUD 管线测试补强

### 背景与现状

主项目约 5.6 万行 C#，`NetYamlForge.Tests` 仅 65 个测试文件；`Tests/Controllers` 只覆盖
`LocalizationController` 与 `TenantAccountController`。框架心脏完全裸奔：
`DynamicEntityController`（1626 行 / 27 action）、`DynamicCrudRepository`（1347 行）、
`DynamicEntitySchemaMigrationService`（770 行）。这些组件出 bug 会同时影响全部 25 个租户项目。

### 目标

为"YAML 进 → SQL/HTTP 出"的核心链路建立回归防线。不追求覆盖率数字，
追求**每个公开行为至少一个特征测试**（characterization test），以保护后续重构（含 WP5）。

### 详细设计

统一测试基建（如已存在类似设施则复用，勿重复造）：

- `NetYamlForge.Tests/TestInfrastructure/SqliteTestDatabase.cs`：
  每测试一个临时文件 SQLite（非 `:memory:`，因框架多处按连接串开新连接），Dispose 删除。
- `NetYamlForge.Tests/TestInfrastructure/EntityMetadataBuilder.cs`：
  流式构造 `EntityDefinition`（`Models/EntityMetadata.cs`），避免每个测试手写大对象。
- 集成测试用 `WebApplicationFactory<Program>`（`Tests/Integration/` 已有先例则沿用其模式），
  指向一个专用测试项目目录（fixture YAML 放 `NetYamlForge.Tests/Fixtures/projects/test-crud/`）。

**测试矩阵**（按优先级实现，A 组为必做）：

A. `DynamicCrudRepositoryTests`（单元，直连 SQLite）
   - Create/GetById/Update/Delete/BulkDelete 往返；
   - 列表查询：分页、排序（合法列 + 非法列拒绝）、filterExpression 生效、join 列投影；
   - `EnsureIdentifier`/`EnsureExpression` 违规元数据在仓储层被拒（与 WP2 联动）；
   - 外键显示列解析（`displayColumn`）。

B. `DynamicEntitySchemaMigrationServiceTests`（单元，直连 SQLite）
   - BuildPlan：加列 / 删列 / 类型变更 / 可空性变更各生成正确 Up/Down SQL；
   - Apply 后 `_nyf_migrations` 有记录，Rollback 恢复原结构；
   - dry-run 不改库。

C. `DynamicEntityControllerIntegrationTests`（`WebApplicationFactory`）
   - `Index` 200 且包含种子行；`Create` POST → 302/成功响应且行落库；
     `Edit` POST 更新生效；`Delete` POST 行消失；
   - 未认证访问受保护 action → 跳转登录（现有 auth 行为为准）；
   - 非法 entity 名（`../etc`）→ 4xx 而非 500。

D. Hook 管线（`EntityHookRegistry` + `CommonHooks` 冒烟）
   - beforeCreate 返回 `Abort()` 时 DB 无写入；afterCreate 抛异常时事务回滚。

### 验收标准

1. A、B、C、D 全部落地且绿；`dotnet test` 总时长增幅 < 60s（CI `timeout-minutes: 10` 内）。
2. 测试不依赖仓库内真实项目数据（只用 Fixtures），可并行执行。

---

## WP5 — DynamicEntityController 拆分（partial class）

### 背景与现状

`Controllers/DynamicEntityController.cs`：1626 行、27 个 action、22 个注入依赖。
职责横跨列表/表单/变更/导出/schema 迁移/自定义 action/选择器。

### 目标

**纯机械拆分**为 partial class 多文件，路由、行为、依赖零变化。
（拆成多个 Controller 会改变路由中的 controller 名，风险不成比例，明确不做。）

### 详细设计

`public partial class DynamicEntityController : BaseProjectController`，按现有 action 分组拆为：

| 文件 | 迁入成员（以现文件行号定位） |
|---|---|
| `DynamicEntityController.cs`（保留） | 字段、构造函数、私有共享 helper |
| `DynamicEntityController.List.cs` | `List`(94)、`Index`(99)、`ListPartial`(160)、`PickerList`(1377) |
| `DynamicEntityController.Form.cs` | `CreateForm`(224)、`CreatePage`(240)、`EditForm`(294)、`DetailPage`(314)、`EditPage`(490) |
| `DynamicEntityController.Mutation.cs` | `Create`(258)、`Edit`(512)、`Delete`(557)、`BulkDelete`(613) |
| `DynamicEntityController.Schema.cs` | `Definition`(640)、`AllDefinitions`(649)、`ConfigDiagnostics`(657)、`SchemaMigration`(678)、`SchemaMigrationApply`(708)、`SchemaMigrationRollback`(729) |
| `DynamicEntityController.Export.cs` | `ExportCsv`(747)、`ExportCustom`(805)、`DocumentPdf`(403) |
| `DynamicEntityController.Actions.cs` | `HeaderActionForm`(1067)、`InvokeHeaderAction`(1086)、`InvokeBulkAction`(1172)、`ActionForm`(1236)、`InvokeAction`(1256) |

规则：**只移动，不改写**——不重命名、不调签名、不"顺手优化"。
私有 helper 移到与其唯一调用组同文件；被多组调用的留在主文件。
`PageController.cs`（1154 行）本 WP 不动，待此模式验证后另行处理。

### 验收标准

1. `git diff` 仅表现为代码搬移（可用 `git diff --color-moved=dimmed-zebra` 复核）。
2. `dotnet build` 零新警告；WP4 的 C 组集成测试（若已实现）全绿；
   否则手工抽查 列表/新建/编辑/删除/导出 CSV 五条路由各返回 200/302。

---

## WP6 — BatchJob 队列执行器基类抽象

### 背景与现状

`Services/BatchJob/` 下 8 个 AI/队列类 executor（各 500-800 行）实现同一骨架：
取待处理行 → 标记 processing → 调 AI/CLI → 写回结果或失败 → 计数。
以 `PhotoAnnotatorExecutor.ExecuteAsync`（`PhotoAnnotatorExecutor.cs:54` 起）为典型样本。
公共接口为 `IBatchStepHandler`（`Services/BatchJob/IBatchStepHandler.cs`），
由 DI 收集、`BatchJobExecutor` 按 `job.Type` 分发。

重复的部分：批量拉取（LIMIT batchSize）、状态机（queued→processing→done/failed）、
retry_count 维护、逐行 try/catch、取消检查、成功/失败计数与 `BatchJobResult` 填充。

### 目标

抽出模板方法基类，让每个 executor 只写"取哪些行、一行怎么处理、结果怎么写回"。
**本 WP 只迁移 2 个试点**（`PhotoAnnotatorExecutor` + `AiAnnotatorExecutor`），
验证抽象后其余 executor 留待后续（避免一次性大爆炸重构）。

### 详细设计

新建 `NetYamlForge/Services/BatchJob/QueueStepHandlerBase.cs`：

```csharp
/// <summary>
/// 「待機行を取得 → 1 行ずつ処理 → 成否を書き戻す」型バッチステップの共通基底。
/// 状態遷移・リトライ計数・キャンセル・集計は基底が担い、派生は 3 つの抽象を実装します。
/// </summary>
public abstract class QueueStepHandlerBase<TRow> : IBatchStepHandler
{
    public abstract string StepType { get; }
    protected virtual int DefaultBatchSize => 5;

    /// <summary>処理対象行を取得（LIMIT batchSize は派生側 SQL に含める）</summary>
    protected abstract Task<IReadOnlyList<TRow>> FetchPendingAsync(
        BatchJobDefinition job, string? projectName, IDbConnection db, IDbTransaction tx, int batchSize, CancellationToken ct);

    /// <summary>行を processing 状態にマーク</summary>
    protected abstract Task MarkProcessingAsync(TRow row, IDbConnection db, IDbTransaction tx);

    /// <summary>1 行処理。RowOutcome.Ok / Fail(reason) / Skip(reason) を返す</summary>
    protected abstract Task<RowOutcome> ProcessRowAsync(
        TRow row, BatchJobDefinition job, string? projectName, IDbConnection db, IDbTransaction tx, CancellationToken ct);

    /// <summary>成功/失敗の書き戻し（retry_count 加算・エラーメッセージ保存など）</summary>
    protected abstract Task WriteOutcomeAsync(TRow row, RowOutcome outcome, IDbConnection db, IDbTransaction tx);

    public async Task ExecuteAsync(BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx, BatchJobResult result, CancellationToken ct)
    {
        // 基底実装: batchSize 決定 → Fetch → 空なら Success/0 → 各行
        //   ct チェック → MarkProcessing → try { ProcessRow } catch → Fail(ex)
        //   → WriteOutcome → 集計 → result へ done/failed/skipped を反映
    }
}

public sealed record RowOutcome(RowStatus Status, string? Reason = null, object? Payload = null)
{
    public static RowOutcome Ok(object? payload = null) => new(RowStatus.Ok, null, payload);
    public static RowOutcome Fail(string reason) => new(RowStatus.Failed, reason);
    public static RowOutcome Skip(string reason) => new(RowStatus.Skipped, reason);
}
```

要点：

- `ExecuteAsync` 中逐行 `catch (Exception ex)` 转 `RowOutcome.Fail(ex.Message)` 并
  LogError（保留现有"单行失败不中断批次"语义，对照 `PhotoAnnotatorExecutor.cs:90-210` 现状）。
- `BatchJobResult` 填充逻辑（Success / RowsAffected / 消息文案）与现有各 executor 保持一致，
  迁移时逐字段对照。
- 试点迁移时行为必须逐一对齐：拉取 SQL、状态字段名、retry 上限、失败消息格式全部不变
  （两个试点各自的表结构不同，这正是抽象只管流程、不管 SQL 的原因）。

### 改动文件清单

| 文件 | 操作 |
|---|---|
| `NetYamlForge/Services/BatchJob/QueueStepHandlerBase.cs` | 新建 |
| `NetYamlForge/Services/BatchJob/PhotoAnnotatorExecutor.cs` | 改为继承基类，删除重复骨架 |
| `NetYamlForge/Services/BatchJob/AiAnnotatorExecutor.cs` | 同上 |
| `NetYamlForge.Tests/Services/BatchJob/QueueStepHandlerBaseTests.cs` | 新建 |

### 测试计划

- 用一个测试专用 `FakeQueueHandler : QueueStepHandlerBase<FakeRow>` 断言基类流程：
  空队列 → Success + 0；3 行中第 2 行抛异常 → done=2 failed=1 且第 3 行仍被处理；
  取消令牌在第 2 行前触发 → 只处理 1 行；Skip 计数正确。
- 现有 `Tests/Services/BatchJob/` 下针对两个试点的测试（如有）保持绿。

### 验收标准

1. 两个试点 executor 行数显著下降（骨架代码归零），业务 SQL 与 AI 调用逻辑逐行可对照。
2. photo-vocab 与相应项目的批处理任务在运行时正常出结果（手工触发一次批次，观察日志计数）。

---

## WP7 — 杂项改进

### 7.1 默认管理员密码安全化

现状：`Data/Seeders/DefaultAdminSeeder.cs` 与 `Data/Schemas/SystemDatabaseInitializer.cs`
播种 `admin / Admin@123`，且 README 公开该凭据。

设计：

- 播种时按优先级取密码：环境变量 `NYF_ADMIN_PASSWORD` →
  配置 `Auth:DefaultAdminPassword` → **随机生成 16 位强密码**并以
  `LogWarning`（醒目框线）输出一次到启动日志。
- 仅在 admin 用户**不存在**时播种（确认现状即如此，若已存在则不动密码）。
- `AppUser` 增加 `MustChangePassword`（bool，默认 false；随机生成路径置 true），
  登录成功后若为 true 强制跳转改密页（`AccountController` 已有改密能力则复用；
  没有则新增最小页面）。此项若 UI 工作量超预期，可降级为仅日志警告，但需在 PR 里说明。
- README/README-ja 中删除固定凭据表述，改为说明上述机制。

验收：全新环境（删除 system.db）启动，日志出现随机密码且可登录；
设置 `NYF_ADMIN_PASSWORD` 后以该值登录成功；已有库升级启动不改变现有 admin 密码。

### 7.2 YAML JSON Schema 导出（编辑器补全）

现状：`Services/YamlSchemaValidator.cs` 对 4 类 YAML（project / entity / uiPage / dashboard）
做运行时校验，但编辑器侧无补全/即时校验。

设计：

- 新增 CLI：`--export-json-schema --out=<dir>`（默认 `docs/schemas/`），
  生成 `project.schema.json`、`entities.schema.json`、`pages.schema.json`、
  `dashboard.schema.json`。
- 生成方式：基于对应的 C# 配置模型（`Models/EntityMetadata.cs`、`Models/PageDefinition.cs` 等）
  反射生成 draft-07 JSON Schema。不引入重量级依赖；可引入 `JsonSchema.Net.Generation` 或
  手写一个 ~200 行的反射生成器（属性名 camelCase、枚举转 enum、`[Required]`/非空引用类型转
  required、集合/字典递归）。**枚举与必填约束以 `YamlSchemaValidator` 现有规则为准**，
  两者冲突时修 Schema 生成器。
- 仓库根新增 `.vscode/settings.json`（若无）：

```json
{
  "yaml.schemas": {
    "docs/schemas/entities.schema.json": "NetYamlForge/projects/*/entities/**/*.yml",
    "docs/schemas/pages.schema.json": "NetYamlForge/projects/*/pages/**/*.yml",
    "docs/schemas/dashboard.schema.json": "NetYamlForge/projects/*/dashboard.yml",
    "docs/schemas/project.schema.json": "NetYamlForge/projects/*/project.yaml"
  }
}
```

- 生成的 schema 提交进仓库；CI 增加一步：导出后 `git diff --exit-code docs/schemas`
  防止模型与 schema 漂移。

验收：VS Code 打开任一 `entities/*.yml` 有补全与未知字段波浪线；
现存 25 个项目的全部 YAML 用生成的 schema 校验通过（可写一个一次性测试遍历校验）。

### 7.3 语言统一（仅政策，不动代码）

现状：注释日文、commit 中英混合、README 英/日。若维持个人项目定位可不动；
若走开源推广，建议：代码注释与错误消息统一日文或英文其一，新代码执行、旧代码不回改。
本项**不产生代码改动**，实现 Agent 在 `CLAUDE.md`（若存在）或 `docs/` 补一行贡献约定即可。

---

## 附：每个 WP 的提交与回归清单

每个 WP 完成时：

1. `dotnet build --configuration Release`：0 error，警告数不高于基线。
2. `dotnet test`：全绿。
3. 应用启动（`dotnet run --project NetYamlForge`）：25 个项目加载无 ERROR 级日志。
4. 独立 commit，消息格式沿用仓库惯例（`feat(scope): ...` / `fix(scope): ...`），
   正文引用本文档对应 WP 编号（如 `Implements docs/IMPROVEMENT-DESIGN-2026-07.md WP1`）。
5. 涉及行为变化的 WP（WP1/WP2/WP3/WP7.1）在 commit 正文列出行为差异。
