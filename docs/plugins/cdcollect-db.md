# CDcollect Database Catalog Import Plugin

## Overview
The `cdcollect-db` plugin provides import-only support for catalog databases (`*.db`, `*.cdcollect`) created by **CDcollect** (GNOME CD cataloging application, versions 0 through 4).

## Format Characteristics
- **Format**: SQLite 3 database catalog.
- **Structure**:
  - `cdcollect_version`: Contains catalog schema version (up to version 4).
  - `cds`: Table containing CD media items (id, name, keywords, year/month/day dates, icon).
  - `files_v1` (or legacy `files`): Table containing cataloged files and directories (cd_id, path, name, info, size, mime, location, type, packed).
  - `infoplugins` / `compplugins`: Catalog plugin definitions.
- **Security**: Opened in ReadOnly mode with safe SQLite parameterization and transactional queries.

## How to Import in SkyCD
1. In SkyCD, choose **File -> Open...** (or pass the file path via CLI).
2. Select your `.db` or `.cdcollect` catalog file.
3. Content-aware detection automatically verifies the SQLite header and CDcollect table schema.
4. Media roots, directory hierarchies, file sizes, mime types, comments, and extracted audio/video metadata are imported directly into the SkyCD catalog.

## Supported Metadata
- **Media items**: CD names, keywords/comments, release/creation dates (year/month/day), icons, volume ID.
- **Folders & Files**: Hierarchical directory structure, file sizes, MIME types, archive packed flag, location paths.
- **Plugin Metadata & Info**: Audio metadata (Artist, Album, Title, Bitrate, Duration), video metadata (Dimensions, VideoFormat, AudioFormat), and comments.

## Supported Versions
- CDcollect version 4 (standard `files_v1` schema) and legacy version 0 (`files` schema) are fully supported.
- Future versions beyond version 4 are handled gracefully in backward-compatibility mode with diagnostic notes.
