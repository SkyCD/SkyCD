# TAR Archive Format Plugin

TAR metadata collection is handled by the consolidated `SkyCD.Plugin.Metadata.Archives` plugin;
see [archives.md](archives.md). This page covers only the read-only format plugin.

| Project | Plugin id | Capability | Role |
|---|---|---|---|
| `Plugins/SkyCD.Plugin.FileFormat.Tar/` | `skycd-tar` | `IFileFormatPluginCapability` | Opens `.tar`, `.tar.gz` and `.tgz` as a read-only catalog/filesystem view |

The plugin is discovered through assembly metadata and implemented capability types only; no
`plugin.json` descriptor is used. Tests live in `tests/SkyCD.Plugin.FileFormat.Tar.Tests/`.

## Format plugin (`skycd-tar`)

`TarArchiveIndexPlugin` lists every folder and file in the archive, normalizing entry names to
forward slashes on every platform, synthesizing missing parent folders, and reporting `kind`,
`fullPath`, `name`, `sizeBytes` and `modifiedUtc` per row. Gzip compression is detected from the
first bytes, so `.tar.gz` and `.tgz` need no separate code path. The format is read-only:
`CanWrite` is `false`, and any write attempt is blocked by the host.

Opening an archive as a catalog view and extracting its contents for metadata collection are
independent: the format plugin implements only `IFileFormatPluginCapability`, while the
`SkyCD.Plugin.Metadata.Archives` plugin implements `IIndexWorkItemProvider`. Disabling one does not
disable the other.

## Dependencies and licensing

The plugin adds no NuGet dependency: it uses the .NET 10 runtime's `System.Formats.Tar` and
`System.IO.Compression` (MIT-licensed .NET runtime) plus the plugin abstractions project.
`docs/legal/dependency-license-policy.json` is unchanged.
