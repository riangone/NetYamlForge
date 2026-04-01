# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Skill Routing

**NetYamlForge フレームワーク AI スキル**

利用可能なスキルがマッチする場合は、直接答えずにまずスキルを呼び出してください。

| ユーザーリクエスト | 呼び出すスキル |
|------------------|---------------|
| コードレビュー、マージ前チェック | `/nyf-review` |
| リリース、デプロイ、PR 作成 | `/nyf-ship` |
| 足場作り、エンティティ生成 | `/nyf-scaffold` |
| セキュリティ診断、脆弱性スキャン | `/nyf-security` |
| 自動車販売、在庫照会 | auto-dealer デモへ転送 |

**auto-dealer-demo サブプロジェクト スキル**

| ユーザーリクエスト | 呼び出すスキル |
|------------------|---------------|
| 在庫照会、車両分析 | `/dealer-inventory` |
| 営業リード管理、成約分析 | `/dealer-sales` |
| 顧客情報、購入履歴 | `/dealer-customer` |
| 試乗予約、サービス予約 | `/dealer-appointment` |

---

## Commands

```bash
# Build / Run
dotnet build
dotnet run --project NetYamlForge
dotnet build -c Release

# Tests
dotnet test
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"
dotnet test --collect:"XPlat Code Coverage"

# CLI scaffolding (run from repo root)
dotnet run -- --init-project --project=<name> --display-name="..." --db-type=sqlite
dotnet run -- --scaffold-entities --project=<name> [--no-overwrite]
dotnet run -- --scaffold-hook --name=<HookName> --project=<name> [--with-tests]
dotnet run -- --scaffold-batch-job --project=<name> --name=<job_name>
dotnet run -- --upgrade-entity-yaml --project=<name>

# PDFsharp で全テンプレートのサンプル PDF を生成
dotnet run -- --generate-pdf-samples [--output-dir=<path>]
```

`--json` を付けると CI 向けの構造化 JSON（`generatedFiles`/`skippedFiles`/`nextSteps`/`errors`）が stdout に出力される。

## Architecture

**NetYamlForge** is a .NET 10 MVC framework for building data management apps through YAML configuration. Each "project" is an independent tenant with its own database, entities, and business logic.

### Multi-Project Tenancy

The core concept: a single running app hosts many independent "projects". The route `/{project}/...` determines which project's database and config is active for each request.

- **`ProjectManager`** (singleton) — scans `projects/` at startup, loads and validates all `project.yaml` files, caches `ProjectInfo` objects
- **`ProjectScope`** (scoped) — holds the current project for one HTTP request
- **`ProjectMiddleware`** — extracts `{project}` from the route and populates `ProjectScope`

### YAML Configuration Layers

Each project in `projects/<name>/` contains:
- `project.yaml` — database type, features, project metadata
- `entities/*.yml` — entity definitions (columns, forms, validations, hooks)
- `dashboard.yml` — SQL-based statistics and charts
- `config/layout.yml` — navigation and theming
- `config/i18n.yml` — localization overrides
- `pages/*.yaml` — custom YAML-driven UI pages

All YAML files are validated against embedded JSON schemas (`NetYamlForge/Schemas/`) at startup.

### Dynamic SQL Generation

`DynamicCrudRepository` generates all SQL at runtime from entity metadata. It never uses string interpolation for identifiers — all column/table names are validated via `SqlSafetyGuard` regex before use. SQL dialect differences are handled by `ISqlDialect` implementations in `Services/Dialect/` (SQLite, SQL Server, PostgreSQL, MySQL).

### Hook System

Hooks run before/after database operations within the same transaction. They implement `IEntityHook`:
- `BeforeAsync` — can abort the operation by returning a failed `HookResult`
- `AfterAsync` — runs after the DB operation succeeds

Built-in hooks (validation, transformation, audit, relationships) are in `Services/Hooks/CommonHooks.cs`. Project-specific hooks are loaded dynamically from `projects/<name>/Hooks/` via `ProjectHookRegistry`.

### Service Registration

`ServiceCollectionExtensions.cs` organizes DI registration into:
- `AddMultiProjectInfrastructure()` — ProjectManager, ProjectScope, metadata providers
- `AddDatabaseServices()` — scoped `IDbConnection` factory (picks driver based on `ProjectScope`)
- `AddDynamicCrudCore()` — repositories, CRUD services, validators
- `AddProjectHooks()` + `AddEntityHooks()` — hook registries and 20+ built-in hooks

