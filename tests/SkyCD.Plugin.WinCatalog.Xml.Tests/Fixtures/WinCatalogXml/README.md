# WinCatalog XML Report Fixtures

These fixtures are synthetic, sanitized, redistributable report documents shaped after the **XML report** export route of **WinCatalog** (`Catalog -> Reports -> XML Report`, a WinCatalog **Professional** feature; see the [official export documentation](https://www.wincatalog.com/help/2026/generating-reports-printing-and-exporting-data/)). The official documentation establishes the export route, not a complete parser specification, so the parser is content-vocabulary based: it recognizes WinCatalog reports by their element/attribute vocabulary (media, folder, file items; name/path/size/date/comment/tag/rating/hash fields; `{@slash}` escape tokens) rather than by an exact vendor-published schema.

## Export Configuration & Profiles

### 1. `wincatalog-nested-en.xml`
- **Application**: WinCatalog 2026 Professional (v26.1, recorded in the `generator` attribute)
- **Language**: English (schema tags; data in English)
- **Data Source**: Custom Set of Folders (complete media snapshot)
- **Encoding**: UTF-8 (no BOM)
- **Shape**: Root `WinCatalog` (`version`, `generator` attributes), `Disks` wrapper, nested `Disk` -> `Folder` -> `File` elements; fields carried as XML attributes (`Name`, `VolumeLabel`, `Size`, `Modified`, `Created`, `Comments`, `Tags`, `Rating`, `MD5`, `SHA256`, `CRC32`).
- **Features Tested**:
  - Multiple storage media (`Archive Disk 1`, `Archive Disk 2`)
  - Nested folder hierarchy reconstructed from element nesting
  - Unit sizes (`700 MB`, `1.5 MB`), multiline comments, checksums, ratings

### 2. `wincatalog-nested-ru.xml`
- **Application**: WinCatalog 2024 Professional
- **Language**: Russian (schema tags are English; data is Unicode Cyrillic)
- **Encoding**: UTF-8 **with BOM**
- **Features Tested**:
  - BOM handling and Unicode Cyrillic media, folder, and file names
  - European size format (`12,5 MB`) and `dd.MM.yyyy` dates (`15.02.2025 18:30:00`)

### 3. `wincatalog-flat-search-results.xml`
- **Application**: WinCatalog 2026 Professional
- **Data Source**: Search Results (partial subset export)
- **Shape**: Flat `Results` wrapper with `Item` elements carrying `Disk`, `CatalogPath`/`FilePath`, `Name`/`Filename`, `Size`, `Type`, `Modified`.
- **Features Tested**:
  - Flat (non-nested) reports routed through the shared WinCatalog CSV hierarchy logic
  - Synthesized intermediate folders and media roots; report flagged as partial

### 4. `wincatalog-empty-fields.xml`
- **Application**: WinCatalog 2026 test suite
- **Features Tested**: Empty and missing attribute values import cleanly (sizes fall back to 0, empty comments are dropped).

### 5. `wincatalog-malformed.xml`
- **Application**: WinCatalog 2026 test suite
- **Features Tested**: Unclosed elements are rejected with a malformed-document diagnostic (including line/position).

### 6. `wincatalog-dtd-entity.xml`
- **Application**: WinCatalog 2026 security test suite
- **Features Tested**: DTD and external entity resolution are prohibited; the file is rejected.

## Hierarchy & Media Recovery Rules
1. **Nested exports**: media/folder/file elements reconstruct the tree directly; folder-less items are attached to a media root synthesized from their `Disk` field (or `WinCatalog Report`), which marks the report as partial.
2. **Flat exports**: items with `Catalog Path`/`File Path` fields are delegated to the shared WinCatalog CSV hierarchy builder, which synthesizes missing folders the same way as CSV reports.
3. **Provenance**: unmapped fields are recorded in the import summary; path fields are preserved as `WinCatalogPath`/`WinCatalogFilePath` properties and the detected schema version as `WinCatalogSchemaVersion`.
4. **Loss of Information**: thumbnails and icon binaries are not carried by WinCatalog XML reports and cannot be imported; only fields included in the export can be recovered. Missing exported entries are never interpreted as deletions.
