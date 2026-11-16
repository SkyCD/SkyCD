# Legacy CDCollect SQLite 2 catalogs

The CDCollect assembly discovers `CdCollectSqlite2Plugin` as a separate, read-only file-format capability (`cdcollect-sqlite2`). It imports pre-0.6 `cds.db` catalogs without a SQLite 2 installation, shell commands, native SQLite 2 libraries, or additional packages. SQLite 3 catalogs continue to use `cdcollect-db` from the same assembly. The legacy capability is absent from save/export choices.

Detection requires the SQLite 2 signature, valid page/header metadata, and CDCollect's `cds` and `files`/`files_v1` column definitions. The `.db` extension alone is insufficient. Detection preserves stream position. Import also supports nonseekable streams and short reads.

The managed reader implements the standard SQLite 2.8 layout: 1024-byte pages, both native byte orders, table b-trees including internal-page records, linked overflow pages, 1/2/3-byte field indices, null/empty text, UTF-8 strings, and INTEGER PRIMARY KEY row IDs. SQLite storage formats 2–4 are accepted. SQL stored in `sqlite_master` is inspected only to identify column positions; it is never executed. The converter writes a private temporary SQLite 3 catalog using only fixed statements and parameterized values, then reuses SkyCD's CDCollect domain mapping. Input bytes are never changed. Stored triggers, views, indexes, and external-command plugin configurations are ignored.

Supported catalog schemas:

- Unversioned or explicit version-0 `files` catalogs, with `cds(id,name,keywords,year,month,day,icon)` and `files(cd_id,path,name,info,size,mime,type,packed)`.
- Version-4 `files_v1` catalogs stored in SQLite 2, with the additional `location` column.
- Columns can be reordered; unrelated columns are ignored. Column declarations must use the simple column layout used by CDCollect. Missing required columns, unsupported layouts, invalid version rows, and intermediate or unknown schema versions raise `CdCollectSqlite2VersionException` with the upstream migration/upgrade path. They are not guessed into a modern layout.

Media, folder/file hierarchy, dates, keywords, icons, exact information text, sizes, MIME types, and packed/archive markers use the same domain representation as SQLite 3 imports. When the original catalog path is available, the existing mapper can also import available thumbnail sidecars. Legacy executable-plugin configuration is not migrated. No SQLite 2 files are written by the plugin; use SQLite 3 export for new catalogs.

Malformed pages, invalid byte-order markers, overlapping/cyclic cells, reused/out-of-range pages, invalid record offsets, and cyclic/truncated overflow chains are rejected. Imports are bounded to 256 MiB of input, 16 MiB per record, 128 columns, one million records per table, and 64 b-tree levels; cancellation is checked during input and traversal. Non-UTF-8 catalogs are rejected rather than silently changing text. Restore a valid backup for corrupt files. For unsupported historical variants, follow CDCollect's documented SQLite 2.8 dump-to-SQLite 3 migration and its internal schema upgrade, then import the resulting `cds3.db`.

Format facts were independently derived from public-domain SQLite 2.8.17 (`btree.c`, `pager.h`, `vdbe.c`, `main.c`) and CDCollect's observed schemas. No GPL implementation was copied. The development-only SQLite executable is not shipped. Committed synthetic fixtures were created and integrity-checked with the official SQLite 2.8.17 CLI; their provenance is recorded alongside them. Tests cover actual binary database import and SkyCD persistence, not a CDCollect GUI run.

References:

- [SQLite 2.8.17 public-domain source](https://www.sqlite.org/sqlite-2.8.17.tar.gz)
- [SQLite 2.8.17 Windows CLI](https://www.sqlite.org/sqlite-2_8_17.zip), fixture generation only
- [CDCollect upstream source and migration FAQ](https://svn.code.sf.net/p/cdcollect/code/cdcollect/trunk/)
