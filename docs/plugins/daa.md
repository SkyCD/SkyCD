# DAA Disk Image Index Plugin

## Overview
The `skycd-daa` plugin indexes PowerISO Direct Access Archive images (`*.daa`)
as read-only catalogs. During scanning it enumerates the ISO 9660 filesystem
stored inside the archive and contributes its folders and files to the catalog
hierarchy. It never writes or modifies the image.

The plugin is discovered through assembly metadata and capability types
(`IFileFormatPluginCapability` and `IIndexWorkItemProvider`); no `plugin.json`
descriptor is used.

## Supported Images
- **Container**: DAA header with 3-byte chunk length table and raw DEFLATE
  (RFC 1951) chunks. Version marker `0x100` is supported.
- **Filesystem**: ISO 9660 with Joliet names, read through DiscUtils.
- **Multipart volumes**: companion volumes are resolved automatically using the
  PowerISO naming conventions.
  - `name.part01.daa` -> `name.part02.daa`, ...
  - `name.part001.daa` -> `name.part002.daa`, ...
  - `name.daa` -> `name.d01`, `name.d02`, ...
  Companion volumes must sit next to the first volume.
- **Not supported**: LZMA-compressed images (version `0x110`) and
  password-protected images; both are rejected with a clear diagnostic instead
  of being partially indexed.

## Metadata Collection
When metadata-collecting plugins are enabled, the plugin contributes one work
item per contained file. Each item opens the archive, positions the compressed
stream at the required chunk, and decompresses only the bytes requested by the
metadata pipeline, so a single file is materialized at a time rather than the
whole image.

## Resource and Security Bounds
- Entry paths are normalized and unsafe names (absolute paths, `..` segments,
  control characters) are skipped; nothing is ever written outside a temporary
  directory.
- Multi-volume loading is capped (1,000 volumes), chunk counts and chunk sizes
  are validated against the declared ISO size, and truncated or invalid chunk
  data raises a damaged-image error.
- Non-seekable inputs are buffered to a per-read temporary directory that is
  deleted when the read finishes, including on failure or cancellation.
- Reads are read-only; attempts to use the format for saving are blocked by the
  host because `CanWrite` is `false`.

## Dependencies and Licensing
The plugin adds no new NuGet dependency for DAA decoding: the container is
handled with `System.IO.Compression.DeflateStream`. ISO 9660 parsing reuses
`DiscUtils.Iso9660` `0.16.13` (MPL-2.0), which is already used by
`skycd-iso` and is allowed by `docs/legal/dependency-license-policy.json`.

The DAA layout was implemented from publicly documented reverse-engineering
notes; no GPL-licensed implementation code was copied.
