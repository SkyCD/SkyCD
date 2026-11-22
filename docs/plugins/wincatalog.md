# WinCatalog import

The `SkyCD.Plugin.FileFormat.WinCatalog` plugin imports WinCatalog data through three read-only
file-format capabilities that share one enable toggle in **Options → Plugins**:

| Format id | Extensions | Source |
| --- | --- | --- |
| `wincatalog-csv` | `.csv` | CSV report exports |
| `wincatalog-native` | `.w3cat`, `.wcat`, `.wca`, `.wcl` | Native catalog database; legacy profiles produce a diagnostic |
| `wincatalog-xml` | `.xml` | XML report exports |

All three are exported by the same vendor product, so they ship as one assembly. SkyCD opens the
right capability through **File → Open** or `skycd open <path>`; `.csv` and `.xml` are
disambiguated by content, and `.w3cat` is claimed only when the SQLite header and WinCatalog
markers match. Export back to WinCatalog is not supported.

The CSV and XML importers share the WinCatalog column vocabulary and the flat row hierarchy
builder. The native reader parses the SQLite catalog directly.

## CSV reports

A genuine English report from **WinCatalog 2026.3.1.805** is covered by the test suite. Other
version and locale support has not been established with genuine exports; localized header aliases
are exercised using synthetic fixtures.

In WinCatalog, create a CSV report using Reports. Include **Catalog path** or **File path** so SkyCD
can recover hierarchy. Include **Filename** or **Name**, and preferably **Disk** or **Volume
Label**. Exact UI labels may vary by version.

### Encoding profiles

Recognition and import use the same strict decoding policy:

1. A BOM selects UTF-8 or UTF-16 LE/BE. Malformed data under that declared encoding is rejected,
   without trying a different code page.
2. Without a BOM, an alternating NUL pattern in the leading ASCII header identifies UTF-16 LE/BE.
3. Otherwise valid UTF-8 takes precedence.
4. If UTF-8 is invalid, Windows-1252 is accepted only when none of its undefined bytes (81, 8D, 8F,
   90, 9D hexadecimal) occur.
5. UTF-32 BOMs and unsupported/invalid input are rejected. No lossy replacement-character decoding
   is used.

No-BOM code pages cannot be identified unambiguously. The fallback is specifically Windows-1252,
not a promise to support every Windows ANSI code page. Other legacy code pages must be converted
using their known source encoding. `DetectedEncoding` in the import summary records the selected
profile.

UTF-8 with/without BOM, UTF-16 LE/BE with/without BOM and Windows-1252 are tested. The genuine
export is UTF-8 without BOM; its exporter encoding setting was not recorded. See the
[fixture provenance](../../tests/SkyCD.Plugin.FileFormat.WinCatalog.Tests/Fixtures/WinCatalogCsv/README.md).

### Columns and partial reports

Comma, semicolon and tab delimiters are detected automatically. Columns may be reordered.
Supported metadata includes sizes, dates, comments, tags, ratings, individual checksum fields and
physical location; absent fields cannot be recovered. Unknown columns are listed in
`UnsupportedFields`.

A report without either path column fails with a descriptive missing-path error. Media roots come
from Disk/Volume Label where available, or catalog/physical paths. A report with no usable media
identity cannot reproduce the original media grouping; generic roots may be used. Include media
columns for reliable recovery.

Missing intermediate folders are synthesized. Such reports, and reports with skipped malformed
rows, are marked partial in the import summary. CSV does not carry thumbnails or embedded file
contents.

## XML reports

