# security-demo

This is NetYamlForge's **single official demo project**. It replaces the
previous collection of parallel business-vertical demos (blog, inventory,
todo-app, auto-dealer-demo, biz-docs, task-management, and several
`_sandbox/` fixtures like golden-template/memo-app/memo2) that had
accumulated without a shared purpose.

## Why one demo, and why this one

The framework's positioning is "a compile-time + runtime guarded scaffold for
AI-generated YAML/SQL/hook code" (see the repository root `README.md` and
`SECURITY.md`). Adding another vertical demo (a CRM, a second blog, a second
inventory tool) does not demonstrate that positioning — it only proves the
generic CRUD pipeline works, which one demo already proves. What actually
needs demonstrating is the thing that makes this framework different: **the
guardrails that reject unsafe patterns before they can do damage.**

So this project intentionally does two things at once:

1. A small, real, working CRUD app (`note` + `attachment` entities, one
   custom action `publish`/`unpublish`/`archive`) — enough surface to
   exercise the generic DynamicEntity pipeline (list/create/edit/delete/
   action/export/search), the MCP tool-calling surface, and localization,
   end to end over HTTP.
2. A documented, *tested* set of guardrails, each backed by an automated
   test that proves the framework actually rejects the unsafe input — not
   just a claim in a README.

## The guardrails this demo proves

| # | Guardrail | Enforced by | What it rejects | Proof |
|---|-----------|-------------|------------------|-------|
| 1 | Entity metadata validation | `NetYamlForge.Services.DynamicCrudMetadataValidator` (built on `SqlSafetyGuard`) | YAML-declared table/column/join names or `expression` strings that smuggle raw SQL (statement separators, comments) or use a join type outside `left/inner/right` | `NetYamlForge.Tests/SecurityDemoGuardRailTests.cs` — loads `docs/unsafe-examples/unsafe-note-entity.yml.txt` and asserts it throws before any query is built |
| 2 | Path traversal prevention | `NetYamlForge.Services.PathSafetyGuard.NormalizeAndValidatePath` | Any resolved path that escapes its declared base directory (`../../../etc/passwd`, or an absolute path outside the tenant's project dir) | Same test file — reproduces the traversal payload described in `docs/unsafe-examples/path-traversal-hook.cs.txt` and asserts `UnauthorizedAccessException` |
| 3 | Default-deny public API | Per-entity `api:` flag (default `disabled`) | External API/MCP tool access to any entity that hasn't explicitly opted in | `attachment.yml` has no `api:` key; `NetYamlForge.Tests.Integration.McpServerIntegrationTests.ToolCall_OnDisabledApiEntity_ShouldReturnError` calls it via MCP and asserts it is rejected |

Each guardrail's "unsafe example" is kept **outside** `entities/` and
`Hooks/` (as `.yml.txt` / `.cs.txt` reference copies under
`docs/unsafe-examples/`) rather than as a live, loaded file. This is
deliberate, not an oversight: if the invalid entity YAML were saved as a real
`entities/*.yml` file, `ProjectManager.InitializeAsync` would fail to load
the *entire* project at application startup (by design — a demo must not
silently boot with a broken entity), which would break every other test in
the suite that boots the app. Loading the same text as a string inside a
unit test proves the same rejection without that blast radius.

## What this demo does *not* include, on purpose

- No AI-generation jobs, scheduled batch jobs, or external API integrations
  (previous demos accumulated these; they added surface area without adding
  to the "guardrails" story).
- No second/third business entity beyond `note` + `attachment` — one CRUD
  entity plus one API-disabled-by-default entity is enough to cover the
  generic pipeline and the default-deny guardrail; more entities would only
  add maintenance weight.
- No project-specific `i18n.yml` — the localization test this project
  supports (`LocalizationIntegrationTests`) exercises the framework's shared
  `Logout` strings in the global layout, which are already localized at the
  framework level.

## Tests that exercise this project

- `NetYamlForge.Tests/Integration/ApiEntityIntegrationTests.cs` — REST API CRUD
- `NetYamlForge.Tests/Integration/YamlPipelineEndToEndTests.cs` — YAML → runtime
  pipeline (list/create/edit/delete/action/export/search) over HTTP
- `NetYamlForge.Tests/Integration/McpServerIntegrationTests.cs` — MCP tool
  surface, including the default-deny guardrail above
- `NetYamlForge.Tests/Integration/LocalizationIntegrationTests.cs` — culture
  switching against a real page render
- `NetYamlForge.Tests/DocumentPdfServiceTests.cs` — PDF template rendering,
  using the `pdf-templates/` fixtures carried over from the retired
  `biz-docs` demo (kept because they test the framework's PDF/font
  resolution engine, not `biz-docs`'s business content)
- `NetYamlForge.Tests/SecurityDemoGuardRailTests.cs` — the three guardrails
  above
