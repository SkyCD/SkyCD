# Plugin SDK Versioning and Compatibility Policy

## Scope
Applies to `SkyCD.Plugin.Abstractions` and runtime discovery behavior.

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

The four production configuration plugins and `Plugins/SkyCD.Plugin.Sample.Configuration` demonstrate
the new contract. See [SDK contracts](sdk-contracts.md#embedded-options-configuration-v3-migration-828) for
threading, draft ownership, cancellation and headless use. Other capability signatures are unchanged.

## Capability Evolution
- Additive capability changes should prefer new interfaces (e.g. `IMenuPluginCapabilityV2`) to avoid breaking existing plugins.
- Host should probe capabilities dynamically at runtime.

## Runtime Discovery Contract
- Runtime discovers plugin classes implementing `IPlugin`.
- Runtime discovers capabilities by implemented interfaces inheriting `IPluginCapability`.
- Incompatible plugins are skipped, not crashed.
