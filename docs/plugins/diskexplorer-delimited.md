# Disk Explorer Professional delimited-text profile (`disk-explorer-delimited`)

Read/write capability in `SkyCD.Plugin.DiskExplorer`, sub-profile next to the
XML (`disk-explorer-xml-report`) and HTML (`disk-explorer-html-report`)
profiles. Discovery uses assembly metadata and capability types; no
`plugin.json`. It covers the Disk Explorer Professional "Special Export"
delimited-text mode: one row per entry, user-ordered column labels, TAB /
comma / custom one-character separators.

## Import

- The first row must be a header with case-insensitive column labels.
  Recognized field labels: `NAME`, `FILES`, `FOLDERS`, `SIZE`, `DATE`,
  `TIME`, `TYPE`, `DESC`. Structural labels: `VOLUME`, `CATEGORY`, `PATH`
  (relative catalog path), `FULLPATH` (full catalog path), `ITEMTYPE` /
  `KIND` (Volume, CategoryFolder, Folder, Archive, File). Anything else is
  retained verbatim as `DiskExplorer.<LABEL>` extension metadata and reported
  once in the summary.
- Separator auto-detection covers comma, tab, semicolon, and pipe: exactly
  one candidate must fit the header and the first data row. Any other
  single-character separator is rejected as ambiguous (it cannot be told
  apart from data reliably). Ties are broken by file extension (`.tsv` /
  `.csv`); remaining ties fail with a diagnostic.
- Encoding: UTF-8 with BOM when present, valid UTF-8 otherwise, and
  Windows-1252 as the historical fallback. UTF-16 input is rejected
  explicitly. Detection needs a seekable stream.
- A double quote opens a quoted field only at field start; elsewhere it is
  literal, so historical unquoted rows parse identically. Quoted fields may
  span lines. Limits: 1M chars per field, 8M per row, 10M rows.
- Empty fields map to null (they are omitted; no catalog property
  distinguishes null from empty string).
- Hierarchy is rebuilt from explicit path columns. `ITEMTYPE` selects the
  kind; without it, volume-only rows become volumes, rows with aggregate
  counts become folders, everything else files. Folder rows may carry the
  parent path with the folder name in `NAME`. `CATEGORY` qualifies the path
  (`PATH/CATEGORY`). Missing intermediate folders and volumes are
  synthesized; entries that attach to synthesized nodes (or to no node) mark
  the import partial — no missing descendants are invented.
- Every non-empty row must carry the header's field count, else the import
  fails naming the row. A header with no data rows fails as an empty report.

## Export

- `DiskExplorerDelimitedExportRequest` carries the rows plus explicit
  options: ordered `Columns` (default `VOLUME PATH ITEMTYPE NAME SIZE DATE
  TIME TYPE DESC`), `Separator` (default comma), `Encoding` (UTF-8 with BOM
  by default; Windows-1252 compatibility mode forces CRLF), `NewLine`
  override, `GuardFormulas` (default on) and `RawCompatibility` (verbatim,
  for byte comparisons only).
- Unknown requested columns export as empty fields. Item kinds map as
  media to Volume, folder to Folder, everything else to File; category
  folders have no generic source and export as Folder.
- Formula guard prefixes fields starting with `=`, `+`, `-`, `@` with a
  single quote unless raw compatibility is set.

## Security and resource bounds

- Imports stream with bounded row/field memory; progress and cancellation
  are honored per row batch. Exports stream rows in encounter order.
- No external entities, no code execution, no formula evaluation on import.

## Known loss of information

- Thumbnail binaries are never present in delimited exports.
- Column subsets omit what they omit; re-importing an export keeps only the
  exported columns (unknown labels survive as extension metadata).
- Volume/category context beyond the exported columns is not reconstructed.
