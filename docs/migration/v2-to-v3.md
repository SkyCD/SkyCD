# SkyCD Migration Guide: v2 to v3

## Overview
SkyCD v3 uses a new C#/.NET 10 architecture with EF Core + SQLite schema. Legacy v2 data can be migrated from `data.db`.
In this repository, the legacy sample database is located at `legacy/SkyCD/data.db`.

## What is Migrated
- `list` table records grouped by `AID` -> one v3 `Catalog` per group
- `Type` mapping:
  - `scdFile` -> `File`
  - anything else -> `Folder`
- `Properties` -> `MetadataJson`
- `Size` -> `SizeBytes` for file nodes

## CLI Command
```powershell
dotnet run --project tools/SkyCD.Migration.Cli -- --legacy-db <path-to-v2-data.db> --target-db <path-to-v3.db>
```

### Dry Run
```powershell
dotnet run --project tools/SkyCD.Migration.Cli -- --legacy-db <path> --target-db <path> --dry-run
```

## Logs and Errors
- Validation/import issues are printed to stderr as actionable messages.
- Invalid rows are skipped while valid catalogs continue importing.

## Legacy Plugin Compatibility
- Legacy binary plugins are not loaded directly.
- Re-implement format support using v3 plugin SDK and manifests (`plugin.json`).
