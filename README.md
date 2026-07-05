# NetYamlForge

A high-productivity, multi-tenant web application framework built with ASP.NET Core (.NET 10.0) that uses YAML configuration to automatically generate CRUD interfaces, business logic hooks, and data management features.

## Features

- **YAML-Driven Development**: Define database schemas, UI layouts, business logic hooks, and dashboards through YAML files
- **Multi-Tenancy**: Isolated project tenants with independent databases and configurations
- **Dynamic SQL Generation**: Automatic generation of safe SQL statements based on YAML definitions
- **Hook System**: Execute custom business logic before/after CRUD operations
- **Multi-Database Support**: SQLite (default), PostgreSQL, MySQL, SQL Server
- **AI Integration**: Built-in support for multiple LLM providers (Gemini, Claude, Qwen, Ollama, LM Studio, etc.)
- **Hot Reload**: YAML configuration hot-reload in development mode

## Quick Start

```bash
# Clone the repository
git clone https://github.com/riangone/NetYamlForge.git
cd NetYamlForge

# Build the solution
dotnet build

# Run the development server
dotnet run --project NetYamlForge

# Create a new project
dotnet run --project NetYamlForge -- --init-project --project=todo-app --display-name="Todo App" --db-type=sqlite

# Scaffold entities from existing database
dotnet run --project NetYamlForge -- --scaffold-entities --project=todo-app
```

## Project Structure

```
NetYamlForge/
├── NetYamlForge/                 # Main web application
│   ├── Controllers/             # MVC controllers
│   ├── Services/                # Business logic services
│   ├── projects/                # Multi-tenant project configurations
│   └── wwwroot/                # Static assets
├── NetYamlForge.Tests/        # xUnit test project
├── NetYamlForge.Analyzers/    # Roslyn code analyzers
└── docs/                       # Documentation (Japanese, English, Chinese)
```

## Documentation

- [Japanese Documentation](README-ja.md)
- [Quick Start Guide](docs/quickstart-ja.md)
- [Framework Overview](docs/framework-overview-tutorial-ja.md)
- [Developer Tutorial](docs/developer-tutorial-ja.md)


## Testing

```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~DynamicEntityControllerTests"

# Run single test
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"

# Generate code coverage report
dotnet test --collect:"XPlat Code Coverage"
```

## Data Migrations

NetYamlForge supports automated and manual data migrations for tenant databases.

### Migration Directory
Store your migration SQL scripts in the following path:
`projects/<project-name>/database/migrations/NNN_description.sql` (where `NNN` is a 3+ digit version number, e.g., `001_init.sql`).

### SQL Segmentation
Use `-- +up` and `-- +down` to segment your migration script:
```sql
-- +up
CREATE TABLE posts (id INT, title TEXT);

-- +down
DROP TABLE posts;
```
If no segment tags are specified, the entire file is treated as `up` SQL.

### CLI Commands
You can manage migrations using the following CLI parameters (replace `<project-name>` with your actual project name):

- **Apply pending migrations**:
  ```bash
  dotnet run --project NetYamlForge -- --migrate-data --project=<project-name>
  ```
- **Check migration status**:
  ```bash
  dotnet run --project NetYamlForge -- --migrate-data-status --project=<project-name>
  ```
- **Rollback to a specific version**:
  ```bash
  dotnet run --project NetYamlForge -- --migrate-data-rollback --version=<version-number> --project=<project-name>
  ```

Pending migrations are also automatically applied upon application startup.

## Default Credentials

During the first startup, a default administrator account is seeded. The password is determined by the following priority:
1. `NYF_ADMIN_PASSWORD` environment variable.
2. `Auth:DefaultAdminPassword` configuration value in `appsettings.json`.
3. If neither is set, a random password is generated and printed in the startup logs.

- **Username**: `admin`

## License

This project is licensed under the terms described in the LICENSE file.

---

For Japanese documentation, see [README-ja.md](README-ja.md).