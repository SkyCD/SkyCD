# Archive Metadata Plugin

## Overview

`SkyCD.Plugin.Metadata.Archives` is the single metadata plugin for archive entries. During a path
scan the host offers every file to `ArchivesIndexPlugin` (an `IIndexWorkItemProvider`); when the file
name matches a supported archive, the plugin extracts its entries into a temporary directory and
returns them as work items so enabled metadata plugins can inspect the extracted files. It never
writes to or modifies an archive, and the plugin exposes no `IFileFormatPluginCapability`: opening an
archive as a catalog view is handled by the separate `SkyCD.Plugin.FileFormat.Tar` (TAR) plugin.

The assembly has one discovery entry with one `[assembly: PluginId]` value,
`SkyCD.Plugin.Metadata.Archives`, so the Plugin Manager shows one plugin and one enable toggle. The
plugin is discovered through assembly metadata and implemented capability types; no `plugin.json`
descriptor is used.

Tests live in `tests/SkyCD.Plugin.Metadata.Archives.Tests/`, with one folder per format and shared
fixtures under `Fixtures/`.

## Supported formats and routing

`ArchivesIndexPlugin` dispatches each file to a per-format reader in the same assembly. Matching is
extension-based and case-insensitive; the first match wins in this order:

| Order | Format | Names matched | Notes |
| --- | --- | --- | --- |
| 1 | TAR | `.tar`, `.tar.gz`, `.tgz` | Matched on `FileName` or `SourcePath`; gzip payloads are detected from the stream, so `.tar.gz` and `.tgz` use the TAR reader, not the gzip reader. |
| 2 | Gzip | `.gz`, `.gzip` | Explicitly excludes `.tar.gz` and `.tar.gzip`, which stay owned by TAR. |
| 3 | Bzip2 | `.bz2`, `.bz`, `.tbz`, `.tbz2` | Single-stream decompression, including concatenated members. |
| 4 | XZ | `.xz`, `.txz` | Single-stream decompression. |
| 5 | Zstandard | `.zst`, `.tzst` | Single-stream decompression, including concatenated frames. |
| 6 | ZIP | `.zip` | BCL-based reader. |
| 7 | 7-Zip | `.7z` | `SharpCompress` reader. |
| 8 | RAR | `.rar` | `SharpCompress` reader with explicit multi-volume detection. |
| 9 | LHA | `.lha`, `.lzh` | `Hst.Compression` decoder. |
| 10 | CAB | `.cab` | `RecursiveAI.OneArchive` reader for stored, MSZIP and LZX folders. |
| 11 | ARJ | `.arj` | `SharpCompress` reader. |
| 12 | ARC | `.arc` | `SharpCompress` reader for stored, packed, squeezed and crunched entries. |
| 13 | ACE | `.ace` | `SharpCompress` reader; stored entries only, verified against the entry CRC-32. |
| 14 | ZOO | `.zoo` | Custom reader with stored and LZD decoding. |

Files that match no format return an empty work-item set. The router passes one shared
`ArchiveIndexLimits` instance to every reader, and per-format state (for example ACE diagnostics) is
exposed once through `ArchivesIndexPlugin.LastDiagnostics` for the last call.

## Shared limits

Every reader is bounded by `ArchiveIndexLimits` (100,000 entries, 256 MiB per entry and 1 GiB total by
default). The constructor validates the values, and the host can pass a custom instance to
`ArchivesIndexPlugin`. Entries beyond the entry count or byte budget are skipped; a reader that
aborts on an exhausted budget leaves no extracted files behind. The same exception type,
`ArchiveEntryLimitExceededException`, signals a member that expands past its per-entry limit while it
is being copied.

## Shared path and name handling

- `PackageEntryPath` normalizes entry names to forward slashes and flags absolute, drive-qualified and
  traversal paths as unsafe before extraction. Every resolved target is re-checked to stay under the
  temporary root, so archive contents never escape it. TAR additionally rejects control characters,
  and RAR drops `.` segments while still rejecting `..` traversal.
- `ArchiveEntryName` derives the catalog name for single-stream formats from the storage file name with
  the compression suffix removed, falling back to `content`; gzip prefers a safe original name stored
  in the member header.
- Duplicate entry names keep the first extracted file where the underlying format allows duplicates.

## Shared extraction behavior

- Each call creates a unique `skycd-<format>-index-<guid>` temporary directory. It is deleted when the
  returned set is disposed, and also on damaged-archive rejection, failure or cancellation.
- Non-seekable sources are buffered to a hidden file inside that directory before reading when the
  format needs seeking.
