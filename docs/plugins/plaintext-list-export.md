# Catalog list plain-text export (`skycd-txt-list`)

Export-only capability in `SkyCD.Plugin.FileFormat.PlainText` that writes the current catalog
list or result set as human-readable plain text, without external software.

## Layout

The default fixed-width layout is byte-deterministic for a given input:

- one header row with the column names as given (truncated with `…` when they
  exceed the width cap);
- one separator row of `-` repeated per column width;
- one row per item, CRLF line endings;
- a blank line between independently selected groups only when `GroupBy` is set.

The tab-delimited layout writes a header row plus one tab-separated row per item
with no padding or quoting.

Columns render in the exact requested order (default `Name, Path, Kind,
SizeBytes, Modified, Description, Disk`; unknown names resolve
case-insensitively through the documented alias map and export as empty fields).
Column width is the maximum of header and rendered values, capped by
`MaxColumnWidth` (default 120, range 2–10000); longer values truncate with `…`
(an ellipsis, one character). Embedded CR/LF/tab characters normalize to spaces.
`SizeBytes` right-aligns; all other columns left-align.

Groups stream in first-seen order of the `GroupBy` column value. When grouping
is disabled, no blank separator lines are ever written.

## Values

Sizes and timestamps are culture-invariant by default: sizes render as plain
or round-trip numbers (including values beyond 4 GiB), timestamps as ISO-8601
(`o`). Set `CultureName` (for example `de-DE`) for display-culture formatting
instead. Nulls become empty fields; a literal empty string writes as an empty
field too — plain text cannot distinguish them (unlike CSV, which quotes empty
strings). Unknown columns and unparseable values pass through as stored text.

## Encodings and safety

`utf-8` (with BOM, default), `utf-8-nobom`, or a Windows code page
(`windows-1252`/`1250`/`1251`). Widths are measured in characters over a bounded
first pass (`MaxWidthSampleRows` rows, re-enumerated sources measured exactly);
rows stream through with bounded memory, progress and cancellation. File targets
are written to a temporary sibling and atomically replaced after success; when
the host already holds the destination open, the completed temp file is streamed
through instead and the temp file is still cleaned up.

## Tests

`tests/SkyCD.Plugin.FileFormat.PlainText.Tests/PlainTextListExportTests.cs` covers
registration as export-only, golden layout assertions (Unicode, long/multiline
values, empty fields, >4 GiB sizes, both layouts, all encodings, display
cultures), exact column and result ordering, grouping, JSON payloads, header
opt-out, atomic file replacement without temp leftovers, large streaming
exports with progress, and cancellation.
