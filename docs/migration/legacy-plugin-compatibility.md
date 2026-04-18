# Legacy Plugin Compatibility Matrix (v2 -> v3)

| Legacy Plugin | Legacy Extension(s) | v3 Status | Notes |
|---|---|---|---|
| TextFormat | `.scd` | Implemented | v3 plugin `SkyCD.Plugin.Legacy.Scd` with read/write + fixture round-trip tests (#71) |
| CompressedTextFormat | `.cscd` | Implemented | v3 plugin `SkyCD.Plugin.Legacy.Cscd` with compressed round-trip tests (#72) |
| SkyCDNativeFormat | `.ascd` | Implemented | v3 plugin `SkyCD.Plugin.Legacy.Ascd` with safe parser, header validation, and security tests (#73) |

## Binary Compatibility
- v2 `.NET Framework` plugin binaries are **not** loaded in v3 runtime.
- Migration path: port plugin to `SkyCD.Plugin.Abstractions` and provide `plugin.json` manifest.

## Known Compatibility Limits
- `.scd` output normalizes size formatting (`KB/MB/GB`) and skips comment/empty lines.
- `.ascd` importer only accepts `# format: skycd-nf <version>` plus `INSERT INTO list ... VALUES (...)` statements.
- `.ascd` payload is parsed as data only; no SQL execution, additional SQL statements, or script batches are supported.
