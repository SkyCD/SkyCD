# Legacy Code Archival Strategy (v3)

## Status
Accepted

## Context
The repository still contains legacy VB.NET/WinForms sources alongside the v3 codebase. During migration, contributors need strict boundaries so new work does not drift back into legacy paths.

## Decision
- Keep legacy code in the repository for reference and rollback support.
- Move legacy implementation under a dedicated `legacy/` tree rather than leaving it at repository root.
- Treat `legacy/` as read-only except for archival/cleanup operations.
- All new feature work for v3 must be implemented in projects reachable from `SkyCD.V3.slnx`.

## Contributor Guardrails
Do:
- Use `SkyCD.V3.slnx` as the primary solution for build/test and new development.
- Implement new file-format behavior via v3 plugins and SDK abstractions.
- Update migration docs when legacy parity behavior changes.

Do not:
- Add new features to legacy VB.NET/WinForms projects.
- Introduce v3 dependencies from legacy code into active runtime paths.
- Make CI depend on building legacy code as part of v3 quality gates.

## Rollback and Support Policy
- Legacy sources remain available for forensic comparison and controlled migration fixes.
- Production/runtime support targets v3 code paths only.
- Rollback strategy is branch/tag based; legacy runtime deployment is not part of v3 release support.

## Follow-up Linked Tasks
- #90 Move legacy VB.NET solution/projects into `legacy/` and fix references.
- #91 Clean repository root and CI to enforce v3-only build path.
