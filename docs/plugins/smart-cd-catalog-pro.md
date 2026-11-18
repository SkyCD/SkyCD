# Smart CD Catalog PRO (.scdc) Import Plugin

## Overview
The `smart-cd-catalog-pro` plugin provides import-only support for Smart CD
Catalog PRO collection databases (`.scdc`). The upstream application stores
its catalog as a renamed Microsoft Jet 4 (Access 2000 / `.mdb`) database, so
the importer reads it directly through a managed Jet engine - no external
executable, COM registration, Access Database Engine, or OLE DB provider is
required.

Detection is content-aware: a file is claimed only when it carries the Jet 4
container signature **and** exposes the Smart CD Catalog PRO core schema
(`CDs`, `Paths`, `Elements`). The `.scdc` extension is advertised for file
dialogs, but an unrelated Jet database or a truncated file is rejected with
an actionable message instead of being imported. Writing Smart CD Catalog
PRO files is out of scope.

## User Import Instructions in SkyCD
1. In SkyCD, choose **File -> Open...** and select the `.scdc` catalog.
2. Smart CD Catalog PRO keeps the catalog single-user; close the application
   before importing so no other process holds the database open.
3. The plugin copies the selected file into a private temporary snapshot,
   verifies the schema, and imports media roots, folders, files, categories,
   boxes, borrowers, and loan history with stable source-derived node ids.
   The source file is never modified and no lock file (`.ldb`) or journal is
   created beside it.

## Supported Versions & Schema Handling
- **Container**: Jet 4 (`Standard Jet DB`, version `4.0`). The fixed
  database password used by Smart CD Catalog PRO is applied automatically;
  a catalog protected with a different password is rejected with a password
  diagnostic.
- **Required schema**: tables `CDs` (with `IDCD`, `Drive_Folder`), `Paths`
  (with `IDPATH`, `IDCD`, `Path`, `IsArchive`), and `Elements` (with `ID`,
  `IDPATH`, `ElementName`, `IsFolder`). Missing tables or columns produce an
  unsupported-schema error naming the missing item.
- **Optional schema**: `Categories`, `Boxes`, `Borrows`, `LoanHistory`,
  `AccessMode`, and all other optional columns are read only when present, so
  catalogs from older releases import cleanly.
- **Hierarchy**: media roots come from `CDs`; paths join their media root via
  `Paths.IDCD`; elements join paths via `Elements.IDPATH` and nested folders
  through `Elements.IDPATHSELF`. Cached denormalized fields (for example the
  serialized category path) are ignored in favor of id-based resolution.
  Duplicate ids, dangling references, and folder cycles are rejected.
- **Classifications**: categories and boxes resolve through their parent ids
  into `A / B` display paths. Borrowers import only by id and name; loan
  history imports dates, state, and comments.

## Security and Resource Bounds
- **Never imported or logged**: the application passwords (`FullPW`,
  `ReadOnlyPW`) and borrower contact fields (`Borrows`). Only booleans
  indicating that a password is configured are exposed.
- **No execution**: macros, VBA, queries, and expression columns in the
  database are never executed; only plain table rows are read.
- **Read-only snapshot**: the source stream is copied to a private temporary
  file capped at 2 GiB and deleted after the import. The original file is
  opened for read only.
- **Bounded workload**: row counts are capped per table (5,000,000 core and
  loan rows, 500,000 classification rows) and cancellation is honored per
  batch and per row.
- **Validated values**: non-finite, negative, or non-integral element sizes
  are omitted (reported as size `0`) and recorded as diagnostics instead of
  producing corrupt entries.

## Known Loss of Information
- **Cached counters** stored on media roots (`CachedFileCount`,
  `CachedFolderCount`, ...) are imported as recorded but are not recomputed;
  hierarchy comes from the normalized `Paths`/`Elements` tables.
- **Application settings** beyond the access-mode booleans (for example UI
  preferences or scan options) are not imported.
- **Borrower contact details** are intentionally dropped; only the borrower
  name and id are kept.
- **Other Jet objects** (queries, forms, modules, relationships) are ignored;
  only the documented tables are read.
