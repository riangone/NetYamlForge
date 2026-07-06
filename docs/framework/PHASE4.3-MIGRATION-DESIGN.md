# Phase 4.3 — YAML Schema 迁移系统设计规格

> 前置条件：Phase 3.1（SQLite WAL/WriteGate）、Phase 4.1/4.2（REST API + MCP）已完成。
> 本文档为可直接交给实现 Agent 的规格：目标 / 架构 / 改动文件清单 / 验收标准。
> 范围：本阶段仅支持 **SQLite**（当前唯一的开发/运行时数据库）。设计上预留方言抽象点，
> 但 PostgreSQL 等其他方言的具体 DDL 实现留给 Phase 4.4。

## 背景与现状

`ProjectSpecificInitializer.AutoMigrateMissingColumnsAsync`（`NetYamlForge/Data/ProjectSpecificInitializer.cs`）
已经在每次启动时做了"最小化自动迁移"：对比 YAML `EntityDefinition.Columns` 与物理表的列，
**只会**自动 `CREATE TABLE`（表不存在时）和 `ALTER TABLE ADD COLUMN`（列缺失时）。

它的局限：
- 不处理**删除列**、**列类型变更**、**可空性变更**。
- 没有版本记录，无法知道"已经应用过哪些迁移"，也无法**回滚**。
- 没有 dry-run，无法在应用前看到将要执行的 SQL。
- 启动时静默执行，管理员无法审阅。

Phase 4.3 的目标：在不破坏上述启动期自动建表/补列行为的前提下，新增一套**显式、可审阅、
可回滚、有版本记录**的迁移管线，覆盖删列/改类型/改可空性等启动期自动迁移不处理的场景。

## 总体架构

```
管理员（Admin UI）
   │  GET  {project}/DynamicEntity/SchemaMigration?entity=xxx   (查看 diff + dry-run SQL)
   │  POST {project}/DynamicEntity/SchemaMigration/Apply         (应用迁移)
   │  POST {project}/DynamicEntity/SchemaMigration/Rollback      (回滚指定迁移)
   ▼
DynamicEntityController (新增 action)
   │
   ▼
DynamicEntitySchemaMigrationService  ← 新增核心服务
   │  1. BuildPlanAsync: 读取物理表结构 (PRAGMA table_info) vs EntityDefinition.Columns → MigrationPlan
   │  2. GenerateSql: MigrationPlan → (UpSql[], DownSql[])
   │  3. ApplyAsync: 事务内执行 UpSql，写入 _nyf_migrations
   │  4. RollbackAsync: 读取 _nyf_migrations.DownSql，事务内执行，标记回滚
   ▼
project.db (各租户库) — 新增 _nyf_migrations 表
```

## 改动文件清单

### 1. 新建 `NetYamlForge/Models/SchemaMigration.cs`

```csharp
namespace NetYamlForge.Models;

public sealed record ColumnSchemaInfo(string Name, string SqlType, bool NotNull, bool IsPrimaryKey);

public enum MigrationOpType
{
    AddColumn,
    DropColumn,
    AlterColumnType,      // 类型変更（SQLite はテーブル再構築が必要）
    AlterNullability       // NULL許可⇄NOT NULL（SQLite はテーブル再構築が必要）
}

public sealed record MigrationOperation(
    MigrationOpType OpType,
    string ColumnName,
    string? OldSqlType,
    string? NewSqlType,
    bool? NewNotNull);

public sealed record MigrationPlan(
    string EntityName,
    string TableName,
    IReadOnlyList<MigrationOperation> Operations)
{
    public bool RequiresTableRebuild =>
        Operations.Any(o => o.OpType is MigrationOpType.AlterColumnType or MigrationOpType.AlterNullability
                             or MigrationOpType.DropColumn);
}

public sealed record MigrationRecord(
    string Id,
    string ProjectName,
    string EntityName,
    string TableName,
    string Description,
    string UpSql,
    string DownSql,
    DateTime AppliedAt,
    DateTime? RolledBackAt);
```

注：SQLite 3.35+ 原生支持 `ALTER TABLE ... DROP COLUMN`，但**为了能生成可靠的 DownSql（恢复被删列及其数据）
仍统一走表重建路径**——重建时旧表整体保留为临时表，回滚即可从临时表把数据迁回。详见第 4 节。

### 2. 新建 `NetYamlForge/Services/DynamicEntity/SqlTypeMapper.cs`

把 `ProjectSpecificInitializer.MapYamlTypeToSqlType`（私有 static 方法）提取为共享静态类，
原方法改为调用此类，避免重复：

```csharp
namespace NetYamlForge.Services.DynamicEntity;

public static class SqlTypeMapper
{
    public static string MapYamlTypeToSqlType(string yamlType, string dbType) { /* 原逻辑搬移 */ }
}
```

