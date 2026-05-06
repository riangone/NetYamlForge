# Repository Guidelines

## Project Structure & Module Organization
- `NetYamlForge/` contains the ASP.NET MVC app. Key areas include `Controllers/`, `Services/`, `Models/`, `Views/`, `Schemas/`, and `wwwroot/` for static assets.
- `NetYamlForge/projects/<name>/` holds per-tenant YAML configuration (`project.yaml`, `entities/*.yml`, `pages/*.yaml`, `config/*.yml`).
- `NetYamlForge.Tests/` contains xUnit tests and snapshots.
- `NetYamlForge.Analyzers/` hosts Roslyn analyzers used during build.
- `docs/` is the documentation hub (many docs are linked from `README-ja.md`).

## Build, Test, and Development Commands
- `dotnet build` builds the solution.
- `dotnet build -c Release` builds optimized binaries.
- `dotnet run --project NetYamlForge` runs the web app locally.
- `dotnet test` runs all tests.
- `dotnet test --filter "FullyQualifiedName~ClassName.MethodName"` runs a single test.
- `dotnet test --collect:"XPlat Code Coverage"` produces coverage data.

CLI helpers:
- `dotnet run -- --init-project --project=<name> --display-name="..." --db-type=sqlite`
- `dotnet run -- --scaffold-entities --project=<name> [--no-overwrite]`
- `dotnet run -- --scaffold-hook --name=<HookName> --project=<name> [--with-tests]`

## Coding Style & Naming Conventions
- C# defaults: 4-space indentation, `PascalCase` for types/methods, `camelCase` for locals/parameters.
- File names typically match type names (e.g., `ProjectManager.cs`).
- YAML pages use `camelCase` keys in `pages/*.yaml` to match entity conventions.
- Avoid SQL string interpolation; rely on the existing `SqlSafetyGuard`/dialects and analyzers.

## Testing Guidelines
- Framework: xUnit (see `NetYamlForge.Tests/`).
- Test files use `*Tests.cs`. Snapshot tests exist for SQL generation.
- When adding features, include tests for the relevant controller/service and any YAML schema impacts.

## Commit & Pull Request Guidelines
- Recent history uses Conventional Commits such as `feat:`, `docs:`, and `refactor:` with short, imperative summaries. Keep scope tight and messages descriptive.
- PRs should include a clear summary, linked issue (if any), and notes on tested commands. Add screenshots or brief UI notes when views change.

## Security & Configuration Tips
- Do not commit secrets. Use `appsettings.Development.json` for local overrides and environment variables for sensitive values.
- Default admin credentials exist for first run; change them for real deployments.
