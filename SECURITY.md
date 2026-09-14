# Security Posture

NetYamlForge generates backend code (SQL, CRUD endpoints, batch jobs) from YAML,
often with AI assistance. Because AI-authored configuration can introduce
mistakes a human reviewer might not catch, the framework ships compile-time and
runtime guardrails whose job is to make the generated code safe by construction,
not just "safe if the YAML author was careful." This document is a factual,
versioned account of what those guardrails currently cover, so anyone evaluating
the framework doesn't have to take that claim on faith.

Last reviewed: 2026-09-11.

## Guardrails in place

| Risk | Guardrail | Where |
|---|---|---|
| SQL string interpolation / raw concatenation | `ForbiddenPatternAnalyzer` (Roslyn analyzer) fails the build if generated or hand-written code interpolates untrusted input into SQL | `NetYamlForge.Analyzers/` |
| Blocking async calls (`.Result` / `.Wait()`) | Same analyzer, compile-time | `NetYamlForge.Analyzers/` |
| Hardcoded role/tenant identifiers | Same analyzer, compile-time | `NetYamlForge.Analyzers/` |
| Path traversal in batch jobs (tenant `.db` files, SQL file/output paths) | `PathSafetyGuard.NormalizeAndValidatePath` — resolves and binds the target path under a tenant's project directory before any file I/O; rejects `..` escapes and same-prefix directory deception | `NetYamlForge/Services/PathSafetyGuard.cs`, used by `SqlBatchStepHandlers.cs` |
| Path traversal / arbitrary directory read in directory-scan import jobs | `PathSafetyGuard.ValidateAgainstAllowList` — `source_path` (user-supplied, absolute) must resolve under an administrator-configured allow-list (`DirectoryImport:AllowedRoots` in `appsettings.json`); **default is deny-all**, not allow-all, when unconfigured | `NetYamlForge/Services/BatchJob/DirectoryImportExecutor.cs` |
| SQL injection via the AI `query_data` tool | `SqlSafetyGuard` — identifier/expression allow-listing, parameterized values, operator allow-list (`=, !=, <, <=, >, >=, like`) | `NetYamlForge/Services/SqlSafetyGuard.cs`, `AiToolRegistryInitializer.cs` |
| Multi-tenant DB isolation for system-level (non-tenant) batch jobs | System jobs run against `_system_jobs/`, a directory that is a sibling of `projects/`, not an ancestor — so a relative-path escape from a system job can never reach another tenant's `projects/<tenant>/` directory | `PathSafetyGuard.GetSystemJobBaseDir` |

Every row above has unit and/or integration test coverage in
`NetYamlForge.Tests/` (see `PathSafetyGuardTests.cs`,
`SqlBatchStepHandlersSecurityTests.cs`, `DirectoryImportExecutorSecurityTests.cs`)
that exercises the guardrail through the real call path (handler `ExecuteAsync`,
not just the validator in isolation), including negative cases (traversal
attempts, same-prefix deception, unconfigured allow-lists).

## Known gaps / actively tracked

- **AI session / tool scoping under context loss**: if `ProjectScope` is empty
  (e.g. CLI offline mode or an async background workflow), session state can
  fall back to a `default` scope. This is a design gap, not yet closed — do not
  rely on tool-call isolation across concurrent tenants in that code path.
  Tracked in `docs/FRAMEWORK-SECURITY-REFACTOR-PLAN.md`.
- Security review here is currently maintainer self-review plus automated
  tests, not third-party penetration testing. Treat this document as "here is
  what we checked," not an external audit attestation.

## Reporting a vulnerability

Please open a private security advisory on the GitHub repository (or contact
the maintainer directly) rather than a public issue. Include the affected
file/handler, a reproduction, and — if you have one — a suggested fix. We will
acknowledge within a reasonable timeframe and credit the report once a fix
ships, unless you ask otherwise.
