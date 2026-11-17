# WinCatalog CSV report import

The `wincatalog-csv` plugin imports CSV reports. A genuine English report from **WinCatalog 2026.3.1.805** is covered by the test suite. Other version and locale support has not been established with genuine exports; localized header aliases are exercised using synthetic fixtures.

In WinCatalog, create a CSV report using Reports. Include **Catalog path** or **File path** so SkyCD can recover hierarchy. Include **Filename** or **Name**, and preferably **Disk** or **Volume Label**. Exact UI labels may vary by version. In SkyCD, open the resulting `.csv` file with **File -> Open**.

## Encoding profiles

Recognition and import use the same strict decoding policy:

1. A BOM selects UTF-8 or UTF-16 LE/BE. Malformed data under that declared encoding is rejected, without trying a different code page.
2. Without a BOM, an alternating NUL pattern in the leading ASCII header identifies UTF-16 LE/BE.
3. Otherwise valid UTF-8 takes precedence.
4. If UTF-8 is invalid, Windows-1252 is accepted only when none of its undefined bytes (81, 8D, 8F, 90, 9D hexadecimal) occur.
5. UTF-32 BOMs and unsupported/invalid input are rejected. No lossy replacement-character decoding is used.

No-BOM code pages cannot be identified unambiguously. The fallback is specifically Windows-1252, not a promise to support every Windows ANSI code page. Other legacy code pages must be converted using their known source encoding. `DetectedEncoding` in the import summary records the selected profile.

UTF-8 with/without BOM, UTF-16 LE/BE with/without BOM and Windows-1252 are tested. The genuine export is UTF-8 without BOM; its exporter encoding setting was not recorded. See the [fixture provenance](../../tests/SkyCD.Plugin.WinCatalog.Csv.Tests/Fixtures/WinCatalogCsv/README.md).

## Columns and partial reports

Comma, semicolon and tab delimiters are detected automatically. Columns may be reordered. Supported metadata includes sizes, dates, comments, tags, ratings, individual checksum fields and physical location; absent fields cannot be recovered. Unknown columns are listed in `UnsupportedFields`.

A report without either path column fails with a descriptive missing-path error. Media roots come from Disk/Volume Label where available, or catalog/physical paths. A report with no usable media identity cannot reproduce the original media grouping; generic roots may be used. Include media columns for reliable recovery.

Missing intermediate folders are synthesized. Such reports, and reports with skipped malformed rows, are marked partial in the import summary. CSV does not carry thumbnails or embedded file contents. This plugin is read-only and does not implement CSV export or native WinCatalog catalog import.
