# DiskCatalogMaker v5 catalog import

The `diskcatalogmaker-v5` plugin imports native DiskCatalogMaker catalogs:

- `.dcmf` — single-file catalog.
- `.dcmd` — macOS package catalog (a directory containing `Catalog.dcm`).

The plugin is read-only and does not require DiskCatalogMaker to be installed.

> **Provenance.** The issue describes the v5 storage model (`TDCatalogFileV5*`,
> `TDVolume*TableV5`, a `Catalog.dcm` payload and a `Thumbnails/aa/bb/cc/dd.ext`
> object store) but no public byte-level specification or redistributable sample
> exists. The byte layout below is the layout implemented by this plugin and
> exercised by its synthetic fixtures. It is documented so future parser revisions
> have a stable reference; interoperability with a specific vendor build has not
> been verified against a real catalog.

## Package layout

```text
<catalog>.dcmd/
├── Catalog.dcm          # required v5 catalog payload
├── Thumbnails/          # optional thumbnail object store
│   └── aa/bb/cc/dd.ext
└── QuickLook/           # optional Finder/Quick Look artifacts (ignored)
```

- A directory is recognized only when `Catalog.dcm` exists and starts with a
  structurally valid v5 header. A missing or invalid `Catalog.dcm` makes the
  package invalid.
- Absent `Thumbnails/` and `QuickLook/` are tolerated.
- Thumbnail object bytes are not imported. The catalog keeps the package-relative
  `Thumbnails/...` path and, when the object exists, reports
  `ThumbnailResolved = "true"`.
- A regular file named `Catalog.dcm` is read as a catalog and its parent directory
  is treated as the package root for thumbnail resolution.

Legacy `.dcm` catalogs, Toast `.toastcat` catalogs and the `.dcmexr` metadata
sidecar are intentionally not claimed by this plugin. In particular, `.dcm` is not
matched by extension, so DICOM files are never routed here.

## Recognition

- **File:** extension `.dcmf`/`.dcmd` plus content validation. `CanHandleContent`
  checks the 32-byte header (magic, header size, little-endian marker) from the
  current stream position and restores the position. Detection does not rely on the
  extension alone: the same header is accepted regardless of file name.
- **Folder:** only when `Catalog.dcm` exists and passes the same header check.
  Folder routing happens before extension matching in the host.

## Container layout (all integers little-endian)

### Header — 32 bytes

| Offset | Size | Field | Value / notes |
| --- | --- | --- | --- |
| 0x00 | 4 | Magic | ASCII `DCM5` |
| 0x04 | 2 | Header size | `32` |
| 0x06 | 2 | Major version | `5` (other values produce a version-specific error) |
| 0x08 | 2 | Minor version | accepted as-is and reported only in errors |
| 0x0A | 2 | Byte-order marker | `0x4C45` = ASCII `LE` |
| 0x0C | 4 | Catalog revision | preserved in the import summary |
| 0x10 | 4 | Table directory offset | must be ≥ 32 and within the payload |
| 0x14 | 4 | Table count | ≤ 4096 |
| 0x18 | 4 | Flags | read and ignored |
| 0x1C | 4 | Header checksum | read and ignored |

### Table directory entry — 20 bytes

| Offset | Size | Field | Notes |
| --- | --- | --- | --- |
| +0x00 | 4 | Table ID | must be unique |
| +0x04 | 4 | Table offset | absolute payload offset |
| +0x08 | 4 | Table length | includes the 16-byte table header |
| +0x0C | 4 | Record count | ≤ 5,000,000 |
| +0x10 | 4 | Flags | read and ignored |

The header, directory and every table range must be disjoint. Overlap,
out-of-range, duplicate-ID and truncated ranges produce
`DiskCatalogMakerV5CorruptCatalogException` with the offending offset/length.

### Table header — 16 bytes

| Offset | Size | Field | Notes |
| --- | --- | --- | --- |
| +0x00 | 4 | Table ID | must equal the directory ID |
| +0x04 | 4 | Header value | known tables require `1` |
| +0x08 | 4 | Record count | must equal the directory count |
| +0x0C | 4 | Payload length | must equal `length - 16` |

### Table IDs

