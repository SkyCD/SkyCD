# Katalog import

The `SkyCD.Plugin.FileFormat.Katalog` plugin imports Katalog data through two read-only
file-format capabilities that share one enable toggle in **Options → Plugins**:

| Format id | Extensions | Source |
| --- | --- | --- |
| `katalog-csv-idx` | `.idx` | Memory-mode collection folder: `device.csv`, `storage.csv`, `parameters.csv`, `tags.csv`, catalog `.idx` and `.folders.idx` files |
| `katalog-sqlite` | `.db`, `.sqlite`, `.sqlite3`, `.db3` | File-mode SQLite collection database |

Both formats belong to the same vendor product and build their node trees through one shared
hierarchy builder, so devices, folders, files, tags, checksums and extracted media metadata import
with identical node shapes and stable source-derived node ids. Export back to Katalog is not
supported; the source collection is never modified.

The merged plugin keeps the explicit plugin id `SkyCD.Plugin.FileFormat.Katalog`. The retired
per-mode ids (`SkyCD.Plugin.Katalog.CsvIdx`, `SkyCD.Plugin.Katalog.Sqlite`) remain in existing
`PluginDocument` rows only as unavailable entries; they are no longer discovered, shown or
editable, and the merged plugin starts enabled.

## Memory-mode collection folder (`katalog-csv-idx`)

1. In SkyCD, choose **File → Open...**.
2. Select the folder Katalog uses for a Memory-mode collection, or a single catalog `.idx` file.
3. A folder is recognized as a Katalog collection when it contains `device.csv` together with
   `storage.csv`/`parameters.csv` or catalog `.idx` files, or when its `.idx` files start with the
   Katalog `<catalog...>` header. Standalone `.idx` files are content-detected the same way.
4. `device.csv` rows become media nodes or virtual groups (parent links are preserved); each
   catalog `.idx` adds its file entries, and the matching `.folders.idx` restores the folder
   hierarchy. `storage.csv` enriches device metadata (label, brand, model, serial, file system)
   and `tags.csv` attaches tags to matching item paths.
5. `.folders.idx` files are consumed together with their catalog and are not imported as separate
   catalogs.

The parser accepts the tab-separated layouts produced by Katalog 2.6 and newer. Relative paths
containing `..` traversal sequences are rejected instead of imported.

## File-mode SQLite database (`katalog-sqlite`)

### User export instructions in Katalog
In Katalog open **Settings -> Collection & Database** and switch the
collection to **File** mode (or use the v2.3+ "export the collection to an
SQLite database" action) so the collection is stored as a single SQLite
database file. **Close Katalog before importing**: the SQLite stack used here
denies plain shared reads while any connection holds the database open, so an
import started against a running Katalog cannot open the file.

### User import instructions in SkyCD
1. In SkyCD, choose **File -> Open...**.
2. Select your Katalog database file.
3. Content-aware detection recognizes the Katalog File-mode database and
   rejects unrelated SQLite files with an actionable error.
4. Devices, virtual groups, catalogs, folders, files, tags, checksums, and
   extracted media metadata import into the SkyCD catalog with stable
   source-derived node ids, so repeated imports of the same database produce
   identical mappings. The source database is never modified.

### Supported versions & schema handling
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

## Security and resource bounds
- The source stream is materialized to a temp copy for SQLite imports; folder
  and `.idx` sources are only ever read.
- Reads are cancellable and served from one consistent snapshot; uncommitted
  SQLite writer frames are never visible.
- Paths containing `..` traversal sequences are rejected instead of imported.

## Known loss of information
- **Statistics, search history, backup mappings, catalog filters, settings**:
  the `statistics_device`, `search`, `device_mapping`, and `catalog_filter`
  tables are not imported; the summary lists them as unsupported for SQLite,
  and the folder importer reports the equivalent unsupported features.
- **Thumbnails and extracted preview images**: not imported in either mode.
- **Upstream-only file columns** without a shared-model slot
  (`file_catalog` catalog name beyond the fallback, `video_framerate`,
  `video_bitrate`) are documented rather than imported.
- Storage totals/free space have no SQLite column and import as zero; device
  totals carry the sizes instead.
