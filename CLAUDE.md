# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build

# Run
dotnet run --project NetYamlForge

# Run tests
dotnet test

# Run a single test
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Build in release mode
dotnet build -c Release
```

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

### Testing Patterns

Tests in `NetYamlForge.Tests/` use xUnit. Large controller tests (`DynamicEntityControllerTests.cs`, `DashboardControllerTests.cs`) test full request pipelines. `SqlGenerationSnapshotTests.cs` uses snapshot testing for SQL output. `YamlSchemaValidationTests.cs` validates YAML config parsing.