- Damage, unsupported compression, encryption and missing multi-volume parts are reported with typed
  diagnostics instead of failing indexing. A damaged archive contributes no work items.

## Per-format notes

### TAR and gzip

TAR reads `.tar`, `.tar.gz` and `.tgz` with `System.Formats.Tar` plus `System.IO.Compression`, and
extracts regular files only; symbolic links, hard links, devices and directory entries are skipped.
TAR uses the control-character path rule. Gzip parses the RFC 1952 header directly, uses the header
name when it is safe, and otherwise derives the name from the storage file name.

### Bzip2, XZ and Zstandard

All three are single-stream formats that decode to one catalog entry. Bzip2 supports `BZh1`–`BZh9`
headers and concatenated streams; XZ and Zstandard validate stream/frame headers before decoding and
reject reserved or unsupported flags. XZ and Zstandard compute the entry size by decoding, because
their headers may omit it.

### ZIP and 7-Zip

ZIP uses the BCL `ZipArchive` with bounded copying; unsafe entry paths are skipped. 7-Zip uses the
`SharpCompress` reader.

### RAR

RAR supports RAR4 and RAR5. Multi-volume detection checks the main header volume flag, the end header
"next volume" flag, per-entry split flags and a companion volume file next to the source; a source
whose data continues in a missing volume is reported as `RAR_MULTIVOLUME` instead of listing
truncated data. Quick Open service headers that repeat file names as zero-length headers are skipped
so each file is listed exactly once.

### LHA and CAB

LHA supports header levels 0, 1 and 2 with `-lh0-` and `-lh1-`/`-lh4-` through `-lh7-` methods; entries
whose method has no decoder are listed but not extracted. CAB uses the `RecursiveAI.OneArchive` reader,
which decodes stored, MSZIP and LZX folders, including version 1.1 attribute prefixes and UTF-8 names.
Quantum folders and entries that continue into another cabinet are listed but reported as
`CAB_UNSUPPORTED`, and multi-volume cabinets return an empty set because their companion volumes are
not available during a scan.

### ARJ and ARC

ARJ uses `SharpCompress` for stored and compressed methods; methods whose original size exceeds the
ARJ algorithm's 128 KB window are listed but reported as unsupported at extraction.

ARC uses the `SharpCompress` `ArcReader` and decodes stored (methods 1 and 2), packed RLE90 (3),
squeezed (4) and crunched (5–8) entries. Methods the reader cannot decode — squashed (9), crushed
(10), distilled (11) and extended `0xFE` headers — are listed as `ARC_UNSUPPORTED`, and their raw
bytes are never written. ARC item sizes come from the extracted bytes, and ARC items report no
modification time.

### ACE

ACE uses the `SharpCompress` `AceReader` with ACE 2.0 block headers and extracts stored entries only.
Each extracted entry is checked against its declared size and the ACE CRC-32, so a truncated or
corrupted entry is reported as damaged instead of being contributed. Encrypted entries are reported
as `ACE_ENCRYPTED`, and compressed entries, multi-volume sets and undecodable blocks as
`ACE_UNSUPPORTED`; skipped entries contribute no work item. A damaged archive contributes no work
items and is reported as `ACE_DAMAGED`. `ArchivesIndexPlugin.LastDiagnostics` exposes the typed
reasons for the last call.

### ZOO

ZOO supports archive formats 1.0 through 2.10 with stored and LZD members. LZH members (method 2) are
listed but not extracted. The LZD decoder validates every code and never writes past the declared
member size.

## Dependencies and licensing

- `SharpCompress` 0.50.8 (MIT) decodes 7-Zip, RAR, ARJ, ARC, ACE, bzip2, XZ and Zstandard.
- `Hst.Compression` 1.0.104 (MIT) and its transitive `Hst.Core` 1.0.104 (MIT) decode LHA.
- `RecursiveAI.OneArchive` 1.2.0 (MIT OR Apache-2.0) and its dependency-free transitive
  `RecursiveAI.OneCompress` 1.2.0 (MIT OR Apache-2.0) decode CAB stored, MSZIP and LZX folders.
- TAR, gzip and ZIP use the .NET 10 runtime and plugin code.
- ZOO and the 7-Zip/ARJ/RAR header checks are implemented in the plugin.

All packages expose a permissive `PackageLicenseExpression` (`MIT` or `MIT OR Apache-2.0`), target
`net10.0`, and pass the Trivy license gate in `docs/legal/dependency-policy.md`. `unace` and its
.NET ports are GPL-licensed and therefore rejected, so ACE relies on `SharpCompress` and extracts
stored entries only; no compliant maintained ZOO package exists, so the ZOO reader is a small
custom implementation written from the public-domain format description.
