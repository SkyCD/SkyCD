# Plugin SDK Versioning and Compatibility Policy

## Scope
Applies to the `SkyCD.SDK` package (assembly `SkyCD.SDK`, source project `src/SkyCD.SDK`, namespaces
`SkyCD.SDK.*`) and runtime discovery behavior.

## Semantic Versioning Rules
- SDK uses SemVer: `MAJOR.MINOR.PATCH`.
- `PATCH`: documentation or non-breaking implementation detail changes.
- `MINOR`: backward-compatible additions (new optional members, new capability interfaces).
- `MAJOR`: breaking contract changes (removed/renamed members, changed signatures, behaviorally incompatible changes).

## Host Compatibility
- Every plugin must declare:
  - `IPlugin.Version`
  - `IPlugin.MinHostVersion`
- Runtime compatibility check:
  - plugin is loadable only if `HostVersion >= MinHostVersion`

## Breaking Change Process
1. Propose change in a dedicated issue/PR with impact table.
2. Bump SDK major version.
3. Provide migration notes and sample updates.
4. Keep previous major branch support window documented.

## SDK 3.0 namespace migration (#1114)

The contract namespaces were renamed from `SkyCD.Plugin.Abstractions.*` to `SkyCD.SDK.*`, so the SDK now
has a single identity: assembly `SkyCD.SDK`, package id `SkyCD.SDK` and namespace root `SkyCD.SDK`. This
is a binary- and source-breaking change for plugins compiled against the old namespace. It lands inside the
unreleased `3.0.0` major, so no additional major bump is required and no transition shim is shipped.

| Previous contract | SDK 3.0 replacement |
|---|---|
| `namespace SkyCD.Plugin.Abstractions.*` | `namespace SkyCD.SDK.*` |
| `using SkyCD.Plugin.Abstractions.Capabilities;` | `using SkyCD.SDK.Capabilities;` |
| `[assembly: SkyCD.Plugin.Abstractions.Lifecycle.PluginId("...")]` | `[assembly: SkyCD.SDK.Lifecycle.PluginId("...")]` |
| `clr-namespace:SkyCD.Plugin.Abstractions.Capabilities.Media;assembly=SkyCD.SDK` | `clr-namespace:SkyCD.SDK.Capabilities.Media;assembly=SkyCD.SDK` |
| `SkyCD.SDK.Capabilities.Capture.*` (`ICaptureSourcePluginCapability`, `ICaptureSourceHostApi`, `CaptureSourceKind`, `CaptureDevice`, `CapturePreviewResult`, `CapturedCatalogFile`, …) | `SkyCD.SDK.Capabilities.Sources.*` (`ISourceFactoryPluginCapability`, `ISourceFactoryHostApi`, `SourceKind`, `SourceDevice`, `SourcePreviewResult`, `SourceFile`, …) |
| `SkyCD.SDK.Capabilities.Media.IAudioCdMetadataCapability` / `AudioCdDiscRequest` / `AudioCdCapture` / `AudioCdProviderCapture` / `IAudioCdMetadataProvider` and `SkyCD.SDK.Capabilities.Volume.*` (`IVolumeMetadataCapability`, `VolumeMediaContext`, `VolumeDescriptor`, `VolumeExtractionResult`, `IVolumeMetadataProvider`) | `SkyCD.SDK.Capabilities.Media.IMediaTargetMetadataCapability` / `MediaTarget` / `MediaTargetCapture` / `MediaTargetProviderCapture` / `IMediaTargetMetadataProvider`; `Capabilities.Volume` is deleted |
| `SkyCD.SDK.PluginFailure` | `SkyCD.SDK.Plugins.PluginFailure` |
| `SkyCD.SDK.Capabilities.Catalog.ToolbarContribution` / `ToolbarButtonContribution` | `SkyCD.SDK.Capabilities.Toolbars.*` |
| `SkyCD.SDK.Capabilities.Catalog.ILocationApi` (and the location exceptions, `CatalogLocationInfo`) | `SkyCD.SDK.Capabilities.Catalog.Locations.*` |
| `SkyCD.SDK.Capabilities.Import.ICatalogImportPluginCapability` (and `ImportOutcome`) | `SkyCD.SDK.Capabilities.Catalog.*` |

Only type and namespace names changed. `PluginId`, `FormatId`, attribute values, existing
`[assembly: PluginId(...)]` strings and persisted plugin state keep their exact values. External authors
update the `using` directives, rebuild against `SkyCD.SDK` 3.0 and reinstall the plugin.

### Media target contract (#1338)

`MediaTarget` describes any catalog medium instead of only a drive: `Kind` is one of `Volume`,
`OpticalDisc`, `Folder`, `File`, `NetworkFolder`, `NetworkResource` or `SourceFolder` (a plugin-filled
source folder with no address of its own). `Location` carries the path, UNC path or URL and is `null`
for `SourceFolder`; `Name` is the catalog entry name; `Facts` exposes the entry properties the host
already captured (`VolumeLabel`, `DriveType`, `URL`, ...) under their catalog keys. A capability that
serves several kinds returns `true` from `Supports` for each of them and reads `Kind` and `Location`
in `CaptureAsync`; captures are merged into the media, folder, remote and disc roots alike.

