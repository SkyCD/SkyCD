# ISO 9660 / UDF Disk Image Index Plugin

## Overview
The `skycd-iso` plugin indexes optical disc images (`*.iso`) as read-only
catalogs. During scanning it enumerates the image's filesystem and contributes
its folders and files to the catalog hierarchy. When metadata-collecting plugins
are enabled it also exposes each stored file to them, one file at a time. It
never writes or modifies the image.

The plugin is discovered through assembly metadata and capability types
(`IFileFormatPluginCapability` and `IIndexWorkItemProvider`); no `plugin.json`
descriptor is used. A single plugin instance provides both capabilities.

## Supported Images
- **Filesystems**: ISO 9660 with Joliet extensions, and UDF (read through
  DiscUtils). UDF is detected first because a UDF image does not carry an ISO
  9660 primary volume descriptor.
- **Naming**: ISO 9660 names are upper-cased and their `;version` suffixes are
  dropped, matching the catalog rows produced for other ISO 9660 based plugins.
  UDF names keep their stored case.
- **Companion files**: none. ISO and UDF images are single-file formats, so
  scanning one file is enough.
- **Not supported**: images without an ISO 9660 or UDF volume, encrypted or
  otherwise unreadable images, and filesystems DiscUtils cannot parse. These are
  reported with typed diagnostics (`ISO_UNSUPPORTED`, `ISO_DAMAGED`) instead of
  being partially indexed.

## Metadata Collection
When at least one metadata-collecting plugin is enabled, the plugin contributes
one work item per contained file. Each item re-opens the image and copies only
that entry into a `skycd-iso-entry-<guid>.tmp` file under the system temporary
directory, marked delete-on-close. The temporary file is removed as soon as the
metadata pipeline disposes the stream, including on provider failure or
cancellation.

Non-seekable image sources are first buffered into a
`skycd-iso-source-<guid>.tmp` file, also delete-on-close, so the DiscUtils reader
can seek. A single file from the image is materialized at a time; the image is
never fully extracted.

## Resource and Security Bounds
- Contributor limits are controlled by `IsoIndexLimits` (100,000 entries and
  256 MiB per entry by default); oversized entries are skipped.
- Entry paths are normalized and unsafe names (absolute paths,
  drive-qualified paths, `.`/`..` segments) are skipped before extraction.
  Temporary files are always named by GUID, so no entry name is ever joined into
  a filesystem path and nothing can be written outside the temporary directory.
- Directory traversal is depth-capped, and an unreadable entry or subdirectory
  is skipped without losing the rest of the listing. A damaged root directory
  fails the read with `ISO_DAMAGED`.
- Cancellation is honored during listing, buffering, extraction and stream
  copies; temporary files are cleaned up on every failure path.
- Reads are read-only; attempts to use the format for saving are blocked by the
  host because `CanWrite` is `false`.

## Dependencies and Licensing
The plugin uses `DiscUtils.Iso9660` `0.16.13` (MIT), already used by
`skycd-daa`, and adds `DiscUtils.Udf` `0.16.13` (MIT) for UDF images. Both are
.NET 10 compatible, actively maintained, and allowed by
`docs/legal/dependency-license-policy.json`.
