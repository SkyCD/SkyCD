# WinCatalog XML Report Import Plugin

## Overview
The `wincatalog-xml` plugin provides import-only support for XML reports exported by **WinCatalog** (tested against the export route of WinCatalog 2024 through 2026 Professional; XML export is a [Professional feature](https://www.wincatalog.com/help/2026/generating-reports-printing-and-exporting-data/)).

The plugin recognizes WinCatalog XML reports by content, not by a vendor-published schema: it scores the document vocabulary (media/folder/file elements, catalog field names such as `Name`, `CatalogPath`, `Size`, `Modified`, `Comments`, `Tags`, `Rating`, checksum fields, and WinCatalog `{@slash}` escape tokens) and rejects other XML files, including SkyCD's own native XML catalog format. Native WinCatalog catalog files and exporting back to WinCatalog are out of scope.

## User Export Instructions in WinCatalog
To generate an export compatible with SkyCD:
1. Open WinCatalog and open your catalog file.
2. Click the **Catalog** application menu button (top-left, next to the Ribbon toolbar) and select **Reports** (or click **Reports** on the Ribbon).
3. Under **Report type**, choose **XML (for data exchange)** (Professional license).
4. Under **Data source**:
   - Choose **Custom Set of Folders** to export the entire catalog or selected media/folders (recommended: include the disks so media roots are explicit).
   - Or choose **Search Results** to export a filtered subset (the report will be imported as a partial report).
5. Configure the report columns so that name and path information (`Name`/`Filename`, `Catalog Path`/`File Path`) is included; without path or hierarchy information folders cannot be reconstructed.
6. Click **Save Report** and save the file with the `.xml` extension.

## User Import Instructions in SkyCD
1. In SkyCD, choose **File -> Open...** (or run `skycd open <path.xml>` via CLI).
2. Select your exported `.xml` file.
3. Content-aware detection automatically recognizes the WinCatalog XML report without confusing it with SkyCD XML catalogs or other XML files.
4. Media roots, folder hierarchy, files, sizes, dates, comments, tags, ratings, and checksums are imported into the SkyCD catalog. Source files are never modified.

## Supported Versions & Locales
- **Software**: WinCatalog 2019 and newer report exports (validated against the WinCatalog 2024–2026 export route).
- **Element/attribute vocabulary**: English plus localized field names shared with the WinCatalog CSV importer (German, French, Spanish, Italian, Russian).
- **Encodings**: UTF-8 (with or without BOM) and any encoding declared in the XML declaration.
- **Report shapes**:
  - *Nested exports*: `Disk` -> `Folder` -> `File` element hierarchies are imported directly.
  - *Flat exports* (e.g. search results): items carrying `Catalog Path`/`File Path` fields are imported through the shared WinCatalog CSV hierarchy logic; intermediate folders are synthesized and the report is flagged as partial.

## Security and Resource Bounds
- DTD processing and external entity resolution are disabled (`DtdProcessing.Prohibit`, no `XmlResolver`); DTD/entity attacks are rejected with diagnostics.
- Parsing resources are bounded (document size limit, maximum nesting depth of 256, maximum item count of 1,000,000); exceeded bounds produce descriptive errors.
- Unsupported schema versions (declared `version`/`generator` outside WinCatalog 2019–2035 or generic `x.y` versions) are rejected with a useful diagnostic.
- Malformed records are skipped and reported in the import summary instead of aborting the whole import.

## Known Loss of Information
- **Thumbnails and Icons**: WinCatalog XML reports do not carry thumbnail images or icon binary data; these cannot be imported.
- **Fields Absent from Source**: only fields included during the WinCatalog export can be recovered; unmapped fields are recorded in the import summary (`UnsupportedFields`) as diagnostics.
- **Partial Exports**: search-result exports without folder rows have their folder structure synthesized from paths; a report is marked partial when ancestors had to be synthesized or records were skipped. Missing exported entries are never interpreted as deleted files.
- **Provenance**: original paths are preserved as `WinCatalogPath`/`WinCatalogFilePath` properties and the detected report schema version as `WinCatalogSchemaVersion`.
