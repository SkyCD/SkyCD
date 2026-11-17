# NeoFinder text export import

The `neofinder-text` plugin imports NeoFinder configurable text exports without
requiring macOS or NeoFinder at import time. Point it at a **Text with Tab**
export with headers included.

In NeoFinder, select one or more catalogs in the Library and use File -> Export:

1. Format: **Text with Tab**.
2. Check **include the names of the column headers**.
3. Levels: **All** (top levels only cannot reproduce the full hierarchy).
4. Under Include Fields keep **Name** (always included) plus at least one of
   **Where**, **Volume** or **Catalog** so SkyCD can rebuild the hierarchy.
   Also include Kind, Size, dates and Comments for full metadata.

In SkyCD, open the resulting `.txt` or `.tsv` file with **File -> Open**.

## Column mapping

Columns are matched by header against a built-in alias table (English export
headers plus best-effort localized variants); columns may be reordered and
unknown columns are listed in `UnsupportedFields` without failing the import.
Two different headers mapping to the same field, a missing Name column, or a
headerless file is rejected as ambiguous with an actionable message — enable
headers and check the Include Fields selection instead of guessing.

Supported metadata: kind, size, volume, catalog, created/modified dates,
comments and label. `DateCreated`/`DateModified` are normalized to
`yyyy-MM-dd HH:mm:ss` across the exporter's locale-dependent date formats;
unparseable dates are preserved verbatim.

## Paths, volumes and partial reports

POSIX (`/`) and classic Mac OS (`:`) separators are recognised, names are
Unicode-normalized, and a leading volume/catalog segment names the media root
instead of a subfolder. Exports listing several catalogs are grouped under one
media root per volume/catalog. Rows without any path, volume or catalog entry
are imported flat under a generic media root with a warning.

Missing intermediate folders derived from paths mark the report partial, as do
skipped malformed rows; both are itemized in the import summary next to counts
and unsupported fields. Sizes accept byte counts and unit suffixes (including
European comma decimals); folders and the em-dash placeholder count as zero.

## Encoding profiles

Recognition and import use the same strict decoding policy:

1. A BOM selects UTF-8 or UTF-16 LE/BE. Malformed data under that declared encoding is rejected.
2. Without a BOM, an alternating NUL pattern in the leading ASCII header identifies UTF-16 LE/BE.
3. Otherwise valid UTF-8 takes precedence.
4. If UTF-8 is invalid, Windows-1252 is accepted when none of its undefined bytes occur.
5. Anything else is rejected. No lossy replacement-character decoding is used.

`DetectedEncoding`, `DetectedDelimiter` and `DetectedColumns` are recorded in
the import summary. The parser never touches the filesystem: thumbnail
references or external paths in the export are treated as plain text.

## What is not imported

Indented Text exports (human-readable tree dumps without columns) and the XML
export are not supported. Embedded file contents, icons and thumbnails are not
imported. The format is read-only. Native NeoFinder catalogs are a separate
investigation.
