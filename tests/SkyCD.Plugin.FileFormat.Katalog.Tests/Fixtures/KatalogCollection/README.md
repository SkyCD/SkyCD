# Katalog Memory-Mode Collection Fixtures

These fixtures represent sanitized redistributable export samples structured according to the **Katalog** (Qt/C++) Memory database mode storage specification.

## Collection Layout & File Relations
In Memory mode, Katalog stores catalog databases as a coordinated multi-file collection folder:
1. `parameters.csv`: Key-value pairs of collection settings (including schema `version`, `imageFolderPath`).
2. `device.csv`: Tab-separated records of devices and virtual groups (`ID`, `Parent ID`, `Name`, `Type`, `ExternalID`, `Path`, `total_file_size`, `total_file_count`, `total_space`, `free_space`, `active`, `groupID`, `date updated`, `order`, `comment`). Free-text fields are escaped (`\\`, `\t`, `\r`, `\n`) when the `comment` column is present.
3. `storage.csv`: Physical media profiles (`ID`, `Name`, `Type`, `Location`, `Path`, `Label`, `FileSystem`, `Total`, `Free`, `Brand`, `Model`, `SerialNumber`, `BuildDate`, `ContentType`, `Container`, `Comment`, `PicturePath`, `UserId`).
4. `tags.csv`: Assigned tag definitions associated with file paths.
5. `<CatalogName>.folders.idx`: Tab-separated folder index records (`CatalogID`, `FolderPath`).
6. `<CatalogName>.idx`: Line-based catalog metadata headers (`<catalogSourcePath>`, `<catalogFileCount>`, `<catalogTotalFileSize>`, etc.) followed by tab-separated file records.

## Fixture Descriptions
- `parameters.csv`, `device.csv`, `storage.csv`, `tags.csv`, `Backup Disk 1.idx`, `Backup Disk 1.folders.idx`, `Photos 2026.idx`: Valid complete multi-file collection containing virtual groups, storage devices, tags, metadata, and checksums.
- `legacy-v26.idx`: Katalog legacy schema version (v2.6) with 3-column file records (`path`, `size`, `date`).
- `traversal-attack.idx`: Fixture with directory traversal attempt (`../../etc/passwd`) designed to verify cross-file reference validation.
