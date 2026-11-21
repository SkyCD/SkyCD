# Catalog Plugin Installs: Staging, Activation and Recovery

Catalog installs and updates never write into the folder that assembly discovery scans. A package is
downloaded and validated in a hidden staging area, a pending operation marker records the change, and the
content activates on the next host restart, immediately before `PluginManager.Discover` enumerates the
plugin root. Contracts live in `SkyCD.Plugin.Runtime.Installation`; the file-system implementation lives in
`SkyCD.Core.Plugins.Installation`.

## Locations

State root defaults to `%LOCALAPPDATA%\SkyCD\plugin-state` and is overridden by the
`SKYCD_PLUGIN_STATE_DIRECTORY` environment variable.

```text
<state>/plugins/<plugin id>/                        install folder; directory name is the plugin id
<state>/plugins/<plugin id>/.skycd-install.json     receipt; marks the folder as catalog-owned
<state>/plugins/<plugin id>/...                     entry assembly and bundled private dependencies

<state>/staging/<host version>/pending/<plugin id>.json    pending operation markers
<state>/staging/<host version>/<nonce>/content/            package content while downloading/validating
<state>/staging/<host version>/<nonce>/prepared/           validated content referenced by the marker
```

Staging is validated to be outside the plugin root, so discovery can never load downloaded content.
Transient folders also live beside the install folders: `.backup-<id>-<nonce>` (replaced install) and
`.replace-<id>-<nonce>` (first step of the two-step move). Both are hidden by the `.` prefix and are not
plugin folders.

## Publisher Package Layout

The catalog stages the exact version named by `PluginPackageRequest`: a NuGet v3 service index URL
(exposing `PackageBaseAddress/3.0.0`), package id and version. Downloads use HTTPS without redirects and are
size-bounded.

- Prefer `lib/<tfm>/` content; packages without a `lib/` folder may use `tools/<tfm>/any/`.
- Every file in the selected folder is extracted; entry names, duplicates, symlinks and path traversal are
  rejected.
- Limits: 4096 entries, 64 MiB per entry, 256 MiB total uncompressed.
- The package must contain exactly one plugin assembly for its plugin id. Identity is read from assembly
  metadata only (`PluginIdAttribute`, `MinHostVersionAttribute`, `MaxHostVersionAttribute`,
  `AssemblyVersion`) with `MetadataReader`; plugin code is never loaded during staging.
- The declared host range must include the running host version. Without `MinHostVersionAttribute` the host
  default of `3.0.0` applies.
- A package may not claim a plugin id that belongs to another active plugin or to a folder without a receipt.

## Staged Install

`IPluginPackageInstaller.StageInstallAsync`:

1. Creates `<staging>/<nonce>`, downloads `nupkg`, and extracts/validates into `content/`.
2. Reads the identity and checks package id, version, host range and plugin-id conflicts.
3. Renames `content/` to `prepared/` and writes `pending/<plugin id>.json` with
   `PluginInstallOperationStatus.PendingRestart`.

Repeating the same source/package/version request returns the existing pending state without downloading.
A different package that would stage the same plugin identity fails with
`PluginIdentityConflictException`. `CancelPendingAsync` deletes the marker and staged content and never
touches the active version.

## Startup Activation and Rollback

`PluginServiceRegistrator.ApplyPendingOperationsBeforeDiscoveryAsync` is called by the desktop app and the
CLI before discovery. For each pending marker (ordered by plugin id):

1. Read the prepared identity; a pending marker whose staged content disappeared is recovered from the
   receipt/`.replace-` folder when possible, otherwise reported as `InvalidPluginAssembly`.
2. A catalog-owned active folder is moved to `.backup-<id>-<nonce>`.
3. Prepared content is moved to `.replace-<id>-<nonce>` and then to the install folder.
4. `.skycd-install.json` is written into the install folder, the marker and staging are deleted, and backups
   are pruned to the two newest.

Results map to `PluginInstallOperationStatus`:

| Outcome | Status | Active version |
|---|---|---|
| Validation failed before the move | `Failed` | previous version untouched |
| Move failed after the backup | `RolledBack` | backup restored |
| Activation completed | `Installed` | new version |
| Marker discarded | `Cancelled` | previous version untouched |

Folders without `.skycd-install.json` are manual or bundled plugins and are never replaced. Interrupted
activation is completed on the next startup: a matching receipt finishes the operation, an install folder
without a receipt is adopted after identity validation, and prepared content found in a `.replace-` folder is
moved into place.

## Receipt

`.skycd-install.json` records `SchemaVersion` (currently `1`), plugin id, source URL, package id, active and
previous version, entry assembly path/name/version, host range, claimed plugin ids and install time. Receipts
from a newer schema are rejected rather than guessed. `IPluginInstallStateSource` projects receipts and
pending markers into `PluginInstallState` for the marketplace UI (`ActiveVersion`, `PendingVersion`,
`RestartRequired`, last `FailureReason`/`Error`).

## Failure Reasons

`PluginInstallException` subclasses keep fixed message templates and map to `PluginInstallFailureReason`:
`FeedUnavailable`, `DownloadFailed`, `ArchiveCorrupt`, `UnsafeArchivePath`, `PackageIdentityMismatch`,
`InvalidPluginAssembly`, `IncompatibleHostVersion`, `IdentityConflict`, `DestinationUnavailable`.

Files that stay locked by the running host cannot be replaced; activation reports `DestinationUnavailable`
and keeps the previous version active. Locked staging leftovers are removed by the next startup cleanup.

See [runtime loading](runtime-loading.md) for the discovery contract that runs after activation.
