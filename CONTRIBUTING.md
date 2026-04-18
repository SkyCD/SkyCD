# Contributing to SkyCD

## Default Development Path
Use the v3 stack for all new work.

- Build/test entrypoint: `SkyCD.V3.slnx`
- CI quality gates: `.github/workflows/v3-ci.yml`
- Architecture and migration docs: `docs/architecture/`, `docs/migration/`

## Do
- Implement features and fixes in `src/`, `tests/`, `tools/`, and `Plugins/samples/` for v3.
- Keep pull requests green against v3 CI checks.
- Use `dotnet format SkyCD.V3.slnx --verify-no-changes` before opening PRs.

## Do Not
- Add new feature work to legacy VB.NET/WinForms code paths.
- Add or depend on legacy solution builds in default CI workflows.
- Use legacy build artifacts as acceptance evidence for v3 changes.
