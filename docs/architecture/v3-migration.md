# SkyCD v3 Migration Blueprint

## 1. Objective
Migrate SkyCD from VB.NET/WinForms to C#/.NET 10/Avalonia as a cross-platform desktop app (Windows, macOS, Linux), with SQLite-first persistence and modern plugin contracts.

## 2. Chosen Migration Strategy
### Decision
Use a **parallel rewrite** with a controlled compatibility bridge for legacy file formats.

### Why this strategy
- Current codebase is tightly coupled (UI, storage, plugin loading), making incremental in-place replacement high-risk.
- Avalonia + .NET 10 architecture differs enough from WinForms that side-by-side construction is cleaner.
- Legacy support is better handled as dedicated migration and plugin workstreams.

### Non-goal
- No binary compatibility promise for legacy .NET Framework plugins.

## 3. Target Repository Structure
```text
/
|-- src/
|   |-- SkyCD.App/                         # Avalonia desktop entrypoint + composition root
|   |-- SkyCD.Presentation/                # ViewModels, UI state, command bindings
|   |-- SkyCD.Application/                 # Use-cases/orchestration, service contracts
|   |-- SkyCD.Domain/                      # Core entities/value objects/rules
|   |-- SkyCD.Infrastructure/              # SQLite, filesystem, logging, settings
|   |-- SkyCD.Plugin.Abstractions/         # Public plugin SDK contracts
|   |-- SkyCD.Plugin.Runtime/              # Discovery/loading/compat checks/isolation
|   |-- SkyCD.Plugin.Host/                 # Host-facing extension services for plugins
|   |-- SkyCD.Format.Builtin.LegacySCD/    # Built-in plugin: *.scd
|   |-- SkyCD.Format.Builtin.LegacyCSCD/   # Built-in plugin: *.cscd
|   |-- SkyCD.Format.Builtin.LegacyASCD/   # Built-in plugin: *.ascd
|
|-- plugins/
|   |-- samples/
|   |   |-- Sample.Format.Json/
|   |   |-- Sample.Menu.Commands/
|   |   |-- Sample.Modal.Dialog/
|   |-- manifests/
|       |-- README.md
|
|-- tests/
|   |-- SkyCD.Domain.Tests/
|   |-- SkyCD.Application.Tests/
|   |-- SkyCD.Infrastructure.Tests/
|   |-- SkyCD.Plugin.Runtime.Tests/
|   |-- SkyCD.Format.Compatibility.Tests/  # Legacy fixture parity tests
|   |-- SkyCD.EndToEnd.Tests/
|
|-- tools/
|   |-- SkyCD.Migration.Cli/               # Legacy conversion/import tooling
|   |-- scripts/
|       |-- ci/
|       |-- release/
|
|-- docs/
|   |-- architecture/
|   |   |-- v3-migration.md
|   |-- migration/
|   |   |-- v2-to-v3.md
|   |-- legal/
|       |-- dependency-policy.md
```

## 4. Module Boundaries
### UI Layer
- `SkyCD.App` + `SkyCD.Presentation`
- Responsibilities: Avalonia views, navigation, command dispatch, dialog invocation.
- Must not depend directly on SQLite, plugin assembly loading internals, or legacy parsing.

### Application Layer
- `SkyCD.Application`
- Responsibilities: use-cases (open catalog, save catalog, import legacy, execute plugin command).
- Depends on domain abstractions and service interfaces only.

### Domain Layer
- `SkyCD.Domain`
- Responsibilities: canonical catalog model, validation rules, domain invariants.
- No UI or infrastructure references.

### Infrastructure Layer
- `SkyCD.Infrastructure`
- Responsibilities: SQLite repositories, migrations, file I/O adapters, config, telemetry.
- Implements interfaces from application/domain.

### Plugin Runtime + SDK
- `SkyCD.Plugin.Abstractions`, `SkyCD.Plugin.Runtime`, `SkyCD.Plugin.Host`
- Responsibilities:
  - Define capabilities/contracts (file formats, menu contributions, modal contributions)
  - Discover and load plugin assemblies/manifests
  - Enforce compatibility and safe host interaction boundaries