`ProjectSpecificInitializer.AutoCreateTableAsync` / `AutoMigrateMissingColumnsAsync` 中对
`MapYamlTypeToSqlType` 的调用改为 `SqlTypeMapper.MapYamlTypeToSqlType`。

### 3. 新建 `NetYamlForge/Services/DynamicEntity/DynamicEntitySchemaMigrationService.cs`

```csharp
namespace NetYamlForge.Services.DynamicEntity;

public sealed class DynamicEntitySchemaMigrationService
{
    // 1. 物理表の列情報を読み取る（SQLite: PRAGMA table_info(tableName)）
    public Task<IReadOnlyList<ColumnSchemaInfo>> GetPhysicalColumnsAsync(IDbConnection conn, string tableName);

    // 2. YAML EntityDefinition と物理列を比較し、MigrationPlan を構築
    //    - YAML にあって物理にない列 → AddColumn
    //    - 物理にあって YAML にない列 → DropColumn
    //    - 両方にあるが型/NOT NULL が不一致 → AlterColumnType / AlterNullability
    //    - Identity/PK 列は対象外（変更検知しても無視してログに警告）
    public MigrationPlan BuildPlan(string entityName, EntityDefinition entity,
        IReadOnlyList<ColumnSchemaInfo> physicalColumns, string dbType);

    // 3. MigrationPlan → 実行可能 SQL（Up/Down）。
    //    RequiresTableRebuild == false の場合は素直に ALTER TABLE ADD COLUMN の列。
    //    true の場合は SQLite 12-step rebuild:
    //      a) ALTER TABLE "Table" RENAME TO "Table__old"
    //      b) YAML 定義どおりに新 "Table" を CREATE（AutoCreateTableAsync のロジックを再利用）
    //      c) INSERT INTO "Table" (...) SELECT ... FROM "Table__old"  -- 列名共通部分のみ、型は CAST
    //      d) DROP TABLE "Table__old"
    //    DownSql は up の逆操作（rebuild の場合は "Table__old" を退避したまま残し、
    //      rollback 時に現行 "Table" を破棄して "Table__old" を "Table" にリネームすることで
    //      確実にデータを復元する。つまり Apply 時は (d) を遅延し、Rollback 完了 or
    //      次回マイグレーション開始時にのみ古いバックアップテーブルを破棄する）
    public (IReadOnlyList<string> UpSql, IReadOnlyList<string> DownSql, string BackupTableName)
        GenerateSql(MigrationPlan plan, EntityDefinition entity, string dbType);

    // 4. _nyf_migrations テーブルを必要に応じて作成し、Up を実行してレコードを残す
    //    dryRun=true の場合は SQL を返すだけで何も実行しない
    public Task<MigrationApplyResult> ApplyAsync(IDbConnection conn, string projectName,
        MigrationPlan plan, EntityDefinition entity, string dbType, bool dryRun);

    // 5. _nyf_migrations から DownSql を取得して実行し、rolled_back_at を記録
    public Task RollbackAsync(IDbConnection conn, string migrationId);

    // 6. 履歴一覧（Admin UI 表示用）
    public Task<IReadOnlyList<MigrationRecord>> GetHistoryAsync(IDbConnection conn, string? projectName = null);
}

public sealed record MigrationApplyResult(bool Applied, string MigrationId, IReadOnlyList<string> ExecutedSql);
```

`_nyf_migrations` テーブル（各プロジェクト DB 内、初回 Apply 時に `CREATE TABLE IF NOT EXISTS` で作成）:

```sql
CREATE TABLE IF NOT EXISTS _nyf_migrations (
    id TEXT PRIMARY KEY,
    entity_name TEXT NOT NULL,
    table_name TEXT NOT NULL,
    description TEXT NOT NULL,
    up_sql TEXT NOT NULL,
    down_sql TEXT NOT NULL,
    applied_at TEXT NOT NULL,
    rolled_back_at TEXT
)
```

### 4. テーブル再構築 (SQLite) の詳細

`AlterColumnType` / `AlterNullability` / `DropColumn` を含むプランは以下の手順で `UpSql` を構成する
（すべて単一トランザクション内）:

1. `ALTER TABLE "{Table}" RENAME TO "{Table}__bak_{timestamp}"`
2. 新しい `"{Table}"` を YAML 定義どおりに `CREATE TABLE`（`ProjectSpecificInitializer.AutoCreateTableAsync`
   と同じ列定義生成ロジックを `DynamicEntitySchemaMigrationService` からも呼べるように、
   テーブル DDL 生成部分を `SqlTypeMapper` 同様に共有 helper（例: `TableDdlBuilder`）へ切り出す）
3. `INSERT INTO "{Table}" (col1, col2, ...) SELECT col1, col2, ... FROM "{Table}__bak_{timestamp}"`
   — 列リストは「新旧両方に存在する列」のみ。型が変わった列は `CAST(col AS NEWTYPE)` を使う。
   削除された列は SELECT から単純に除外（バックアップテーブルには残るのでロールバック可能）。

