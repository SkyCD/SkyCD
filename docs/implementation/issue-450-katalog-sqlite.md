# Issue #450: Katalog SQLite collection import

Read-only `katalog-sqlite` file-format plugin for Katalog File-mode SQLite
databases. No new dependencies beyond the repo-standard
`Microsoft.Data.Sqlite` 11.0.0 + `SQLitePCLRaw.lib.e_sqlite3` 2.1.14 pair
already used by other import plugins.

## Pipeline (`Plugins/SkyCD.Plugin.FileFormat.Katalog/Sqlite`)

- `KatalogSqliteSchema`: schema contract derived from upstream Katalog
  `core/database.cpp` (table factories), `core/collection.cpp` (version
  parameter, catalog/file/folder queries), and `core/catalog.cpp`
  (`device.device_external_id = catalog.catalog_id` join). Required tables
  and columns; everything else optional.
- `KatalogSqliteReader`: temp-file snapshot (source stream plus `-wal`/
  `-shm` sidecars when the source path is known), read-only + `query_only`
  open, schema/version validation before reads, column-tolerant SELECTs via
  `PRAGMA table_info`, NUMERIC-affinity-tolerant converters, per-batch
  cancellation. Maps rows onto the shared
  `SkyCD.Plugin.FileFormat.Katalog.Models` record models in the same assembly
  as the Memory-mode importer.
- `Katalog HierarchyBuilder.Build` (shared): devices, storages, tags,
  catalogs, folders produce identical node shapes and stable node ids as the
  Memory-mode CSV/IDX importer. Storage-brand details attach to Storage-type
  devices only through the shared builder's external-id lookup.
- `KatalogSqliteRecognizer` / `KatalogSqlitePlugin`: SQLite header plus
  Katalog schema markers (`device_external_id`, `file_catalog_id`,
  `folder_catalog_id`, `catalog_app_version`, `checksum_sha256`,
  `metadata_extended`); `.db`/`.sqlite`/`.sqlite3`/`.db3`; read-only.
- Custom exceptions (`KatalogSqliteCorruptException`,
  `KatalogSqliteUnsupportedSchemaException`,
  `KatalogSqliteEmptyCollectionException`), one class per file, fixed
  message templates.

## Environment findings

- `dotnet test` cannot run in this sandbox (testhost crashes on startup);
  CI owns the xunit run. Behavior verified with a standalone harness.
- This stack's SQLite native handle denies `File.OpenRead` sharing while
  any connection holds the database (proven by probe, both rollback-journal
  and WAL modes), so Katalog must be closed before importing; documented in
  `docs/plugins/katalog.md`. Shared `ReadWrite` streams still import
  a consistent snapshot excluding uncommitted frames (tested).

## Tests

`tests/SkyCD.Plugin.FileFormat.Katalog.Tests` (xUnit, schema-faithful synthetic
SQLite fixtures built by `KatalogFixtureBuilder`, closed cleanly with
pooling disabled): full multi-device/catalog import with metadata, tags,
checksums, and folders; schema variation without optional columns/tables;
missing-table/old-version/empty/garbage failures; cancellation; WAL snapshot
under an active writer; stable node ids across imports; source immutability
(mtime unchanged, no `-shm` created); detection true/false cases; write
refusal.
