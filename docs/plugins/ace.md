# ACE Archive Index Plugin

## Overview
The `skycd-ace` plugin indexes ACE archives (`*.ace`) as read-only catalogs.
During scanning it lists every stored folder and file, including nested paths,
and contributes them to the catalog hierarchy. When at least one
metadata-collecting plugin is enabled it also temporarily extracts each
uncompressed entry so those plugins can inspect it, then removes the extracted
files. It never writes or modifies the archive.

The plugin is discovered through assembly metadata and capability types
(`IFileFormatPluginCapability` and `IIndexWorkItemProvider`); no `plugin.json`
descriptor is used. A single plugin instance provides both capabilities.

## Supported Archives
- **Container**: ACE 2.0 archives. Every block starts with a CRC-16 of its
  header and a header length; the main header is validated for the `**ACE**`
  signature. The main, file, and recovery blocks are understood.
- **Compression methods**: stored entries (method 0) are listed and, when
  metadata collection is on, extracted after their CRC-32 verifies. Compressed
  entries are listed with their sizes from the headers but are never
  extracted.
- **Not supported**: compressed entries, encrypted archives, split
  multi-volume sets, and unknown block types. These are reported with typed
  diagnostics (`ACE_UNSUPPORTED`, `ACE_ENCRYPTED`, `ACE_DAMAGED`) instead of
  being partially indexed.

## Why Stored Entries Only
ACE compression methods are proprietary. The only free decoders found are
licensed under the GNU GPL, which `docs/legal/dependency-license-policy.json`
blocks for redistribution, so the plugin ships a self-contained header parser
and no decompressor. Compressed entries therefore stay visible in the catalog
listing (name, size, timestamp) but contribute no metadata work item. This is
an explicit tradeoff: correct, license-compliant behavior instead of no plugin
at all.

## Metadata Collection
When metadata-collecting plugins are enabled, the plugin contributes one work
item per stored, unencrypted entry whose CRC-32 matches. Extraction is bounded
by `AceIndexLimits` (100,000 entries, 256 MiB per entry, 1 GiB total by
default); entries beyond the budget are skipped with a typed diagnostic. All
extracted files live under a unique `skycd-ace-index-<guid>` temporary
directory created for a single indexing pass. That directory is deleted when
the pass finishes, including on provider failure or cancellation. Non-seekable
sources are buffered to a temporary file inside the same directory first,
because the archive is walked twice (headers, then payloads).

## Resource and Security Bounds
- Entry paths are normalized and unsafe names (absolute paths, `..` segments)
  are skipped before extraction; every resolved path is re-checked to stay under
  the temporary root, so archive contents never escape it.
- Entry and total-size budgets stop a hostile archive from exhausting disk space.
- Each extracted file is written only after its stored CRC-32 matches the
  header, so corrupted payloads are skipped instead of entering the catalog.
- Damaged headers raise a damaged-archive error and are reported through plugin
  failure telemetry rather than crashing indexing.
- Reads are read-only; attempts to use the format for saving are blocked by the
  host because `CanWrite` is `false`.

## Dependencies and Licensing
The plugin adds no NuGet dependency: the ACE CRC-16/CRC-32, block headers, and
stored-entry extraction are implemented in the plugin against the public ACE
format description. Evaluated alternatives were rejected by
`docs/legal/dependency-license-policy.json`: `unace` and its .NET ports are
GPL-licensed, which the policy blocks, and no permissively licensed ACE
decompressor exists. Stored entries need no decompression, so the plugin
remains useful without a bundled decoder.
