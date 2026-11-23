# Disk Image Metadata Plugin

## Overview

`SkyCD.Plugin.Metadata.DiskImages` is the single metadata plugin for disk images. During a path
scan the host offers every file to `DiskImagesIndexPlugin` (an `IIndexWorkItemProvider`); when the
file name matches a supported image, the plugin lists the files contained in the image and returns
them as work items so enabled metadata plugins can inspect them one at a time. It never writes to or
modifies an image, and the plugin exposes no `IFileFormatPluginCapability`.

The assembly has one discovery entry with one `[assembly: PluginId]` value, `skycd.diskimages`, so
the Plugin Manager shows one plugin and one enable toggle for all supported image formats. The
plugin is discovered through assembly metadata and implemented capability types; no `plugin.json`
descriptor is used.

Tests live in `tests/SkyCD.Plugin.Metadata.DiskImages.Tests/`, with one folder per format and shared
fixtures under `Fixtures/`.

## Previous per-format plugins

This plugin replaces eleven separate assemblies and plugin ids. No data migration is needed:
`PluginManager.UpsertPluginDocuments` keeps the existing `PluginDocument` rows and marks the ids it
no longer discovers as unavailable, so their rows stay in the repository but are no longer offered
as enable toggles. The new `skycd.diskimages` id is inserted with the default enabled state.

| Format | Replaced assembly | Replaced plugin id |
| --- | --- | --- |
| BIN/CUE | `SkyCD.Plugin.Metadata.BinCue` | `skycd.bincue` |
| CloneCD | `SkyCD.Plugin.Metadata.Ccd` | `skycd.ccd` |
| DAA | `SkyCD.Plugin.Metadata.Daa` | `skycd.daa` |
| DMG | `SkyCD.Plugin.Metadata.Dmg` | `skycd.dmg` |
| ISO/UDF | `SkyCD.Plugin.Metadata.Iso` | `SkyCD.Plugin.Iso` |
| MDS/MDF | `SkyCD.Plugin.Metadata.Mdf` | `skycd-mdf` |
| NRG | `SkyCD.Plugin.Metadata.Nrg` | `SkyCD.Plugin.Nrg` |
| QCOW2 | `SkyCD.Plugin.Metadata.Qcow2` | `skycd.qcow2` |
| VDI | `SkyCD.Plugin.Metadata.Vdi` | `skycd.vdi` |
| VHD/VHDX | `SkyCD.Plugin.Metadata.Vhd` | `skycd.vhd` |
| VMDK | `SkyCD.Plugin.Metadata.Vmdk` | `SkyCD.Plugin.Vmdk` |

## Supported formats and routing

`DiskImagesIndexPlugin` dispatches each file to a per-format reader in the same assembly. Matching is
extension-based and case-insensitive; companion formats match on `FileName` or `SourcePath`:

| Order | Format | Names matched | Notes |
| --- | --- | --- | --- |
| 1 | ISO/UDF | `.iso` | UDF is detected before ISO 9660. |
| 2 | NRG | `.nrg` | Nero image; first data track is browsed. |
| 3 | DAA | `.daa` | PowerISO archive with ISO 9660 payload. |
| 4 | BIN/CUE | `.cue` | BIN companion resolved next to the sheet. |
| 5 | CloneCD | `.ccd` | IMG companion resolved next to the descriptor. |
| 6 | MDS/MDF | `.mdf`, `.mds` | Descriptor optional; an MDF path is accepted directly. |
| 7 | DMG | `.dmg` | UDIF container with HFS+, FAT or ext volume. |
| 8 | QCOW2 | `.qcow2` | Backing files must stay next to the image. |
| 9 | VDI | `.vdi` | Fixed and dynamic VirtualBox images. |
| 10 | VHD/VHDX | `.vhd`, `.vhdx` | Differencing parents must stay next to the image. |
| 11 | VMDK | `.vmdk` | Descriptor and split extents. |

Files that match no format return an empty work-item set. A damaged, unsupported, encrypted or
incomplete image also contributes no work items instead of failing the scan; the per-format
diagnostics described below are preserved.

## Shared limits

Every reader is bounded by `DiskImageIndexLimits` (100,000 entries, 256 MiB per entry and 1 GiB total
by default; the constructor validates the values). The host can pass a custom instance to
`DiskImagesIndexPlugin`. `MaxDepth` is optional: when it is not set, optical-disc formats use 128
levels and file-system image formats use 256, matching their previous behavior.

Entries beyond the entry count or byte budget are skipped. ISO/UDF and NRG readers do not apply the
total-byte budget, matching the previous plugins. DAA images keep their original behavior and do not
use this limit type. The same exception type, `DiskImageEntryLimitExceededException`, signals an
entry that grows past its per-entry limit while it is being copied.

## Shared path and name handling

`DiskImageEntryPath` centralizes the path rules:

