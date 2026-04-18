# Legacy `data.db` Migration Strategy (v2 -> v3)

## Scope
This plan defines how legacy SQLite data from `legacy/SkyCD/data.db` is imported into the v3 EF Core model.

## Legacy Source
- Table: `list`
- Relevant fields: `ID`, `Name`, `ParentID`, `Type`, `Properties`, `Size`, `AID`

## Target
- `Catalogs`
- `CatalogNodes`
- `CatalogTags` (optional derived metadata)

## Import Steps
1. Open legacy database in read-only mode.
2. Group records by `AID` and create one `Catalog` per group.
3. Convert each `list` row to `CatalogNode`:
   - `Type == scdFile` -> `Kind = File`
   - otherwise -> `Kind = Folder`
   - `Properties` -> `MetadataJson`
4. Resolve invalid parent links:
   - if missing parent, place node at root (`ParentId = null`)
   - write warning to migration log
5. Validate resulting catalog with domain validator.
6. Persist using EF Core repositories/unit-of-work.

## Safety Rules
- Never execute SQL text from imported files directly.
- Keep raw legacy value in migration logs when conversion fails.
- Use transaction per catalog import unit.

## Current Status
- EF Core schema and migration pipeline implemented in #63.
- Import utility implementation is tracked in #70.
