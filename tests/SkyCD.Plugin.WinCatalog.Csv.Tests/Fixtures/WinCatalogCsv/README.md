# WinCatalog CSV Report Fixtures

These fixtures represent synthetic and sanitized redistributable reports exported from **WinCatalog 2024 / 2026 Professional** via the **Reports** feature (**Catalog -> Reports -> CSV Report**).

## Export Configuration & Profiles

### 1. `wincatalog-standard-en.csv`
- **Application**: WinCatalog 2026 Professional (v26.1)
- **Language**: English
- **Data Source**: Custom Set of Folders (Complete export)
- **Delimiter**: Comma (`,`)
- **Encoding**: UTF-8 without BOM
- **Columns**: `Disk,Catalog Path,Name,Size,Type,Modified,Comments,Tags,Rating,MD5,SHA256`
- **Features Tested**:
  - Multiple storage media (`Archive Disk 1`, `Archive Disk 2`)
  - Duplicate filenames in distinct directories and distinct media (e.g., `readme.txt`)
  - Quoted fields with embedded commas, quotes, and multiline comments
  - Checksums (MD5, SHA256) and star ratings (1-5)

### 2. `wincatalog-localized-de.csv`
- **Application**: WinCatalog 2024 Professional
- **Language**: German (Deutsch)
- **Data Source**: Custom Set of Folders
- **Delimiter**: Semicolon (`;`)
- **Encoding**: UTF-8 without BOM
- **Columns**: `Datenträger;Katalogpfad;Dateiname;Dateigröße;Dateityp;Änderungsdatum;Kommentare;Schlagwörter;Bewertung;Prüfsumme MD5`
- **Features Tested**:
  - Localized German header names
  - Semicolon separator with European decimal separators (e.g. `12,5 MB`, `1.048.576`)
  - Multiple folders and duplicate file titles

### 3. `wincatalog-filepath-partial.csv`
- **Application**: WinCatalog 2026 Professional
- **Language**: English
- **Data Source**: Search Results (Partial subset export)
- **Delimiter**: Comma (`,`)
- **Encoding**: UTF-8
- **Columns**: `File Path,Filename,Size,Modified,Disk`
- **Features Tested**:
  - Physical file paths (`D:\...`, `\\StorageServer\Shares\...`)
  - Subset export where intermediate directories are missing from the row list and must be synthesized
  - Identification of partial report status

### 4. `wincatalog-malformed-rows.csv`
- **Application**: WinCatalog 2026 test suite
- **Features Tested**:
  - Valid rows mixed with malformed records (missing paths, corrupt delimiter balance)
  - Verification that valid rows are imported while malformed rows are counted and recorded in summary

### 5. `wincatalog-missing-essential-path.csv`
- **Application**: WinCatalog 2026 test suite
- **Columns**: `Name,Size,Type,Modified,Comments`
- **Features Tested**:
  - Rejection with descriptive `WinCatalogMissingPathColumnException` when neither `Catalog Path` nor `File Path` is present

### 6. `wincatalog-utf8-bom.csv`
- **Application**: WinCatalog 2026 Professional
- **Language**: Russian (Русский)
- **Encoding**: UTF-8 with Byte Order Mark (BOM)
- **Columns**: `Диск,Путь в каталоге,Имя,Размер,Изменен,Комментарии`
- **Features Tested**:
  - BOM handling and Unicode Cyrillic filenames and directory names

## Hierarchy & Media Recovery Rules
1. **Media Identification**: Resolved from the `Disk` / `Volume Label` column when present. When absent, resolved from the root level of `Catalog Path` (e.g. `Catalog\<MediaName>\...`) or drive letter / UNC share of `File Path`.
2. **Directory & File Hierarchy**: Derived from path segments. WinCatalog escape codes (`{@slash}`, `{@dslash}`, `{@semicolon}`) are converted to real characters.
3. **Loss of Information**: Thumbnails and embedded file contents are not stored in CSV reports and cannot be imported. Unsupported custom user fields are recorded in `UnsupportedFields` summary.
