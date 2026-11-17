# WinCatalog CSV report fixtures

All fixtures except `wincatalog-real-export-2026-utf8.csv` are synthetic test data authored for this repository and distributed under its license. They are not evidence of vendor version or locale compatibility.

## Genuine export

`wincatalog-real-export-2026-utf8.csv` was captured using WinCatalog **2026.3.1.805**, English UI, on 2026-11-17. The contributor confirmed the version, UI language and permission to redistribute this sample under the repository license. The catalog contains purpose-created sample files, accented folder/file names and comments; it contains no bundled vendor catalog data or application code.

- Separator: comma; quoted header and fields.
- Encoding: UTF-8 without BOM, verified from the fixture bytes. The selected exporter encoding setting was not recorded.
- Selected columns: the complete header in the fixture is the authoritative list (Name, Comments, Type, Size, File path, Id, Tags associated, Created, Modified, Disk, disk details, Filename, Catalog path, Serial number, Physical location, checksums, ID3, Exif and media fields, and Sample Field).
- `Catalog path` describes the containing catalog folder. Folder rows use `File folder` and supply the folder name separately.
- Newlines in exported comments appear as literal `\n`; the importer preserves that text.
- CSV is a report interchange format, not proof of native catalog or CSV input support in WinCatalog.

## Synthetic fixtures

| File | Coverage |
| --- | --- |
| `wincatalog-standard-en.csv` | UTF-8, comma, multiple media, duplicate names and metadata |
| `wincatalog-localized-de.csv` | UTF-8, semicolon, German header aliases and number formats |
| `wincatalog-filepath-partial.csv` | File paths, synthesized directories, partial report |
| `wincatalog-malformed-rows.csv` | Skipped invalid rows and summary diagnostics |
| `wincatalog-missing-essential-path.csv` | Missing essential path column diagnostic |
| `wincatalog-utf8-bom.csv` | UTF-8 BOM and Cyrillic names |
| `wincatalog-ansi-1252.csv` | No-BOM Windows-1252 names, paths and comments |
| `wincatalog-invalid-encoding.csv` | Unsupported/invalid bytes |
| `wincatalog-quoted-fields.csv` | Quoted separators, quotes and multiline comments |
| `wincatalog-reordered-columns.csv` | Reordered headers and accented names |

UTF-16 LE/BE with and without BOM, malformed declared encodings and nonzero stream positions are also generated directly in encoding regression tests.
