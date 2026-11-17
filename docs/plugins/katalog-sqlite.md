# Katalog SQLite Collection Import Plugin

## Overview
The `katalog-sqlite` plugin provides import-only support for Katalog
File-mode SQLite collection databases (Katalog 2.x "File" database mode, one
`.db`-style file holding the whole collection). It recognizes databases by
content (SQLite header plus Katalog-specific schema markers), never by file
name alone, so the user-configurable database filename is accepted under any
of the `.db`, `.sqlite`, `.sqlite3`, `.db3` extensions. Direct MySQL/MariaDB
connections and writing Katalog databases are out of scope.

## User Export Instructions in Katalog
In Katalog open **Settings -> Collection & Database** and switch the
collection to **File** mode (or use the v2.3+ "export the collection to an
SQLite database" action) so the collection is stored as a single SQLite
database file. **Close Katalog before importing**: the SQLite stack used here
denies plain shared reads while any connection holds the database open, so an
import started against a running Katalog cannot open the file.

## User Import Instructions in SkyCD
1. In SkyCD, choose **File -> Open...**.
2. Select your Katalog database file.
3. Content-aware detection recognizes the Katalog File-mode database and
   rejects unrelated SQLite files with an actionable error.
4. Devices, virtual groups, catalogs, folders, files, tags, checksums, and
   extracted media metadata import into the SkyCD catalog with stable
   source-derived node ids, so repeated imports of the same database produce
   identical mappings. The source database is never modified.

## Supported Versions & Schema Handling
- **Schema versions**: 2.6 and newer File-mode layouts (the upstream
  migration chain starts at 2.6). The version is read from the `parameter`
  table (`type 'collection'`, `name 'version'`) before any data is read;
  older schemas are rejected, unknown version strings proceed with a
  diagnostic.
- **Schema variations**: required tables (`device`, `catalog`, `file`,
  `folder`, `tag`, `parameter`) and required columns are enforced; every
  other column is optional and read only when present, so databases from
  versions that predate newer metadata/checksum columns import cleanly.
  The optional `storage` table may be absent entirely.
- **WAL mode**: Katalog opens its database in WAL mode, so `-wal`/`-shm`
  sidecars next to the selected file are copied into the read-only snapshot
  when present. The snapshot is opened read-only with `query_only`
  enforcement and deleted after the import.
- **Catalog identity**: catalogs join their owning catalog-type device via
  `device.device_external_id = catalog.catalog_id`; catalogs without a
  device row fall back to the denormalized `file.file_catalog` name, then to
  `Catalog_<id>`. Files join via `file.file_catalog_id`, folders via
  `folder.folder_catalog_id`.

## Security and Resource Bounds
- The source stream is materialized to a temp copy; the original is only
  ever read (sidecar copies are best-effort and read-only).
- Reads are cancellable (checked per batch and between tables) and served
  from a single read-only connection, so an import observes one consistent
  snapshot; uncommitted writer frames are never visible.
- Paths containing `..` traversal sequences are rejected instead of imported.

## Known Loss of Information
- **Statistics, search history, backup mappings, catalog filters, settings**:
  the `statistics_device`, `search`, `device_mapping`, and `catalog_filter`
  tables are not imported; the summary lists them as unsupported.
- **Upstream-only file columns** without a shared-model slot
  (`file_catalog` catalog name beyond the fallback, `video_framerate`,
  `video_bitrate`) are documented rather than imported.
- Storage totals/free space have no SQLite column and import as zero; device
  totals carry the sizes instead.