`DownSql`:
1. `DROP TABLE "{Table}"`
2. `ALTER TABLE "{Table}__bak_{timestamp}" RENAME TO "{Table}"`

バックアップテーブル `"{Table}__bak_{timestamp}"` は **ロールバック可能な間は残す**。
次回同じエンティティに対して新たな破壊的マイグレーションを Apply する際、
古いバックアップテーブル（`rolled_back_at IS NOT NULL` または十分古いもの）は
クリーンアップ対象としてログに警告を出す（自動 DROP はしない — 安全側）。

### 5. Admin UI — `DynamicEntityController` 拡張

`NetYamlForge/Controllers/DynamicEntityController.cs` の既存 `ConfigDiagnostics` action
（行 647 付近）と同じ Admin 限定ルートパターンに追加:

```csharp
[HttpGet]
public async Task<IActionResult> SchemaMigration(string entity)
{
    // EntityMetadataProvider から EntityDefinition 取得
    // DynamicEntitySchemaMigrationService.GetPhysicalColumnsAsync + BuildPlan + GenerateSql(dryRun想定)
    // ビュー: 差分テーブル（列名 / 操作 / 旧型 → 新型）+ 生成される Up/Down SQL のプレビュー
    //         + _nyf_migrations 履歴一覧（Rollback ボタン付き）
}

[HttpPost]
public async Task<IActionResult> SchemaMigrationApply(string entity)
{
    // BuildPlan → ApplyAsync(dryRun:false) → TempData にメッセージ → Redirect to SchemaMigration
}

[HttpPost]
public async Task<IActionResult> SchemaMigrationRollback(string migrationId)
{
    // RollbackAsync(migrationId) → Redirect to SchemaMigration
}
```

新規ビュー `Views/DynamicEntity/SchemaMigration.cshtml`（`ConfigDiagnostics.cshtml` を参考に
シンプルなテーブル + `<pre>` での SQL 表示でよい）。

このコントローラーアクションは既存の Admin 専用 `[Authorize]` ポリシーに従う
（`ConfigDiagnostics` と同じ認可属性をコピーする）。

### 6. DI 登録

`NetYamlForge/Extensions/ServiceCollectionExtensions.cs` に
`services.AddScoped<DynamicEntitySchemaMigrationService>();` を追加。

## 既存自動マイグレーションとの関係

- `ProjectSpecificInitializer.AutoMigrateMissingColumnsAsync` はそのまま残す
  （起動時の「列が無ければ追加」という最小自動化は開発体験として有用）。
- Phase 4.3 の新パイプラインは**それに追加して**、削除/型変更/可空性変更という
  自動化では危険な操作を、管理者が明示的に確認・適用・ロールバックできるようにするもの。
- 両者が同じ列を対象にした場合の競合は起きない: 起動時自動補列は「YAML にあって物理にない列」
  のみを対象とするため、Phase 4.3 の `BuildPlan` を呼ぶ時点ではその列はもう物理的に存在し、
  `AddColumn` 操作は生成されない（diff が既に解消済み）。

## テスト要件

新規 `NetYamlForge.Tests/Services/DynamicEntity/DynamicEntitySchemaMigrationServiceTests.cs`:

1. `BuildPlan`: YAML に新しい列 → `AddColumn` 1件。物理にあって YAML から消えた列 → `DropColumn` 1件。
   型が `string`→`int` に変わった列 → `AlterColumnType` 1件。
2. `GenerateSql`（非破壊: AddColumn のみ）→ `ALTER TABLE ... ADD COLUMN ...` を含む 1 文のみ。
3. `GenerateSql`（破壊的: DropColumn/AlterColumnType を含む）→ rebuild 手順（RENAME → CREATE → INSERT）
   を含むことを検証。
4. 統合テスト: in-memory SQLite に既存テーブル作成 → 列を削除する YAML 定義で `ApplyAsync(dryRun:false)`
   → 物理列が消えたこと・既存行のデータが保持されていることを確認 → `RollbackAsync` →
   元の列・データが復元されることを確認。
5. `_nyf_migrations` に正しくレコードが書き込まれること（`up_sql`/`down_sql`/`applied_at`）。

## 受け入れ基準

- `dotnet build` 0 警告 0 エラー。
- `dotnet test` 全緑（新規テスト含む）。
- エンティティ YAML に列追加 → 起動時自動補列で物理列が作成される（既存動作、回帰なし）。
- エンティティ YAML から列削除 or 型変更 → Admin の `SchemaMigration` 画面で diff と
  生成 SQL（dry-run）が確認でき、Apply 後に物理スキーマが追従し既存データが保持される。
- 上記 Apply を Rollback すると、物理スキーマとデータが Apply 前の状態に戻る。
- `docs/EVOLUTION_PLAN.md` の 4.3 行を ✅ に更新し、完了状況を追記する。
