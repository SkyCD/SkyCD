# DiskCatalogMaker text-export fixtures

All fixtures in this directory are synthetic test data authored for this repository
and distributed under its license. They are **not** redistributed vendor exports and
are not evidence of tested DiskCatalogMaker versions.

## Provenance

No redistributable real DiskCatalogMaker export could be located: the format
reference ([official export instructions](https://diskcatalogmaker.com/faq/exporttext.html))
describes the export route (list-view attributes plus "Export Text…", with the
**Path** attribute enabled) but publishes no sample file, and no sample export
found online carries redistribution rights. The fixtures below are modeled on:

- the official FAQ (attribute list comes from the list view; attribute order is
  user-configurable with Name fixed first; the Path attribute must be checked),
- the CDFinder/NeoFinder import guide (consumes DiskCatalogMaker output as
  **Tab Delimited Text (.txt)**),
- a 2008 field report of a DiskCatalogMaker text export (tab-delimited fields
  `Name`, `Size`, `Type`, `Creator`, `Date Modified`).

Tested with this plugin: tab (`.txt`), comma and semicolon delimiters; UTF-8 with
and without BOM, UTF-16 LE/BE with and without BOM, and Windows-1252. macOS
POSIX paths (`/Volumes/<Disk>/…`), bare relative paths, and classic HFS
colon-separated paths (`Disk:Folder:File`) are covered. Slash-separated dates
parse month-first (en-US); dot-separated dates parse day-first (de-DE); other
values fall back to the invariant culture.

## Fixtures

| File | Coverage |
| --- | --- |
| `diskcatalogmaker-standard-en.txt` | UTF-8, tab, two disks, Unicode names, comma/tab in quoted names, duplicate names across disks, creator/type codes, comments, labels, unit sizes |
| `diskcatalogmaker-reordered-columns.txt` | Comma delimiter, reordered header, missing optional attributes, explicit folder row |
| `diskcatalogmaker-hfs-paths.txt` | HFS colon paths, relative paths with an explicit Disk column, two media roots |
| `diskcatalogmaker-localized-de.txt` | Semicolon delimiter, German header aliases, comma-decimal sizes, dotted dates, duplicate names in distinct folders |
| `diskcatalogmaker-partial-invalid.txt` | Synthesized folders, skipped rows (empty path, short row), unsupported `Thumbnail` column |
| `diskcatalogmaker-missing-path.txt` | Missing Path column diagnostic |

UTF-16, BOM variants, malformed declared encodings and nonzero stream positions
are also exercised directly in the encoding regression tests.