## 5. Project Dependency Graph
```text
SkyCD.App
  -> SkyCD.Presentation
  -> SkyCD.Application
  -> SkyCD.Plugin.Host

SkyCD.Presentation
  -> SkyCD.Application
  -> SkyCD.Domain
  -> SkyCD.Plugin.Abstractions

SkyCD.Application
  -> SkyCD.Domain
  -> SkyCD.Plugin.Abstractions

SkyCD.Infrastructure
  -> SkyCD.Application
  -> SkyCD.Domain

SkyCD.Plugin.Runtime
  -> SkyCD.Plugin.Abstractions

SkyCD.Plugin.Host
  -> SkyCD.Plugin.Abstractions
  -> SkyCD.Plugin.Runtime
  -> SkyCD.Application

Built-in Format Plugins
  -> SkyCD.Plugin.Abstractions
  -> SkyCD.Domain

Tests
  -> Target projects under src/
```

## 6. Legacy Compatibility Approach
### File format compatibility
- Implement each legacy format as a v3 plugin (not host special-casing):
  - `*.scd` via `SkyCD.Format.Builtin.LegacySCD`
  - `*.cscd` via `SkyCD.Format.Builtin.LegacyCSCD`
  - `*.ascd` via `SkyCD.Format.Builtin.LegacyASCD`
- Add fixture-based round-trip tests in `SkyCD.Format.Compatibility.Tests`.

### Data compatibility
- Canonical storage is SQLite only.
- Introduce schema versioning + migrations for forward evolution.
- Legacy imports map old fields into new domain model through migration services.

### Plugin compatibility
- Do not load old VB/.NET Framework plugin binaries directly.
- Provide migration guidance and sample templates for plugin authors.

## 7. Phased Plan and Issue Mapping
### Phase 0 - Foundation
- #60 Architecture blueprint (this document)
- #61 New .NET 10 + Avalonia solution baseline
- #64 BSD-2 license migration + dependency policy
- #86 GitHub automation (tests/build/style)
- #87 Dependabot configuration

### Phase 1 - Core Model + Storage
- #62 Modern catalog data model
- #63 SQLite-first persistence

### Phase 2 - Plugin Platform Core
- #65 Plugin SDK contracts
- #66 File-format plugin contracts
- #69 Plugin runtime (discovery/isolation/version checks)

### Phase 3 - Legacy Format Continuity
- #71 Legacy `*.scd` plugin
- #72 Legacy `*.cscd` plugin
- #73 Legacy `*.ascd` plugin
- #74 Parity matrix tracker
- #70 Migration tooling/docs integration

### Phase 4 - Plugin UX Extensions
- #67 Menu/command extension points
- #68 Modal/dialog extension points

### Phase 5 - Additional Format Ecosystem
- #75 `*.json` plugin
- #76 `*.csv` plugin
- #77 `*.xml` plugin
- #78 `*.yaml/*.yml` plugin
- #79 `*.toml` plugin
- #80 `*.html` export plugin
- #81 `*.md` export plugin
- #82 `*.zip` read-only plugin
- #83 `*.7z` read-only plugin
- #84 `*.tar/*.tar.gz/*.tgz` read-only plugin
- #85 `*.iso` read-only plugin

## 8. Top 5 Technical Risks and Mitigations
| Risk | Impact | Probability | Mitigation |
|---|---|---|---|
| R1: Plugin API churn during early implementation | Rework across plugin tickets | High | Freeze vNext contracts after #65 + #66 design review; version contracts semantically; add compatibility tests in #69 |
| R2: Legacy `*.ascd` parser security concerns (SQL-like payload) | Data integrity/security issues | Medium | Treat format parser as data transform only (no SQL execution), add hostile fixture tests in #73 |
| R3: Cross-platform UI behavior divergence (Win/macOS/Linux) | Runtime bugs and UX inconsistency | Medium | Enforce tri-OS CI in #61/#86, maintain end-to-end smoke tests per OS |
| R4: SQLite schema and migration drift | Data loss or migration failure | Medium | Formal schema versioning in #62/#63, migration tests with rollback and fixture snapshots |
| R5: Scope expansion from many file format plugins | Schedule slip | High | Keep P0/P1 gates strict, ship legacy parity first (#71-#74/#70), defer P2 formats (#75-#85) if needed |

## 9. Execution Guardrails
- Keep host logic format-agnostic; all format behavior must live in plugins.
- Keep domain model independent from Avalonia and plugin runtime internals.
- Keep infrastructure replaceable at boundaries, even if SQLite is the only supported local backend.
- Accept minimal viable v3 scope if parity blockers emerge; protect architecture stability first.

