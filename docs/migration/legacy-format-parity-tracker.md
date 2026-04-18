# Legacy File-Format Parity Tracker

This tracker records migration parity status for legacy file formats and related dependencies.

## Dependency Chain
- [x] #65 Plugin SDK vNext contracts and lifecycle
- [x] #66 File format plugin contracts
- [x] #69 Plugin loading runtime
- [x] #71 `*.scd` migration plugin
- [x] #72 `*.cscd` migration plugin
- [x] #73 `*.ascd` migration plugin
- [ ] #70 Migration tooling/docs consume finalized plugins

## Parity Matrix
| Extension | Plugin Package | Read | Write | Fixture Coverage | Notes |
|---|---|---|---|---|---|
| `.scd` | `SkyCD.Plugin.Legacy.Scd` | Yes | Yes | `gamez.scd` parse + round-trip | Text comments/blank lines are ignored |
| `.cscd` | `SkyCD.Plugin.Legacy.Cscd` | Yes | Yes | synthetic compressed round-trip | Shares `.scd` text model with deflate wrapper |
| `.ascd` | `SkyCD.Plugin.Legacy.Ascd` | Yes | Yes | `My Documents.ascd`, `ftpz.ascd`, round-trip | Safe parser accepts only `skycd-nf` + `INSERT INTO list` shape |

## Host Integration Status
- Legacy file format handling is routed through `IFileFormatPluginCapability` in plugin host.
- No host hardcoded extension-specific parser logic is required for `.scd`, `.cscd`, or `.ascd`.

## Known Limitations
- `.ascd` files containing SQL statements outside the supported insert shape are rejected.
- `.scd/.cscd` output formatting may differ while preserving entry data semantics.
