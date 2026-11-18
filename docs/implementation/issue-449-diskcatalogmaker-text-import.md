# Issue #449: DiskCatalogMaker text-export import

Fixes #449: Add DiskCatalogMaker text-export import plugin.

## Format evidence

- [Official export instructions](https://diskcatalogmaker.com/faq/exporttext.html):
  DiskCatalogMaker exports the attributes visible in the list view via “Export Text…”;
  the **Path** attribute must be enabled for full paths, its order follows the list
  view, and **Name is fixed first** (not movable).
- [CDFinder/NeoFinder import guide](https://www.cdfinder.de/guide/3/3.6/import.html):
  consumes DiskCatalogMaker output as **Tab Delimited Text (.txt)**.
- A 2008 field report of a DiskCatalogMaker text export lists tab-delimited fields
  `Name`, `Size`, `Type`, `Creator`, `Date Modified`.
- No redistributable real export could be located (no sample file ships with the
  docs, and no found sample carries redistribution rights), so the test suite uses
  synthetic fixtures modeled on the above and claims **no tested vendor version**.
  The [fixture README](../../tests/SkyCD.Plugin.DiskCatalogMaker.Tests/Fixtures/DiskCatalogMaker/README.md)
  records this explicitly.

## Design

New read-only plugin `diskcatalogmaker-text` (`.txt`, `.tsv`), following the
`wincatalog-csv` precedent: header-driven column mapping (`DiskCatalogMakerProfile`
with EN/DE/FR/ES/JA aliases), strict BOM-first encoding resolution
(UTF-8/UTF-16, Windows-1252 fallback, no lossy decoding), `CsvParser` delimiter
auto-detection (tab/comma/semicolon), and hierarchy reconstruction into the shared
flat node rows (`media`/`folder`/`file` with `nodeId`/`parentId`/`kind`/`name`/
`sizeBytes`/`properties`).

macOS specifics: `/Volumes/<Disk>/` prefix stripped for the media root, an explicit
`Disk`/`Volume` column wins when present, POSIX slash paths primary, HFS colon
paths supported when slashless, backslashes preserved (legal in macOS names).
Slash dates parse month-first (en-US), dotted dates day-first (de-DE) — the
separator-ordered rule adopted after the #577 dotted-date fix. Sizes accept raw
bytes and unit suffixes with either decimal convention.

## Recognition conflicts (neighbouring formats)

- `.csv` is deliberately **not** claimed, avoiding the WinCatalog/generic-CSV
  routing space (WinCatalog also maps `Path`+`Size`).
- `.txt`/`.tsv` is shared with Disk Explorer delimited text. Separation is by
  content: Name-must-be-first (per the official doc) plus a Path column plus one
  supporting attribute claims a file; WinCatalog-distinctive headers (catalog
  paths, checksums, tags, ratings) and Disk Explorer structural labels
  (`ITEMTYPE`, `FULLPATH`, `CATEGORY`, `DESC`, …) veto the match.
- Residual order-dependence: a DCM-shaped header also satisfies Disk Explorer's
  loose ≥2-label rule, so with both plugins installed a DCM file resolves to
  whichever candidate the `FileFormatManager` registration order consults first.
  DiskCatalogMaker never claims Disk Explorer's `VOLUME`-first shape (pinned by
  the `FileFormatManager_DisambiguatesDiskCatalogMakerFromDiskExplorer` test).

## Loss of information (documented)

Native catalogs (`.dcm`/`.dcmf`/`.dcmd`), thumbnails, and any attribute the user
did not include in the list view cannot be recovered. Absent attributes are
omitted from node properties (never fabricated); synthesized folders and skipped
rows mark the report partial with per-row reasons and `UnsupportedFields`.
Source files are only read; cancellation and progress flow through the standard
`FileFormatReadRequest` contract.

## Verification

New `SkyCD.Plugin.DiskCatalogMaker.Tests`: detection (metadata, generic-CSV/
arbitrary/WinCatalog/DiskExplorer negatives, Name-first rule, manager tie-break),
hierarchy (2 disks, Unicode, comma/tab in quoted names, duplicates, reordered and
missing attributes, HFS/relative paths, localized DE, partial/invalid rows,
missing-Path and empty-report diagnostics, path splitting, date rules), encoding
(UTF-8 ±BOM, UTF-16 LE/BE ±BOM, Windows-1252, stream-position preservation,
consistent rejection of malformed/undefined bytes), and integration (multi-disk
persist/reopen and accented-name round trips through offline Couchbase Lite).