| ID | Table | Record layout |
| --- | --- | --- |
| 1 | Volume data | [Volume record](#volume-record--56-bytes) |
| 2 | Volume directory | [Item record](#item-record--80-bytes) |
| 3 | Volume name strings | [String record](#string-record) |
| 4 | Volume aliases | [Alias record](#alias-record--64-fixed-bytes--extra) |
| 5 | Volume thumbnails | [Thumbnail record](#thumbnail-record--16-bytes) |
| 6 | Volume mutable strings | Same [string record](#string-record) layout as table 3 |

Any other table ID is preserved as opaque bytes and reported as a warning; it
never invalidates the rest of the catalog.

### Volume record — 56 bytes

| Offset | Size | Field | Notes |
| --- | --- | --- | --- |
| +0x00 | 4 | Volume ID | unique |
| +0x04 | 4 | Name string ID | required |
| +0x08 | 4 | Source string ID | optional |
| +0x0C | 4 | Reserved | ignored |
| +0x10 | 8 | Capacity bytes | |
| +0x18 | 8 | Free bytes | |
| +0x20 | 8 | Created (Unix seconds) | ≤ 0 means absent |
| +0x28 | 8 | Scanned (Unix seconds) | ≤ 0 means absent |
| +0x30 | 2 | Volume kind | 1 Disk, 2 CD, 3 DVD, 4 Network, 5 Folder, 6 Disk image |
| +0x32 | 2 | File system | 1 HFS, 2 HFS+, 3 APFS, 4 FAT, 5 NTFS, 6 UDF, 7 ISO 9660, 8 Network |
| +0x34 | 2 | Flags | ignored |
| +0x36 | 2 | Flags 2 | ignored |

The payload must be exactly `recordCount × 56` bytes.

### Item record — 80 bytes

| Offset | Size | Field | Notes |
| --- | --- | --- | --- |
| +0x00 | 4 | Item ID | unique per volume |
| +0x04 | 4 | Parent item ID | `0xFFFFFFFF` = directly under the volume root |
| +0x08 | 4 | Volume ID | |
| +0x0C | 4 | Name string ID | required |
| +0x10 | 4 | Full path string ID | optional; overrides the synthesized path |
| +0x14 | 4 | Comment string ID | optional |
| +0x18 | 4 | Alias target item ID | optional (`0` = none) |
| +0x1C | 4 | Thumbnail key | optional (`0` = none) |
| +0x20 | 8 | Size bytes | |
| +0x28 | 8 | Created (Unix seconds) | present only when flag bit 0 is set |
| +0x30 | 8 | Modified (Unix seconds) | present only when flag bit 1 is set |
| +0x38 | 8 | Scanned (Unix seconds) | present only when flag bit 2 is set |
| +0x40 | 4 | Type code | FourCC, big-endian byte order |
| +0x44 | 4 | Creator code | FourCC, big-endian byte order |
| +0x48 | 2 | Kind | 0 Directory, 1 File, 2 Package, 3 Alias, 4 Archive member |
| +0x4A | 2 | Flags | bit 0 created, bit 1 modified, bit 2 scanned, bit 3 label |
| +0x4C | 2 | POSIX mode | `0` means absent; otherwise rendered as octal `0NNN` |
| +0x4E | 2 | Label | 0–7 |

The payload must be exactly `recordCount × 80` bytes. Directory and package items
become `folder` rows; aliases and archive members become `file` rows. Missing
parents and parent cycles attach the item to the volume root and add a warning.

### String record

| Offset | Size | Field | Notes |
| --- | --- | --- | --- |
| +0x00 | 4 | String ID | `0` is reserved; duplicates keep the first value |
| +0x04 | 4 | Byte length | ≤ 4 MiB |
| +0x08 | 2 | Encoding | 0 UTF-8 (strict), 1 UTF-16LE (strict) |
| +0x0A | 2 | Flags | ignored |
| +0x0C | n | Bytes | no null-terminator is required; trailing NULs are trimmed |

A string table must be fully consumed by its declared records. Invalid or odd
UTF-16 byte sequences are rejected; decoding never substitutes replacement
characters.

### Alias record — 64 fixed bytes + extra

| Offset | Size | Field | Notes |
| --- | --- | --- | --- |
| +0x00 | 4 | Alias ID | |
| +0x04 | 4 | Item ID | first alias per item wins |
| +0x08 | 4 | Target volume ID | |
| +0x0C | 4 | Target directory ID | |
| +0x10 | 4 | Mount point string ID | |
| +0x14 | 4 | POSIX path string ID | |
| +0x18 | 4 | Server string ID | |
| +0x1C | 4 | User string ID | |
| +0x20 | 4 | Zone string ID | |
| +0x24 | 4 | Unicode file name string ID | |
| +0x28 | 4 | Unicode volume name string ID | |
| +0x2C | 8 | Volume created (Unix seconds) | |
| +0x34 | 8 | File created (Unix seconds) | |
| +0x3C | 4 | Extra length | |
| +0x40 | n | Extra bytes | ≤ 64 KiB retained |

Extra data larger than 64 KiB is skipped with a warning. An extra length that
points past the table stops alias parsing with a warning instead of failing the
whole catalog; already decoded aliases and all other tables remain available.

### Thumbnail record — 16 bytes

| Offset | Size | Field | Notes |
| --- | --- | --- | --- |
| +0x00 | 4 | Thumbnail key | duplicate keys keep the first record |
| +0x04 | 4 | Item ID | informational |
| +0x08 | 2 | Extension string ID | optional |
| +0x0A | 2 | Flags | ignored |
| +0x0C | 4 | Reserved | ignored |

The payload must be exactly `recordCount × 16` bytes.

Thumbnail object paths fan out most-significant byte first:

```text
key 0x0A0B0C0D + extension "png" -> Thumbnails/0a/0b/0c/0d.png
key 0x01020304, no extension      -> Thumbnails/01/02/03/04
```

Extensions are normalized to a leading dot and at most 16 ASCII letters/digits;
unusable extensions are omitted without dropping the reference.

## Sample offsets

`single-volume-unicode.dcmf` (841 bytes, three tables) demonstrates the layout:

| Offset | Content |
| --- | --- |
| `0x0000` | 32-byte header; directory at `0x0020`, 3 tables, revision 3 |
| `0x0020` | Directory: strings table `0x005C` (261 bytes, 10 records), volume table `0x0161` (72 bytes, 1 record), item table `0x01A9` (416 bytes, 5 records) |
| `0x005C` | String table header; first record at `0x006C` = ID 1, 12 bytes, UTF-8, `Archive Disk` |
| `0x0161` | Volume table header; record at `0x0171` |
| `0x01A9` | Item table header; records from `0x01B9` |

## Imported row mapping

Node IDs are `media:<volumeId>` and `item:<volumeId>:<itemId>`; they contain no
control characters and are stable across imports.

- **Media rows:** `VolumeId`, `Kind`, `FileSystem`, `CapacityBytes`, `FreeBytes`,
  `Source`, `Created`, `Scanned`.
- **Item rows:** `RecordId`, `CatalogPath`, `Kind`, `CatalogItemKind`, `Volume`,
  `TypeCode`, `Creator`, `Comments`, `Label`, `LabelName`, `PosixMode`, `Created`,
  `Modified`, `Scanned`, plus `Package`/`Alias`/`ArchiveMember` flags.
- **Aliases:** `AliasTargetItemId`, `AliasTargetPath`, `AliasTargetVolumeId`,
  `AliasTargetDirectoryId`, `AliasMountPoint`, `AliasPosixPath`, `AliasServer`,
  `AliasUser`, `AliasZone`, `AliasUnicodeName`, `AliasUnicodeVolumeName`,
  `AliasVolumeCreated`, `AliasFileCreated`, `AliasExtraBytes`.
- **Thumbnails:** `Thumbnail` (package-relative path) and, when a package root is
  known, `ThumbnailResolved` (`"true"`/`"false"`).
- Non-printable FourCC bytes render as `\xNN`; timestamps render as
  `yyyy-MM-dd HH:mm:ss` in invariant culture.
- Items that reference a volume without a volume record get a synthesized media
  node (`Volume <id>`) and a warning.

## Bounds, diagnostics and safety

- Maximum catalog payload: 512 MiB. Maximum tables: 4096. Maximum records per
  table: 5,000,000. Maximum string: 4 MiB. Maximum retained alias extra data:
  64 KiB.
- Every offset, length and count is validated with checked arithmetic before
  slicing or allocating; ranges must not overlap.
- Errors are format-specific:
  `DiskCatalogMakerV5CorruptCatalogException`,
  `DiskCatalogMakerV5UnsupportedVersionException`,
  `DiskCatalogMakerV5CatalogTooLargeException` and
  `DiskCatalogMakerV5MissingCatalogException`.
- Unknown tables are preserved as opaque bytes with a warning and do not discard
  otherwise valid records. Warning text is capped at 1000 entries.
- The parser only interprets bytes: catalog content is never executed, no code or
  external resource is loaded, and no DiskCatalogMaker installation is required.
- Source files are opened read-only. Cancellation and progress use the standard
  `FileFormatReadRequest` contract.

## Loss of information

- **Byte-layout verification:** no real v5 sample was available; see the
  provenance note above.
- **`.dcmexr` metadata sidecar:** not imported (independent optional follow-up).
- **Legacy `.dcm` and Toast `.toastcat`:** out of scope.
- **Thumbnail bytes and QuickLook artifacts:** referenced by path only; objects
  are not copied into the catalog database.
- **Unknown tables:** available in the in-memory import summary, not projected
  into catalog rows.
- **Checksums and header flags:** read but not verified.

## Fixtures and tests

Committed fixtures live in
[DiskCatalogMaker v5 fixture provenance](https://github.com/SkyCD/SkyCD.Plugin.FileFormat.DiskCatalogMaker/blob/main/tests/SkyCD.Plugin.FileFormat.DiskCatalogMaker.Tests/Fixtures/DiskCatalogMakerV5/README.md)
and are synthetic. They cover an empty catalog, nested Unicode names, zero-byte
and 5 TB files, type/creator codes, comments/labels/POSIX modes, aliases,
unknown tables, packages with and without thumbnails, multiple volumes, and
truncated/invalid/overlapping/unsupported containers. Regenerate them after a
layout change with `SKYCD_REGENERATE_DCM5_FIXTURES=1`; the consistency test
verifies the committed bytes match the generator.
