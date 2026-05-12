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

## Default Credentials

- **Username**: `admin`
- **Password**: `Admin@123`

## License

This project is licensed under the terms described in the LICENSE file.

---

For Japanese documentation, see [README-ja.md](README-ja.md).