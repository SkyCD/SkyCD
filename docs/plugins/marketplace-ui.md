# Plugin Marketplace: Browse, Install and Update in Options

## Scope
The Options dialog (`Tools > Plugins`) hosts two views over the same plugin category: the existing
**Installed** list and the new **Browse** view backed by the plugin catalog (#802) and the staged installer
(#803). Browsing never enables, configures or executes a plugin; installing or updating only stages a package
that activates on the next SkyCD start.

## Views and switching
- `Installed` and `Browse` toggle buttons appear in the plugin category header. The installed list, plugin
  path and per-plugin settings pages are unchanged.
- `Browse` loads the catalog lazily on first selection. `Refresh` forces a network resolution; a plain view
  switch reuses the last snapshot.
- Catalog and installed content are independent. If the catalog cannot be loaded, the Browse view reports the
  failure and the Installed list remains fully usable.

## Browse list
Each catalog package appears once even when several sources advertise the same package ID. The highest
eligible release is selected; the item shows:

| Field | Source |
| --- | --- |
| Name | `MarketplacePackage.Title` |
| Publisher | publisher manifest, fallback "Unknown publisher" |
| Source | host of the publisher manifest URL |
| Available version | selected release, or the highest advertised version |
| Installed version | active version from the install state source |
| Compatibility | host range and platform verdict for the selected release |
| Status | Available, Update, Installed, Restart required, Incompatible, Unavailable, Retry |

Filtering matches name, package ID, publisher and source. The same right-hand details pane pattern as the
installed list shows the full publisher, source, compatibility text, status message and description.

## Install and update flow
1. `Install` / `Update` opens an inline confirmation showing the plugin title and publisher, the manifest
   source URL, and the notice that plugins run code inside SkyCD. Nothing is downloaded before confirmation.
2. Confirming calls `IPluginMarketplaceService.StageInstallAsync`, which forwards the selected release's feed,
   package ID and normalized version to `IPluginPackageInstaller.StageInstallAsync`.
3. On success the item shows `Restart required` with the staged version and the message that it activates
   after SkyCD restarts. The primary action disappears; requests are ignored while a staged version equals
   the selected release, so duplicate installs cannot be queued.
4. On failure the error message is displayed together with whether the previously installed version is still
   active (`Version x is still installed.`) or the plugin is not installed. `Retry` reopens the confirmation
   for the same release; the catalog snapshot is not discarded.
5. Staged operations apply at the next startup, before plugin discovery, as described in
   `catalog-install-staging.md`.

## Catalog states
| Snapshot state | Browse message |
| --- | --- |
| `IsOffline` | catalog could not be reached; installed plugins are still available |
| `HasTotalFailure` | catalog could not be loaded; installed plugins are still available |
| `HasPartialFailure` | some sources failed; available results are shown |
| `IsEmpty` | no plugins are published yet |

Loading uses an indeterminate progress bar above the list; the empty state text appears only after a load
finishes with no items.

## Accessibility
- The `Installed`/`Browse` toggles, filter box and action buttons carry automation names.
- Switching views or loading the catalog never removes keyboard focus targets from the Installed list; the
  Installed section is only hidden when Browse is selected.

## Tests
- `tests/SkyCD.UI.Tests/PluginMarketplaceOptionsTests.cs` covers catalog mapping, duplicate sources,
  filtering, lazy/refresh loading, offline/partial/failure messages, confirmation, staged success,
  restart-required duplicate blocking, retryable failure, and view switching.
- Desktop wiring is `SkyCD.App.Marketplace.PluginMarketplaceService` over `PluginCatalog`,
  `IPluginPackageInstaller` and `IPluginInstallStateSource`; `PluginCatalog` is registered as a singleton in
  `PluginServiceRegistrator` with the running host version.
