# WinCatalog CSV Report Import Plugin

## Overview
The `wincatalog-csv` plugin provides import-only support for CSV reports exported by **WinCatalog** (tested with WinCatalog 2024 through 2026 Professional).

## User Export Instructions in WinCatalog
To generate an export compatible with SkyCD:
1. Open WinCatalog and open your catalog file.
2. Click the **Catalog** application menu button (top-left, next to the Ribbon toolbar) and select **Reports** (or click **Reports** on the Ribbon).
3. Under **Report type**, choose **CSV (Comma-Separated Values)**.
4. Under **Data source**:
   - Choose **Custom Set of Folders** to export the entire catalog or selected media/folders.
   - Or choose **Search Results** to export a filtered subset.
5. In **Columns** (the gear icon / column selection dialog):
   - **Essential columns**: Ensure that either **Catalog Path** (or **File Path**) and **Name** (or **Filename**) are selected. Hierarchy cannot be reconstructed if path information is omitted.
   - **Recommended columns**: Include **Disk** (or **Volume Label**) so storage media roots are recovered cleanly.
   - **Supported metadata columns**: **Size**, **Type**, **Date Modified**, **Date Created**, **Comments**, **Tags**, **Rating**, **MD5**, **SHA256**, **CRC32**, **Blake2**, **Physical Location**.
6. Delimiter & Number Settings:
   - Delimiters: Comma (`,`), Semicolon (`;`), and Tab (`\t`) are supported automatically.
   - Encodings: UTF-8 (with or without BOM) and UTF-16 are supported.
   - Size units: Raw bytes, formatted numbers with thousand separators (e.g., `1,048,576`), or unit strings (e.g., `12.5 MB` or European `12,5 MB`) are supported.
7. Click **Save Report** and save the file with the `.csv` extension.

## User Import Instructions in SkyCD
1. In SkyCD, choose **File -> Open...** (or run `skycd open <path.csv>` via CLI).
2. Select your exported `.csv` file.
3. Content-aware detection automatically recognizes the WinCatalog report schema without confusing it with generic CSV files.
4. The media roots, directory hierarchy, files, sizes, and metadata are imported into the SkyCD catalog.

## Supported Versions & Locales
- **Software**: WinCatalog 2024, 2025, 2026 Professional.
- **Languages**: English, German (`Katalogpfad`, `Datenträger`), Russian (`Путь в каталоге`, `Диск`), French (`Chemin du catalogue`), Spanish (`Ruta del catálogo`), Italian (`Percorso catalogo`).
- **Encodings**: UTF-8 with BOM, standard UTF-8, ANSI/Windows-1252.

## Known Loss of Information
- **Thumbnails and Icons**: WinCatalog does not embed thumbnail images or icon binary data into CSV reports; these cannot be imported.
- **Fields Absent from Source**: Only fields included during the WinCatalog export can be recovered. Omitted columns will not have metadata in SkyCD.
- **Search Result Exports**: In search-result exports without explicit folder rows, intermediate folder structures are automatically synthesized from paths.
