# Zstandard Archive Index Plugin

## Overview
The `skycd-zstd` plugin indexes Zstandard-compressed single streams (`*.zst`
and the tarball suffix `*.tzst`) as read-only catalogs. During scanning it
reports the decompressed payload as one catalog entry. When at least one
metadata-collecting plugin is enabled it also temporarily extracts the payload
so those plugins can inspect it, then removes the extracted file. It never
writes or modifies the compressed file.

The plugin is discovered through assembly metadata and capability types
(`IFileFormatPluginCapability` and `IIndexWorkItemProvider`); no `plugin.json`
descriptor is used. A single plugin instance provides both capabilities.

## Supported Archives
- **Container**: standard Zstandard frames (magic `28 B5 2F FD`) and
  concatenated multi-frame files, where every frame is decoded in sequence.
- **Extensions**: `.zst` and the compressed-tarball suffix `.tzst`.
- **Not supported**: files without the Zstandard frame magic, skippable-frame
  prefixes, frame header descriptors with reserved or unused bits set, and
  reserved dictionary-identifier encodings. These are rejected as
  `ZSTD_UNSUPPORTED`; truncated or corrupted frames are reported as
  `ZSTD_DAMAGED`. In both cases indexing continues without a work item.

## Metadata Collection
When metadata-collecting plugins are enabled, the plugin contributes one work
item for the decompressed stream. The catalog name is derived from the file name
with the compression suffix removed (`catalog.zst` becomes `catalog`), falling
back to `content` when no usable name remains. Extraction is bounded by
`ZstdIndexLimits` (256 MiB per entry by default); a payload beyond the budget is
skipped. The extracted file lives under a unique `skycd-zstd-index-<guid>`
temporary directory created for a single indexing pass. That directory is
deleted when the pass finishes, including on provider failure or cancellation.
Non-seekable sources are buffered to a temporary file inside the same directory
first.

Zstandard frames may omit the content size, so the catalog entry size is
determined by decoding the stream once during listing.

## Resource and Security Bounds
- The single derived name is sanitized (path separators stripped, compression
  suffix removed) before it is combined with the temporary root, so a crafted
  file name can never place output outside it.
- The per-entry size budget stops a hostile stream (including concatenated
  frames) from exhausting disk space.
- Header validation runs before any decoding, so unsupported or damaged frames
  are rejected without allocating decoder state beyond the frame prefix.
- Reads are read-only; attempts to use the format for saving are blocked by the
  host because `CanWrite` is `false`.

## Dependencies and Licensing
The plugin uses `SharpCompress` `0.50.8` (MIT), which contains a maintained
managed Zstandard implementation (`SharpCompress.Compressors.ZStandard`) and
avoids a separate native decoder. The package exposes `PackageLicenseExpression`
`MIT`, targets `net10.0`, is already used by the 7z, EComic, ARJ, RAR, XZ, and
BZIP2 plugins, and is allowed by `docs/legal/dependency-license-policy.json`.
