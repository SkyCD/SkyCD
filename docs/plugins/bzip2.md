# BZIP2 Archive Index Plugin

## Overview
The `skycd-bzip2` plugin indexes bzip2-compressed single streams (`*.bz2`,
`*.bz`, and the tarball suffixes `*.tbz`, `*.tbz2`) as read-only catalogs.
During scanning it reports the decompressed payload as one catalog entry. When
at least one metadata-collecting plugin is enabled it also temporarily extracts
the payload so those plugins can inspect it, then removes the extracted file. It
never writes or modifies the compressed file.

The plugin is discovered through assembly metadata and capability types
(`IFileFormatPluginCapability` and `IIndexWorkItemProvider`); no `plugin.json`
descriptor is used. A single plugin instance provides both capabilities.

## Supported Archives
- **Container**: standard bzip2 streams with a `BZh1`–`BZh9` header and
  concatenated multi-stream files (all members are decoded in sequence).
- **Extensions**: `.bz2`, the legacy `.bz` variant, and the compressed-tarball
  suffixes `.tbz` and `.tbz2`.
- **Not supported**: files without a valid `BZh` header, invalid block-size
  digits (`BZh0` and later), and truncated or corrupted streams. These are
  rejected as `BZIP2_DAMAGED` instead of being reported as an empty archive;
  extraction skips them and indexing continues without a work item.

## Metadata Collection
When metadata-collecting plugins are enabled, the plugin contributes one work
item for the decompressed stream. The catalog name is derived from the file name
with the compression suffix removed (`catalog.bz2` becomes `catalog`, a `.tbz2`
tarball becomes its base name), falling back to `content` when no usable name
remains. Extraction is bounded by `Bzip2IndexLimits` (256 MiB per entry by
default); a payload beyond the budget is skipped. The extracted file lives under
a unique `skycd-bzip2-index-<guid>` temporary directory created for a single
indexing pass. That directory is deleted when the pass finishes, including on
provider failure or cancellation. Non-seekable sources are buffered to a
temporary file inside the same directory first.

Because bzip2 stores no uncompressed size, the catalog entry size is determined
by decoding the stream once during listing.

## Resource and Security Bounds
- The single derived name is sanitized (path separators stripped, compression
  suffix removed) before it is combined with the temporary root, so a crafted
  file name can never place output outside it.
- The per-entry size budget stops a hostile stream (including concatenated
  members) from exhausting disk space.
- Damaged headers, unsupported block sizes, and truncated streams are reported
  through failure telemetry or skipped work items rather than crashing indexing.
- Reads are read-only; attempts to use the format for saving are blocked by the
  host because `CanWrite` is `false`.

## Dependencies and Licensing
The plugin adds `SharpCompress` `0.50.8` (MIT) for bzip2 decoding. The package
exposes `PackageLicenseExpression` `MIT`, targets `net10.0`, is already used by
the ARJ, RAR, EComic, and SevenZip plugins, and is allowed by
`docs/legal/dependency-license-policy.json`. It is actively maintained and
avoids hand-written Huffman/BWT decoding and CRC verification.
