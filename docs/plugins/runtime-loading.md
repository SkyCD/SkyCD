# Plugin Runtime Loading

## Discovery Flow
1. Enumerate configured plugin directories and scan DLLs recursively.
2. Skip SDK contract assemblies, intermediate (`obj`), and reference outputs.
3. Load each assembly once, reusing an already loaded assembly when available.
4. Resolve identity and version constraints from assembly metadata, including
   `PluginIdAttribute`, `MinHostVersionAttribute`, and `MaxHostVersionAttribute`.
5. Instantiate concrete `IPluginCapability` implementations with public parameterless constructors.
6. Check host compatibility against minimum and optional maximum host versions.
7. Persist discovered descriptors and expose capabilities from enabled, available plugins.

No `plugin.json` is used. File format, menu, modal, CLI, localization, ownership,
account, and action capabilities all follow this same assembly discovery path.
A plugin may contain several capability classes, and each class may implement
several capability interfaces. Registration exposes the same instance through
all its capability interfaces.

## Diagnostics
- Missing directories are warnings.
- Invalid DLLs and assemblies without usable capabilities are skipped with warnings.
- Incompatible plugins are skipped.

## Capability Lookup
Use `PluginManager.GetCapabilities<TCapability>()` for enabled plugins, or register
plugins with `DiscoveredPlugin.RegisterPluginServices` and resolve capability
interfaces through the existing container. Ownership and account methods and
explicitly selected action handlers receive host cancellation tokens.