## SDK 3.0 Options unification (#1340)

Tools > Options now lists every plugin page through `IPluginConfigurationCapability` only. Plugins may
contribute several configuration pages, the descriptor gained `Order`, and the page's accept callback is
the place for plugin-side work (managing host state) as well as settings validation. AI provider and ACP
agent management moved out of the host into the plugins that own them.

| Previous contract | SDK 3.0 replacement |
|---|---|
| `IOptionsPageCapability` / `OptionsPageDescriptor` | `IPluginConfigurationCapability.Configurations` with `PluginConfigurationDescriptor.Order` |
| `IPluginConfigurationCapability.Configuration` (one page) | `IReadOnlyList<PluginConfigurationDescriptor> Configurations` |
| Host-owned AI provider settings page (`IAiProviderSettingsCapability`) | Configuration page contributed by `SkyCD.Plugin.Ai.Provider`; settings stay plugin-owned |
| Host-owned ACP agent settings page (`IAcpAgentSettingsCapability`) | Configuration page contributed by `SkyCD.Plugin.Ai.Description`; settings stay plugin-owned |
| `AcpAgentChoice` / `AcpAgentSettings` in `SkyCD.SDK.Capabilities.Media` | Plugin-internal records in `SkyCD.Plugin.Ai.Description` |

`IOptionsNavigationConsumer` remains: it still asks the host to open one of the plugin's configuration
pages by `ConfigurationId`. Persisted plugin settings, configuration IDs and plugin IDs keep their values.

## SDK 3.0 removal of the clipboard and file-dialog host services (#1339)

## SDK 2.0 / SkyCD v3 configuration migration (#828)

The SDK major version is now `2.0.0` (previously the implicit `1.0.0`). Configuration is a breaking
contract change, not a modal compatibility shim. Existing settings dictionaries and plugin IDs remain
unchanged. Rebuild external configuration plugins; this development branch does not provide an SDK 1
configuration adapter. Previously released binaries/branches are not modified by this migration.

| Previous contract | SDK 2.0 replacement |
|---|---|
| `IPluginConfigurationCapability.ConfigureAsync` | `CreatePageAsync` returns an isolated `PluginConfigurationPage` |
| Descriptor `Width` / `Height` | Host-owned Options layout; descriptor keeps ID and title |
| Plugin-owned configuration Window / OK / Cancel | Embedded control plus side-effect-free acceptance callback |
| Immediate persistence after modal OK | Host batch persistence on Options Save, disposal on Cancel/close |

The four production configuration plugins and `Plugins/SkyCD.Plugin.Sample.Config` demonstrate
the new contract. See [SDK contracts](sdk-contracts.md#embedded-options-configuration-sdk-20-migration-828) for
threading, draft ownership, cancellation and headless use. Other capability signatures are unchanged.

## Capability Evolution
- Additive capability changes should prefer new interfaces (e.g. `IMenuPluginCapabilityV2`) to avoid breaking existing plugins.
- Host should probe capabilities dynamically at runtime.

## Runtime Discovery Contract
- Runtime discovers plugin classes implementing `IPlugin`.
- Runtime discovers capabilities by implemented interfaces inheriting `IPluginCapability`.
- Incompatible plugins are skipped, not crashed.

## SDK 3.0 description generator unification (#1341)

Generated descriptions and the Options implementation identity now share one capability. Replace
`IGeneratedDescriptionCapability` with `IDescriptionGeneratorCapability` and remove the separate
`IAiDescriptionImplementationCapability`; the host persists `ImplementationId` for every generator.

| Previous contract | SDK 3.0 replacement |
|---|---|
| `IGeneratedDescriptionCapability` | `IDescriptionGeneratorCapability` (adds `ImplementationId`, `ImplementationName`) |
| `IAiDescriptionImplementationCapability` | Removed; identity lives on `IDescriptionGeneratorCapability` |
| `AiDescriptionImplementationIds` (SDK) | Host-owned constant set in `SkyCD.Plugin.Host.Media`; each plugin owns its own stable id |

`DescriptionGenerationPlan` and `IDescriptionGenerationPlanner` keep their provider-neutral shape.

## SDK 3.0 description entry facts (#1342)

The `Capabilities.GenDesc` area named a single shipped plugin inside the SDK. The helper is generic to any
description generator, so it moved next to the generator contracts in `Capabilities.Media`.

| Previous contract | SDK 3.0 replacement |
|---|---|
| `GenDescEntryContext` | `DescriptionEntryFacts` |
| `IGenDescHostApi.GetEntryContextAsync` | `IDescriptionEntryFactsApi.GetEntryFactsAsync` |
| `IGenDescHostConsumer.InitializeGenDescHost` | `IDescriptionEntryFactsConsumer.InitializeDescriptionEntryFacts` |
