# AGENTS.md

Full instructions: [CLAUDE.md](CLAUDE.md)

## Critical gotchas

- **Deleting a sub-project**: also delete `NetYamlForge/projects/<name>/Hooks/`,
  `NetYamlForge.Tests/Hooks/`, and any test files referencing the project namespace —
  or `dotnet build` fails with CS0234/CS0246.

- **`columns.required` must match DB schema**: `EntityDbSchemaConsistencyValidator`
  throws at startup if a NOT NULL/no-default column lacks `required: true` in the
  `columns` section of entity YAML (not just `forms`).

- **Roslyn analyzers appear as compiler errors during `dotnet build`**:
  DCS001 (no SQL string interpolation), DCS002 (no `.Result`/`.Wait()`),
  DCS003 (no direct `IDbConnection`), DCS004 (use `UserRoles` constants).

- **CLI `--json` flag**: add to any scaffolding command for structured JSON output
  (`generatedFiles`/`skippedFiles`/`nextSteps`/`errors`) on stdout.

- **Default admin credentials**: `admin` / `Admin@123` (created by `DbInitializer`
  on first run).

- **Solution has nested projects**: `NetYamlForge.AI` and `NetYamlForge.AI.Web`
  live under `NetYamlForge/` directory, not at repo root like other projects.
