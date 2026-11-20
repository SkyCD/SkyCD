# ZOO Archive Index Plugin

## Overview
The `skycd-zoo` plugin indexes ZOO archives (`*.zoo`) as read-only catalogs.
During scanning it lists every stored folder and file, including nested paths,
and contributes them to the catalog hierarchy. When at least one
metadata-collecting plugin is enabled it also temporarily extracts each entry so
those plugins can inspect it, then removes the extracted files. It never writes
or modifies the archive.

The plugin is discovered through assembly metadata and capability types
(`IFileFormatPluginCapability` and `IIndexWorkItemProvider`); no `plugin.json`
descriptor is used. A single plugin instance provides both capabilities.

## Supported Archives
- **Container**: ZOO archives, format 1.0 through 2.10, with the `0xFDC4A7DC`
  archive tag and self-consistent `zoo_start`/`zoo_minus` header fields. Both the
  legacy 34-byte header and the current 42-byte header are accepted; the linked
  directory-entry chain is followed to its terminator.
- **Compression methods**: stored (method 0) and LZD (method 1). LZD is the ZOO
  LZW variant with 9-13 bit least-significant-bit-first codes, a CLEAR code
  (256) and an end-of-file code (257); the extractor restores the original bytes
  and verifies length and CRC-16 for every member.
- **Listed but not extracted**: LZH members (method 2) appear in the inventory
  because only directory headers are parsed, but they are skipped during
  metadata collection because their packing method is unsupported. ZOO has no
  encryption feature, so there is no encrypted-archive case to report.
- Deleted directory entries are ignored. Damaged headers, invalid tags, and
  truncated entry chains raise a damaged-archive error that is surfaced as a
  typed `ZOO_DAMAGED` failure instead of partially indexing the archive.

## Metadata Collection
When metadata-collecting plugins are enabled, the plugin contributes one work
item per contained file. Extraction is bounded by `ZooIndexLimits`
(100,000 entries, 256 MiB per entry, 1 GiB total by default); entries beyond the
budget are skipped. All extracted files live under a unique
`skycd-zoo-index-<guid>` temporary directory created for a single indexing pass.
That directory is deleted when the pass finishes, including on provider failure
or cancellation. Non-seekable sources are buffered to a temporary file inside
the same directory first, because the directory chain is located by seeking.

Extraction failures on a single member (unsupported method, damaged data, CRC
mismatch, or I/O error) delete that member's file and skip it without failing
the rest of the archive.

## Resource and Security Bounds
- Entry paths are normalized and unsafe names (absolute paths, `..` segments)
  are skipped before extraction; every resolved path is re-checked to stay under
  the temporary root, so archive contents never escape it.
- Entry and total-size budgets stop a hostile archive from exhausting disk space,
  and the LZD decoder validates every code and never writes past the declared
  member size.
- Directory entries are bounds-checked against the archive length, and the
  variable directory-entry section is capped at 527 bytes.
- Reads are read-only; attempts to use the format for saving are blocked by the
  host because `CanWrite` is `false`.

## Dependencies and Licensing
The plugin adds no new NuGet dependency. `SharpCompress` (MIT, already approved)
does not support ZOO, and no suitable maintained .NET 10-compatible ZOO package
with a license allowed by `docs/legal/dependency-license-policy.json` was found.
The reader and LZD decoder are therefore a small custom implementation written
from the public-domain ZOO format knowledge published with the reference `zoo`
sources (released to the public domain / Unlicense), which introduces no new
third-party code or licensing obligations.
