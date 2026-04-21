# Contributing to SkyCD

## Default Development Path
Use the main stack for all new work.

- Build/test entrypoint: `SkyCD.slnx`
- CI quality gates: `.github/workflows/ci.yml`
- Architecture and migration docs: `docs/architecture/`, `docs/migration/`

## Do
- Implement features and fixes in `src/`, `tests/`, `tools/`, and `Plugins/` (legacy formats remain in `Plugins/samples/`).
- Keep pull requests green against CI checks.
- Use `dotnet format SkyCD.slnx --verify-no-changes` before opening PRs.

## Do Not
- Add new feature work to legacy VB.NET/WinForms code paths.
- Add or depend on legacy solution builds in default CI workflows.
- Use legacy build artifacts as acceptance evidence for changes.
