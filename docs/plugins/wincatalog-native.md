# WinCatalog Native Catalog Import Plugin

## Overview
The `wincatalog-native` plugin provides read-only import of native WinCatalog
catalog databases (`*.w3cat`). It detects the SQLite container plus
WinCatalog-specific markers, so unrelated SQLite files are never claimed.
Native export back to WinCatalog is out of scope.

Legacy collection profiles (`*.wcat`, `*.wca`, `*.wcl`) are **not** parsed.
They produce an actionable diagnostic directing users to convert to `.w3cat`
inside WinCatalog or to export a CSV/XML report (see `wincatalog-csv` and
`wincatalog-xml`).

## User Import Instructions in SkyCD
1. In SkyCD, choose **File -> Open...** (or run `skycd open <path.w3cat>` via CLI).
2. Select your `.w3cat` file.
3. Content-aware detection verifies the SQLite header and WinCatalog markers
   before importing. Other SQLite databases are rejected with a diagnostic.
4. Media roots, folder hierarchy, files, sizes, dates, comments, and tags are
   imported where present. Source files are never modified.

## Supported Versions & Locales
- **Container**: SQLite (`SQLite format 3\0`) with WinCatalog catalog tables.
- **Schema handling**: column-tolerant reads; tables resembling catalog, disk,
  volume, folder, file, entry, media, or item tables are mapped. Tables without
  a name column are skipped with a diagnostic.
- **Versions**: an explicit version marker is recorded as provenance when
  present; unknown versions are imported best-effort or rejected with a
  diagnostic when no catalog tables exist.
- **Legacy profiles**: `.wcat` (WinCatalog 2003–2009 collections), `.wca`, and
  `.wcl` (WinCatalog Light collections) are rejected with a conversion
  diagnostic. No direct parsing is claimed until versioned fixtures validate it.

## Security and Resource Bounds
- Source is materialized to a temp snapshot and opened read-only with
  `PRAGMA query_only = ON`; sidecars (`-wal`/`-shm`/`-journal`) are not used.
- Bounded: 1,000,000 item cap, identifier truncation, 8 MiB blob cap, and
  cancellation checks every 500 rows.
- Unknown/corrupt/encrypted profiles produce dedicated errors; nothing is
  silently omitted or rewritten.

## Known Loss of Information
- **Thumbnails and Icons**: not carried by the mapped tables; not imported.
- **Search history, statistics, settings**: intentionally not imported.
- **Fields Absent from Source**: only mapped columns are recovered; missing
  tables/columns are recorded as diagnostics.
- **Provenance**: source table per row (`WinCatalogTable`), schema marker
  (`WinCatalogSchema`), and original full path (`WinCatalogPath`) are preserved
  as properties.
