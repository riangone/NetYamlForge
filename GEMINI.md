# NetYamlForge: GEMINI.md

This document serves as the foundational instructional context for Gemini CLI when interacting with the **NetYamlForge** project.

## Project Overview

**NetYamlForge** is a high-productivity, multi-tenant web application framework built with **ASP.NET Core (currently targeting .NET 10.0)**. It follows a "YAML-First" design philosophy, where database schemas, UI layouts, business logic (hooks), and dashboards are defined in YAML files, which are then used to dynamically generate a functional web interface.

### Core Technologies
- **Backend:** ASP.NET Core, C#, Dapper (ORM-less approach for performance and control).
- **Database:** Supports SQLite (default), SQL Server, PostgreSQL, and MySQL/MariaDB.
- **Frontend:** Server-side rendering with Razor Views, dynamic UI generation based on YAML.
- **AI Integration:** Deep integration with multiple LLM providers (Gemini, Claude, Qwen, Ollama, LmStudio, Copilot) for development assistance, natural language querying, and customer support (AI Window system).
- **Configuration:** YAML (YamlDotNet) and JSON Schema validation for configuration files.

### Key Architecture & Directory Structure
- `NetYamlForge/`: Main web application project.
    - `config/`: Global configuration files (`dashboard.yml`, `entities.yml`, `i18n.yml`).
    - `projects/`: Multi-tenant project definitions. Each subdirectory is a "tenant" project.
        - `{project}/entities/`: YAML definitions for business entities.
        - `{project}/project.yaml`: Project-specific database and metadata configuration.
    - `Services/`: Core logic, including `Cli/` (scaffolding tools) and `AI/` (LLM integrations).
    - `Extensions/`: DI registration and middleware extensions.
    - `skills/`: System prompts and LLM skill definitions.
- `NetYamlForge.Tests/`: Comprehensive test suite (xUnit).
- `NetYamlForge.Analyzers/`: Custom Roslyn analyzers to enforce coding standards (e.g., forbidding direct DB connection instantiation in services).
- `docs/`: Extensive documentation in Japanese, English, and Chinese.

---

## Building, Running, and Testing

### Prerequisites
- .NET 10.0 SDK
- Appropriate database drivers (SQLite is included by default).

### Common Commands

| Task | Command |
|------|---------|
| **Build** | `dotnet build` |
| **Run App** | `dotnet run --project NetYamlForge/NetYamlForge.csproj` |
| **Run Tests** | `dotnet test NetYamlForge.Tests/NetYamlForge.Tests.csproj` |
| **Init Project** | `dotnet run --project NetYamlForge/NetYamlForge.csproj -- --init-project --project=<name> --display-name="<Name>" --db-type=sqlite` |
| **Scaffold Entities**| `dotnet run --project NetYamlForge/NetYamlForge.csproj -- --scaffold-entities --project=<name>` |
| **Scaffold Hook** | `dotnet run --project NetYamlForge/NetYamlForge.csproj -- --scaffold-hook --name=<HookName> --project=<name> [--with-tests]` |
| **Modernize YAML** | `dotnet run --project NetYamlForge/NetYamlForge.csproj -- --upgrade-entity-yaml --project=<name>` |

---

## Development Conventions

### 1. YAML-Driven Development
- **Entity Definitions:** Define tables, columns, forms, and filters in `projects/{project}/entities/{entity}.yml`.
- **Validation:** Use `YamlSchemaValidationTests.cs` to verify YAML structure.
- **Scaffolding:** Use the CLI tools to bootstrap YAML from existing databases or create new hooks.

### 2. Business Logic (Hooks)
- Custom logic should be implemented as "Hooks" rather than modifying the core `DynamicEntityController`.
- Hooks are registered in the YAML and executed during the CRUD lifecycle (e.g., `BeforeSave`, `AfterDelete`).

### 3. AI Integration Patterns
- System prompts are stored in `NetYamlForge/skills/` as `.md` files.
- The `AutoDealerChatService` manages specialized AI agents (Staff vs. Customer).
- Use `ICLIService` implementations to interact with different LLM backends.

### 4. Database Access
- Use **Dapper** for data access.
- Avoid direct `SqlConnection` or `SqliteConnection` instantiation in services; use the provided repository patterns or ensure compliance with custom analyzers (`NetYamlForge.Analyzers`).

### 5. Multi-Tenancy
- The framework identifies the current project/tenant via the URL path: `/{project}/{controller}/{action}`.
- Use `ProjectManager` and `ProjectMiddleware` to resolve tenant-specific context.

---

## Documentation Index (Key Files)
- `README-ja.md`: Main entry point for Japanese documentation (Hub).
- `docs/quickstart-ja.md`: Shortest path to running the app.
- `docs/AI-SYSTEM-PROMPT-CONFIG.md`: How to configure AI prompts.
- `docs/AI-WINDOW-README.md`: Overview of the AI customer service system.
- `docs/COMMON_HOOKS.md`: Reference for available business logic hooks.

---

*This GEMINI.md is generated to provide context. Adhere to the established patterns in `Program.cs` and `ServiceCollectionExtensions.cs` when extending the framework.*
