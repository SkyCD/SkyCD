# Repository Guidelines

## Mandatory Workflow Rules
1. For every issue: create a new branch from `origin/main`, implement the fix, and open a PR.
2. Before opening any PR: verify the project compiles successfully.
3. If multiple issues are requested: solve each issue in a separate branch.
4. Exception for sub-issues: if tasks are sub-issues of one larger feature, create PRs into the shared feature branch.
5. If a PR targets a feature branch: also create an additional PR from that feature branch into `main`.
6. Do not include temporary/debug scripts in commits or PRs.
7. Each commit message must begin with `[XXX]` where `XXX` is the issue number solved by that commit.
8. PR descriptions must clearly state what issue they solve, using format "Solves #XXX: [issue title]" at the beginning.
9. Prefer solving issues with NuGet packages when appropriate, but only use packages with licenses compatible with project policy.
10. After each code change, if the build fails, do not commit the changes.
11. If possible, everytime write integration tests for new functionality

## Project Structure (SkyCD)
- `src/`: active v3 application code.
- `src/SkyCD.App`: Avalonia desktop app entrypoint and views (`Program.cs`, `Views/*`).
- `src/SkyCD.Presentation`: UI-facing view models and presentation logic.
- `src/SkyCD.Domain`: core catalog domain model and validation logic.
- `src/SkyCD.Application`: use-case abstractions and application-level contracts.
- `src/SkyCD.Infrastructure`: persistence layer (EF Core `SkyCdDbContext`, repositories, migrations).
- `src/SkyCD.Plugin.Abstractions`: plugin capability contracts (file formats, menu, modal, lifecycle).
- `src/SkyCD.Plugin.Runtime`: plugin discovery/loading and compatibility checks.
- `src/SkyCD.Plugin.Host`: host-side plugin routing/execution services.
- `Plugins/`: functional plugin implementations with `plugin.json` manifests.
- `Plugins/samples/`: legacy-format plugins and sample menu/modal plugins.
- `tests/`: xUnit test projects organized per module (`SkyCD.App.Tests`, `SkyCD.Infrastructure.Tests`, etc.).
- `tools/`: CI/publishing scripts and migration CLI.
- `docs/`: architecture, migration, plugin SDK, legal/license, and implementation guides.
- `legacy/`: archived VB.NET WinForms code; reference-only for migration context.

## Build, Test, and PR Validation
- Restore: `dotnet restore SkyCD.slnx`
- Build (required before PR): `dotnet build SkyCD.slnx --configuration Release --no-restore`
- Test: `dotnet test SkyCD.slnx --configuration Release --no-build`
- Format check: `dotnet format SkyCD.slnx --verify-no-changes --verbosity minimal`

## License and Dependency Policy
- License policy source: `docs/legal/dependency-license-policy.json`.
- Allowed licenses include: MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, MPL-2.0, Zlib.
- Blocked licenses include GPL/LGPL/AGPL families, SSPL-1.0, and CC-BY-NC-4.0.
- Unknown/missing licenses are non-compliant unless explicitly whitelisted in policy.

# Hard requirements
- .NET 10

## Commit Guidelines
AI-generated commits should not include the 'Co-Authored-By' line unless explicitly instructed by the user.
