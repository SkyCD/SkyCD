# Legacy Plugin Compatibility Matrix (v2 -> v3)

| Legacy Plugin | Legacy Extension(s) | v3 Status | Notes |
|---|---|---|---|
| TextFormat | `.scd` | Planned | Re-implemented as v3 file-format plugin (#71) |
| CompressedTextFormat | `.cscd` | Planned | Re-implemented as v3 file-format plugin (#72) |
| SkyCDNativeFormat | `.ascd` | Planned | Re-implemented as v3 file-format plugin (#73) |

## Binary Compatibility
- v2 `.NET Framework` plugin binaries are **not** loaded in v3 runtime.
- Migration path: port plugin to `SkyCD.Plugin.Abstractions` and provide `plugin.json` manifest.
