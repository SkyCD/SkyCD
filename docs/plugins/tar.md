# TAR Archive Plugins

TAR support is implemented by **two separate plugins** with distinct responsibilities:

| Project | Plugin id | Capability | Role |
|---|---|---|---|
| `Plugins/SkyCD.Plugin.FileFormat.Tar/` | `SkyCD.Plugin.FileFormat.Tar` | `IFileFormatPluginCapability` | Opens `.tar`, `.tar.gz` and `.tgz` as a read-only catalog/filesystem view |
| `Plugins/SkyCD.Plugin.Metadata.Tar/` | `skycd.tar-metadata` | `IIndexWorkItemProvider` | Contributes archive entries as index work items so metadata plugins can inspect them |

Both are discovered through assembly metadata and implemented capability types only; no
`plugin.json` descriptor is used.

Tests: `tests/SkyCD.Plugin.FileFormat.Tar.Tests/` (format) and `tests/SkyCD.Plugin.Metadata.Tar.Tests/` (work items).

## Format plugin (`skycd-tar`)

`TarArchiveIndexPlugin` implements only `IFileFormatPluginCapability`. `ReadAsync` lists
every folder and file in the archive, normalizing entry names to forward slashes on every
platform, synthesizing missing parent folders, and reporting `kind`, `fullPath`, `name`,
`sizeBytes` and `modifiedUtc` per row. Gzip compression is detected from the first bytes, so
`.tar.gz` and `.tgz` need no separate code path. The format is read-only: `CanWrite` is
`false`, and any write attempt is blocked by the host.

The plugin no longer implements `IIndexWorkItemProvider`, so opening an archive as a
catalog view and extracting its contents for metadata collection are independent: disabling
one does not disable the other.

## Metadata plugin (`skycd.tar-metadata`)

`TarMetadataWorkItemProvider` implements only `IIndexWorkItemProvider`. It claims files whose
name ends in `.tar`, `.tar.gz` or `.tgz` and returns one work item per stored regular file;
other inputs return an empty set. Symbolic links, hard links, devices and directory entries
are not extracted. The host runs enabled metadata providers over each item and disposes the
returned set afterwards, so temporary files never outlive indexing, including on provider
failure or cancellation.

### Limits and cleanup

- Extraction is bounded by `TarMetadataLimits` (100,000 entries, 256 MiB per entry,
  1 GiB total by default); entries beyond the budget are skipped, and the entry count bound
  stops the scan.
- All extracted files live under a unique `skycd-tar-index-<guid>` temporary directory
  created for a single indexing pass. It is deleted when the set is disposed, when a
  damaged archive is rejected, and when extraction fails or is cancelled.
- Non-seekable sources are buffered to a temporary file inside the same directory before
  reading.

### Safety

- Entry paths are normalized and unsafe names (absolute paths, drive-qualified paths,
  `..` segments, control characters) are skipped before extraction; every resolved path is
  re-checked to stay under the temporary root, so archive contents never escape it.
- The copy loop counts bytes actually written and aborts with
  `TarEntryLimitExceededException` when an entry expands past its limit, so a header that
  understates the size cannot fill the disk.
- Damaged archives, unsupported entry data and per-entry I/O failures skip that entry or
  return an empty set instead of crashing indexing.

## Dependencies and licensing

Neither plugin adds a NuGet dependency: both use the .NET 10 runtime's
`System.Formats.Tar` and `System.IO.Compression` (MIT-licensed .NET runtime) plus the plugin
abstractions project. `docs/legal/dependency-license-policy.json` is unchanged.
