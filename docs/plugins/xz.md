# XZ Archive Index Plugin

## Overview
The `skycd-xz` plugin indexes xz-compressed single files (`*.xz`, `*.txz`) as read-only catalogs.
During scanning it lists the compressed stream as one catalog entry. When at least one
metadata-collecting plugin is enabled it also temporarily extracts the stream so those plugins can
inspect it, then removes the extracted file. It never writes or modifies the archive.

The plugin is discovered through assembly metadata and capability types
(`IFileFormatPluginCapability` and `IIndexWorkItemProvider`); no `plugin.json` descriptor is used. A
single plugin instance provides both capabilities.

## Supported Member
- **Container**: xz stream header with the `FD 37 7A 58 5A 00` magic, stream flags, and a verified
  header CRC32.
- **Entry name**: xz stores no file name, so the storage file name is used with its `.xz`/`.txz`
  suffix removed.
- **Entry size**: computed by decoding the stream, because xz keeps no uncompressed size in the
  header. Very large streams therefore cost one full decode while cataloging.

## Diagnostics
- `XZ_DAMAGED` – bad magic, header CRC mismatch, truncated stream, or corrupt compressed data
  detected while decoding.
- `XZ_UNSUPPORTED` – the stream flags set reserved bits, or the decoder does not support the
  declared checksum type.
xz has no encryption concept, so encrypted payloads surface as damaged streams.

## Metadata Collection
When metadata-collecting plugins are enabled, the plugin contributes the extracted stream as one work
item. Extraction is bounded by `XzIndexLimits` (256 MiB by default); streams beyond the budget are
skipped. The extracted file lives under a unique `skycd-xz-index-<guid>` temporary directory created
for a single indexing pass. That directory is deleted when the pass finishes, including on provider
failure or cancellation. Non-seekable sources are buffered to a temporary file inside the same
directory first, because the header is validated before decoding.

## Resource and Security Bounds
- The member name is derived from the storage file name only, so archive contents cannot inject path
  segments; the resolved target always lives directly under the temporary root.
- The entry-size budget stops a hostile stream (a compression bomb) from exhausting disk space.
- Damaged, truncated, and unsupported streams are reported through typed diagnostics or contribute
  nothing instead of crashing indexing.
- Reads are read-only; attempts to use the format for saving are blocked by the host because
  `CanWrite` is `false`.

## Dependencies and Licensing
The plugin uses `SharpCompress` `0.50.8` (MIT) for xz decoding. The package is already used by
`skycd-7z`, `skycd-ecomic`, `skycd-arj`, and `skycd-rar`, targets `net10.0`, remains actively
maintained, and is allowed by `docs/legal/dependency-license-policy.json`.
