# Plugin Runtime Loading

## Manifest Format (`plugin.json`)
```json
{
  "id": "skycd.plugin.sample.json",
  "version": "1.0.0",
  "minHostVersion": "3.0.0",
  "assembly": "SkyCD.Plugin.Sample.Json.dll",
  "capabilities": ["file-format"]
}
```

## Discovery Flow
1. Enumerate configured plugin directories.
2. Find `plugin.json` recursively.
3. Validate manifest fields.
4. Check host compatibility (`hostVersion >= minHostVersion`).
5. Load assembly (default or isolated `AssemblyLoadContext`).
6. Discover `IPlugin` implementations and capabilities.
7. Return discovered plugins + diagnostics.

## Diagnostics
- Missing directories are warnings.
- Invalid manifests, missing assemblies, and load failures are errors.
- Incompatible plugins are skipped with informative diagnostics.

## Unload/Reload Strategy
- Isolation mode uses collectible `AssemblyLoadContext`.
- Recommended host behavior for reload:
  1. Dispose plugin instances (`IAsyncDisposable`)
  2. Release capability references
  3. Unload the associated load context
  4. Trigger GC cycle before reloading
