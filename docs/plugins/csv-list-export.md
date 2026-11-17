# Catalog list CSV export (`skycd-csv-list`)

Export-only capability in `SkyCD.Plugin.Csv` that writes the current catalog list or
result set as delimiter-separated text. It never reads: `CanHandleContent` is not
implemented and `ReadAsync` reports write-only. Routing uses the explicit format id
`skycd-csv-list` (for example `convert --format skycd-csv-list`); the `.csv` extension
is shared with the `skycd-csv` round-trip format, which keeps its own fixed schema.

## Request

`CsvListExportRequest` carries rows plus configuration:

- `Rows`: the visible list/result order, streamed row by row and never fully buffered.
  Any `IEnumerable<Dictionary<string, object?>>` works, including lazily generated
  sequences, or a JSON array of objects (numbers kept as raw text until formatted).
- `Columns`: selected columns in exact export order. Unknown names export as empty
  fields. Default: `Name, Path, Kind, SizeBytes, Modified, Description, Disk`.
- `Separator`: one character (`,` default; `;` and tab supported; quotes and line
  breaks are rejected with `CsvListExportException`).
- `EncodingName`: `utf-8` (BOM, default), `utf-8-nobom`, or a Windows code page
  (`windows-1252`/`1252`, `windows-1250`, `windows-1251`).
- `IncludeHeader`: header row on by default, using the column names as given.
- `AllowFormulas`: raw-mode opt-out of injection protection (default off).

A bare row list or JSON array payload exports with all defaults.

## Columns and value rendering

Names resolve case-insensitively against row keys, with aliases for the canonical
set: `Name` (`name`), `Path` (`path`, `fullpath`, `relativepath`), `Kind` (`kind`,
`type`, `entrykind`), `SizeBytes` (`sizebytes`, `size`), `Modified` (`modified`,
`modifiedutc`, `lastmodified`, `date`), `Description` (`description`, `comment`,
`info`), `Disk` (`disk`, `media`, `medianame`). Any other requested name passes the
matching row value through as text, which exposes available plugin metadata columns.

`SizeBytes` renders culture-invariantly, including values beyond 4 GiB; non-numeric
input is preserved as text. `Modified` renders ISO-8601 (`o`); unzoned input is
treated as UTC and unparseable input is preserved as text. Nulls become empty fields;
a literal empty string writes as a quoted empty field (`""`), so readers can tell
them apart. Progress reports exact percentages when the row count is known and
monotonic pulses otherwise; cancellation is honored between rows.

## Text safety

Fields quote per RFC 4180 (separator, `"`, CR or LF trigger quoting, `"` doubles),
plus leading/trailing whitespace triggers quoting so padding survives. Line endings
are always CRLF. Values whose first non-whitespace character is `=`, `+`, `-` or `@`
are prefixed with `'` unless `AllowFormulas` is set; headers are quoted without the
prefix. Unknown encodings, separators, or non-object payloads fail the export with a
message instead of writing a partial file.

## Tests

`tests/SkyCD.Plugin.Csv.Tests/CsvListExportTests.cs` covers registration as
export-only, defaults (header, BOM, invariant sizes, ISO timestamps), column
selection and order, quoting, formula neutralization and raw mode, null/empty
distinction, all encodings, delimiters, >4 GiB sizes, Unicode and multiline fields,
unknown columns, JSON payloads, header opt-out, 5000-row streaming with progress and
cancellation.
