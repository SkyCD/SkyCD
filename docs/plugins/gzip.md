# GZIP Archive Index Plugin

## Overview
The `skycd-gzip` plugin indexes gzip-compressed single files (`*.gz`, `*.gzip`) as read-only catalogs.
During scanning it lists the compressed member as one catalog entry. When at least one
metadata-collecting plugin is enabled it also temporarily extracts the member so those plugins can
inspect it, then removes the extracted file. It never writes or modifies the archive.

The plugin is discovered through assembly metadata and capability types
(`IFileFormatPluginCapability` and `IIndexWorkItemProvider`); no `plugin.json` descriptor is used. A
single plugin instance provides both capabilities.

## Supported Member
- **Container**: gzip member header per RFC 1952 (magic `1F 8B`) with the deflate method (8).
- **Entry name**: the optional original file name from the header when it is safe; otherwise the
  storage file name with its `.gz`/`.gzip` suffix removed.
- **Entry size**: the uncompressed size from the member trailer; `0` when the trailer is unavailable.
- **Compressed TAR files**: `*.tar.gz` and `*.tgz` stay owned by the TAR plugin, which already reads
  the compressed stream, so this plugin contributes nothing for those names.

## Diagnostics
- `GZIP_DAMAGED` – the header is not a gzip member, uses reserved flags, has a truncated or
  over-long name field, or the member is shorter than a complete header and trailer.
- `GZIP_UNSUPPORTED` – the header declares a compression method other than deflate.
gzip has no encryption flag, so encrypted data surfaces as a damaged member.

## Metadata Collection
When metadata-collecting plugins are enabled, the plugin contributes the extracted member as one work
item. Extraction is bounded by `GzipIndexLimits` (256 MiB by default); members beyond the budget are
skipped. The extracted file lives under a unique `skycd-gzip-index-<guid>` temporary directory created
for a single indexing pass. That directory is deleted when the pass finishes, including on provider
failure or cancellation. Non-seekable sources are buffered to a temporary file inside the same
directory first, because the header can only be validated on a seekable stream.

## Resource and Security Bounds
- The header name is normalized to relative forward-slash paths. Absolute paths, drive qualifiers,
  `.`/`..` segments, and names longer than 1024 bytes never reach extraction; the storage file name is
  used instead.
- Every resolved target is re-checked to stay under the temporary root, so archive contents never
  escape it.
- The entry-size budget stops a hostile member (a compression bomb) from exhausting disk space.
- Damaged and truncated members are reported through typed diagnostics or contribute nothing instead
  of crashing indexing.
- Reads are read-only; attempts to use the format for saving are blocked by the host because
  `CanWrite` is `false`.

## Dependencies and Licensing
The plugin adds no NuGet dependency: gzip decompression uses the BCL `System.IO.Compression.GZipStream`
and the header is parsed directly. No new package evaluation was required.
