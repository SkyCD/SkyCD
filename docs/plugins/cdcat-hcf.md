# CdCat HCF Catalog Import Plugin

## Overview
The `cdcat-hcf` plugin provides import-only support for Hyper Catalog Files (`*.hcf`) created by **CdCat** (tested up to version 2.1).

## Format Characteristics
- **Format**: GZip compressed XML catalog (or uncompressed XML with CdCat structure).
- **Structure**: Root `<datafile version="...">` or `<catalog>` containing `<media>` volumes, nested `<directory>` folders, and `<file>` elements.
- **Security**: DTD processing and external entity resolution are disabled. Decompression size limits (512 MB) are strictly enforced to prevent compression bomb attacks.

## How to Import in SkyCD
1. In SkyCD, choose **File -> Open...** (or pass the file path via CLI).
2. Select your `.hcf` (or `.xml`) catalog file.
3. Content-aware detection automatically identifies the CdCat catalog structure.
4. Media roots, directory hierarchies, file sizes, timestamps, comments, loan/borrow status, and audio/video/image metadata are imported directly into the SkyCD catalog.

## Supported Metadata
- **Media volumes**: Media number, drive type (CD, DVD, HardDisc, NetworkPlace, flashdrive, floppy), comments, borrow/loan records.
- **Audio tags**: MP3 tags (artist, album, title, year, genre, track, bitrate, duration) from `<mp3tag>`.
- **Multimedia comments**: Video codec/format, resolution/dimensions, framerate, duration, and audio stream properties parsed from structured comment strings.
- **Archive contents**: Archive file listings extracted from `<content>`.

## Supported Versions
- CdCat versions up to **2.1** are fully supported.
- Newer incompatible versions (such as 3.0+) are rejected with a clear diagnostic error message.