- ISO 9660 names are upper-cased and their `;version` suffix is dropped; Joliet and UDF names keep
  their stored case.
- File-system entries are normalized to forward slashes. Empty, `.`/`..` and control-character
  names are skipped rather than extracted.
- DAA entry names reject leading slashes and traversal segments before they become work items.
- ISO/UDF and executable-entry checks reject absolute, drive-qualified and traversal paths.

## Shared extraction behavior

- Each call creates a unique `skycd-<name>-entry-<guid>.tmp` (or format-specific temporary
  directory) file under the system temporary directory, marked delete-on-close.
- The file is removed when the host disposes the returned stream, including on extraction failure
  or cancellation; temporary directories are removed when the item set is disposed.
- Non-seekable sources are buffered only where the underlying reader needs seeking.
- Large or damaged entries are reported or skipped through the same typed limits instead of being
  materialized in full.

## Per-format notes

### ISO/UDF

ISO 9660 with Joliet and UDF (DiscUtils). UDF is probed first because a UDF image does not carry an
ISO 9660 primary volume descriptor. Images without either volume are reported as unsupported.

### NRG

The Nero image layout is parsed to locate the first data track, then the ISO 9660 filesystem is read
through DiscUtils. Audio-only images contribute no entries.

### DAA

PowerISO Direct Access Archive with raw DEFLATE chunks, including multipart volumes (`part01`, `d01`
and similar naming), read through DiscUtils' ISO 9660 reader without extracting the whole archive.
LZMA-compressed and password-protected images are rejected. Multi-volume loading is capped, and
non-seekable inputs are buffered to a temporary directory that is removed afterwards.

### BIN/CUE, CloneCD and MDS/MDF

All three are companion sets whose track data is stored raw:

- The small descriptor or sheet (`.cue`, `.ccd`, `.mds`) is parsed to find the first data track and
  its sector size; the `.bin`, `.img` or `.mdf` companion is resolved next to it. Only the simple
  file name from the sheet or descriptor is honored, so a hostile descriptor cannot point outside
  the image directory.
- Supported modes are `MODE1/2048`, `MODE1/2352`, `MODE2/2048`, `MODE2/2336`, `MODE2/2352` and, for
  BIN/CUE, `CDI/2336` and `CDI/2352`. The 2048-byte user-data area is mapped for the ISO 9660
  reader.
- Audio tracks before the data track are skipped by honoring their declared sector index. MDS/MDF
  additionally probes the sector layout (`2048/0`, `2352/16`, `2352/24`, `2336/8`) instead of
  trusting the descriptor.
- Audio-only sets, scrambled CloneCD data tracks, missing companions, damaged descriptors and
  non-binary track containers contribute no entries.

### DMG, QCOW2, VDI, VHD/VHDX and VMDK

Container formats are parsed in managed code with DiscUtils, so no native tool (`hdiutil`,
`qemu-img`, `VBoxManage`, `diskpart`) is used. Guest FAT, NTFS and ext2/3/4 volumes are opened the
same way as before; every format keeps its previous rejection paths:

- DMG: encrypted and segmented images are rejected.
- QCOW2: backing chains are limited to eight layers and must stay in the image directory; encrypted
  images, zstd clusters, extended L2 entries and raw external data files are rejected.
- VDI: only the VirtualBox 1.2 header layout is accepted; snapshot chains are reported as a missing
  parent.
- VHD/VHDX: differencing parents are resolved relative to the child and limited to eight layers with
  cycle detection; parents outside the image directory are rejected.
- VMDK: descriptor and split extents must stay in the image directory.

Unsafe paths, reparse points, device entries, damaged entries and overly deep trees are skipped
without dropping the rest of the listing.

## Dependencies and licensing

All container and file-system access uses DiscUtils `0.16.13` (MIT packages:
`DiscUtils.Iso9660`, `DiscUtils.Udf`, `DiscUtils.Dmg`, `DiscUtils.Ext`, `DiscUtils.Fat`,
`DiscUtils.HfsPlus`, `DiscUtils.Ntfs`, `DiscUtils.Vhd`, `DiscUtils.Vhdx` and `DiscUtils.Vmdk`),
already used by these plugins before the consolidation. DAA decoding uses
`System.IO.Compression.DeflateStream`. Every package passes the Trivy license gate in
`docs/legal/dependency-policy.md`.

## Tests

`tests/SkyCD.Plugin.Metadata.DiskImages.Tests/` contains one folder per format plus
`DiskImagesPluginDiscoveryTests`, which asserts a single index provider, the `skycd.diskimages` id
and the absence of `IFileFormatPluginCapability`. The per-format tests keep their previous coverage:
images built in memory or from fixtures, extraction and temporary-file cleanup, entry and depth
limits, cancellation, damaged/encrypted/missing-companion rejections, and host indexing through
`IndexWorkItemSequence` with a metadata provider.

`Fixtures/<Format>/` keeps the sample images for manual checks.
