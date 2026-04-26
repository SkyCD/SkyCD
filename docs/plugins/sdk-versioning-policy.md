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

## Capability Evolution
- Additive capability changes should prefer new interfaces (e.g. `IMenuPluginCapabilityV2`) to avoid breaking existing plugins.
- Host should probe capabilities dynamically at runtime.

## Runtime Discovery Contract
- Runtime discovers plugin classes implementing `IPlugin`.
- Runtime discovers capabilities by implemented interfaces inheriting `IPluginCapability`.
- Incompatible plugins are skipped, not crashed.
