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
| `SkyCD.SDK.PluginFailure` | `SkyCD.SDK.Plugins.PluginFailure` |

Only type and namespace names changed. `PluginId`, `FormatId`, attribute values, existing
`[assembly: PluginId(...)]` strings and persisted plugin state keep their exact values. External authors
update the `using` directives, rebuild against `SkyCD.SDK` 3.0 and reinstall the plugin.

## SDK 3.0 removal of the clipboard and file-dialog host services (#1339)

`IClipboardApi` / `IClipboardConsumer` and `IHostFileDialogApi` / `IHostFileDialogConsumer` are removed from
`SkyCD.SDK`: a plugin already references Avalonia, so it opens pickers through its own top level
(`TopLevel.StorageProvider`) and sets clipboard text through `TopLevel.Clipboard`. The host keeps supplying
windows through `IHostDialogApi`, so a command that has no control on screen shows a plugin-owned window
first and picks from that window. Plugins compiled against the old contracts drop the two consumer
interfaces and their `InitializeClipboard` / `InitializeFileDialogApi` methods; `Capabilities.Ui` keeps
dialogs, questions, notifications and the host command API.

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