### Custom Roslyn Analyzers

`NetYamlForge.Analyzers` (netstandard2.0) enforces four rules as compiler errors/warnings:

| Rule | Severity | What it catches |
|------|----------|-----------------|
| DCS001 | Error | SQL string interpolation (injection risk) |
| DCS002 | Error | Blocking async calls (`.Result`, `.Wait()`) |
| DCS003 | Error | Direct `IDbConnection` instantiation (bypass DI) |
| DCS004 | Warning | Hardcoded role name strings (use `UserRoles` constants) |

These appear as standard compiler diagnostics during `dotnet build`.

### Key Controllers

- `DynamicEntityController` — CRUD list/create/edit/delete with HTMX partial updates
- `DashboardController` — executes admin-defined SQL aggregations from `dashboard.yml`
- `PageController` — renders YAML-defined custom pages
- `ApiEntityController` — REST API for entities
- `AccountController` / `UsersController` — auth and user management

### Database Initialization

`DbInitializer` creates schema and seeds data on first run. Default admin credentials: `admin` / `Admin@123`.

### Batch Jobs

`BatchJobHostedService` runs background workers on cron schedules defined in `projects/<name>/jobs/*.yml`. Job implementations live in `projects/<name>/Hooks/` and are scaffolded via `--scaffold-batch-job`. Job types include `sql_to_csv` and custom C# implementations via `IBatchJobHandler`.

### PDF Export

PDF テンプレートはグローバル定義 (`NetYamlForge/Schemas/pdf-templates/`) に統一されています。プロジェクト固有テンプレートが不要なプロジェクト（biz-docs 等）はグローバルテンプレートを直接使用します。

| クラス | エンジン | ライセンス | 用途 |
|---|---|---|---|
| `DocumentPdfService` | PDFsharp | MIT | **既定実装** (`IDocumentPdfService`)。Google Fonts (Noto Sans JP) で日本語表示に対応 |
| `PdfExportService` | PDFsharp | MIT | 一覧データの表形式 PDF |

`PdfTemplateSampleRunner` で全テンプレートのサンプル PDF を生成できます。

日本語 PDF を正しく描画するには Noto Sans JP TTF フォントが `wwwroot/fonts/` に必要です。セットアップ手順は `NETYOAMLFORGE-FONT-SETUP.md` を参照。

### Testing Patterns

Tests in `NetYamlForge.Tests/` use xUnit (~380 tests).

| File | What it covers |
|------|---------------|
| `DynamicEntityControllerTests.cs` | Full controller request pipeline |
| `EntityCrudExecutionServiceTests.cs` | Hook execution and transactions |
| `YamlSchemaValidationTests.cs` | YAML config parsing for all projects |
| `SqlGenerationSnapshotTests.cs` | SQL output regression (snapshot tests) |
| `YamlConfigStartupValidatorTests.cs` | Startup type validation |
| `ListStateUrlBuilderTests.cs` | URL state builder |

## Known Pitfalls for AI Agents

These are recurring mistakes — read before modifying or deleting sub-projects.

### Deleting a sub-project

When deleting `projects/<name>/`, also delete **all** of the following or `dotnet build` will fail with `CS0234`/`CS0246` errors:

1. `NetYamlForge/projects/<name>/Hooks/` — C# hook classes
2. `NetYamlForge.Tests/Hooks/` — hook test files for that project
3. Any other test files that reference the project's namespace

```bash
# Find lingering references before deleting
grep -rl "NetYamlForge.Projects.<ProjectName>" NetYamlForge.Tests/
dotnet build  # verify clean after deletion
```

### YAML `columns.required` must match the DB schema

`EntityDbSchemaConsistencyValidator` runs at startup and throws if a `NOT NULL` / no-default DB column is missing `required: true` in the **`columns`** section of the entity YAML (not just in `forms`).

| DB column definition | `columns.required` needed? |
|---|---|
| `NOT NULL` and no default | **yes** |
| `NOT NULL` with a `DEFAULT` | no |
| Nullable | no |
| `PRIMARY KEY` / `AUTOINCREMENT` | no |