XML import is tested against the export route of WinCatalog 2024 through 2026 Professional; XML
export is a [Professional feature](https://www.wincatalog.com/help/2026/generating-reports-printing-and-exporting-data/).

The plugin recognizes WinCatalog XML reports by content, not by a vendor-published schema: it
scores the document vocabulary (media/folder/file elements, catalog field names such as `Name`,
`CatalogPath`, `Size`, `Modified`, `Comments`, `Tags`, `Rating`, checksum fields, and WinCatalog
`{@slash}` escape tokens) and rejects other XML files, including SkyCD's own native XML catalog.

### Exporting from WinCatalog

1. Open WinCatalog and open your catalog file.
2. Click the **Catalog** application menu button (top-left, next to the Ribbon toolbar) and select
   **Reports** (or click **Reports** on the Ribbon).
3. Under **Report type**, choose **XML (for data exchange)** (Professional license).
4. Under **Data source**:
   - Choose **Custom Set of Folders** to export the entire catalog or selected media/folders
     (recommended: include the disks so media roots are explicit).
   - Or choose **Search Results** to export a filtered subset (the report will be imported as a
     partial report).
5. Configure the report columns so that name and path information
   (`Name`/`Filename`, `Catalog Path`/`File Path`) is included; without path or hierarchy
   information folders cannot be reconstructed.
6. Click **Save Report** and save the file with the `.xml` extension.

### Supported reports

- **Software**: WinCatalog 2019 and newer report exports (validated against the WinCatalog
  2024–2026 export route).
- **Element/attribute vocabulary**: English plus localized field names shared with the CSV
  importer (German, French, Spanish, Italian, Russian).
- **Encodings**: UTF-8 (with or without BOM) and any encoding declared in the XML declaration.
- **Report shapes**:
  - *Nested exports*: `Disk` → `Folder` → `File` element hierarchies are imported directly.
  - *Flat exports* (e.g. search results): items carrying `Catalog Path`/`File Path` fields are
    imported through the shared flat hierarchy logic; intermediate folders are synthesized and
    the report is flagged as partial.

## Native catalogs

The native capability provides read-only import of native WinCatalog catalog databases
(`*.w3cat`). It detects the SQLite container plus WinCatalog-specific markers, so unrelated SQLite
files are never claimed.

Legacy collection profiles (`*.wcat`, `*.wca`, `*.wcl`) are **not** parsed. They produce an
actionable diagnostic directing users to convert to `.w3cat` inside WinCatalog or to export a CSV
or XML report.

### Supported versions and locales

- **Container**: SQLite (`SQLite format 3\0`) with WinCatalog catalog tables.
- **Schema handling**: column-tolerant reads; tables resembling catalog, disk, volume, folder,
  file, entry, media, or item tables are mapped. Tables without a name column are skipped with a
  diagnostic.
- **Versions**: an explicit version marker is recorded as provenance when present; unknown
  versions are imported best-effort or rejected with a diagnostic when no catalog tables exist.
- **Legacy profiles**: `.wcat` (WinCatalog 2003–2009 collections), `.wca`, and `.wcl` (WinCatalog
  Light collections) are rejected with a conversion diagnostic. No direct parsing is claimed until
  versioned fixtures validate it.
- Media roots, folder hierarchy, files, sizes, dates, comments, and tags are imported where
  present. Source files are never modified.

## Security and resource bounds

- DTD processing and external entity resolution are disabled for XML (`DtdProcessing.Prohibit`, no
  `XmlResolver`); DTD/entity attacks are rejected with diagnostics.
- XML parsing resources are bounded (document size limit, maximum nesting depth of 256, maximum
  item count of 1,000,000); exceeded bounds produce descriptive errors.
- Unsupported XML schema versions (declared `version`/`generator` outside WinCatalog 2019–2035 or
  generic `x.y` versions) are rejected with a useful diagnostic.
- Native sources are materialized to a temp snapshot and opened read-only with
  `PRAGMA query_only = ON`; sidecars (`-wal`/`-shm`/`-journal`) are not used. Reads are bounded
  (1,000,000 item cap, identifier truncation, 8 MiB blob cap, cancellation every 500 rows).
- Malformed records are skipped and reported in the import summary instead of aborting the whole
  import.

## Known loss of information

- **Thumbnails and icons**: not carried by CSV reports, XML reports, or the mapped native tables;
  they are not imported.
- **Search history, statistics, settings**: intentionally not imported.
- **Fields absent from source**: only exported/mapped fields are recovered; unmapped or missing
  fields are recorded as diagnostics.
- **Partial exports**: search-result exports without folder rows have their folder structure
  synthesized from paths; a report is marked partial when ancestors had to be synthesized or
  records were skipped. Missing exported entries are never interpreted as deleted files.
- **Provenance**: original paths are preserved as `WinCatalogPath`/`WinCatalogFilePath` properties,
  the detected XML report schema version as `WinCatalogSchemaVersion`, the native schema marker as
  `WinCatalogSchema`, and the native source table per row as `WinCatalogTable`.

## Physical location

Physical location fields are imported under the `PhysicalLocation` property key and canonicalized
into SkyCD's location model on the host side (issue #647). The raw `PhysicalLocation` value is
retained verbatim on each entry; the bare `Location` key is never used for this data, because other
importers reserve it for filesystem paths. A value that is present but blank (empty or
whitespace-only) cannot be mapped to a location and is reported in the import summary
`SkippedReasons` (e.g. `Row 3: blank Physical Location value was ignored.` or
`Item 'name': blank Physical Location value was ignored.`) instead of being silently dropped.
Entries that simply omit the field are silent.

Location export is unsupported: all three formats are read-only, so physical locations assigned in
SkyCD are never written back to WinCatalog data.

## Plugin identity and packaging

The plugin id is `SkyCD.Plugin.FileFormat.WinCatalog`; the former per-format ids
(`SkyCD.Plugin.WinCatalog.Csv`, `SkyCD.Plugin.WinCatalog.Native`, `SkyCD.Plugin.WinCatalog.Xml`)
are retired. Existing `PluginDocument` rows for the retired ids are kept but marked unavailable on
the next discovery pass, and the merged plugin is discovered with its default enabled state.

The merged assembly always references `Microsoft.Data.Sqlite` and
`SQLitePCLRaw.lib.e_sqlite3` because it hosts the native catalog reader. The SQLite native assets
are therefore present for every WinCatalog import, including installations that only open CSV or
XML reports. They were already shipped while the native importer was a separate plugin; the merged
plugin adds no new runtime dependency.
