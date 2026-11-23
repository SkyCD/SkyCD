# Issue #455: Investigate native Virtual Volumes View (.vvv) catalog compatibility

## Verdict: no-go for native `.vvv` import at this time

A native importer would have to embed or shell out to a Firebird engine: a
`.vvv` catalog is a Firebird relational database with no standalone magic
signature, and every compliant .NET access path is either license-blocked or
disproportionate. The existing `vvv` plugin (CSV/TSV export import, #452)
already covers the physical hierarchy, names, sizes, extensions, timestamps and
descriptions. Revisit only with (a) a policy decision on a Firebird client
dependency and (b) redistributable fixtures from identified VVV versions.

## Evidence

All findings below come from reading the VVV application sources (local
reference dump, not part of this repository; originals never modified, nothing
executed) and the in-repo `vvv` plugin. No live catalog was opened: no Firebird
engine is available in this environment.

### Container and format

- A `.vvv` catalog is a Firebird database file (`vvvapp-code/src/
  data_interface/firebird_db.cpp`, `CFirebirdDB`; the dump also ships
  `VVV.fbk`, a `gbak` backup, and a live `vvv-struct-update.fdb`).
- The first 64 bytes of the sample database carry no magic string — Firebird
  database files start with a binary header page, so content sniffing without
  an engine is not possible, and support must never be claimed from the
  extension alone.
- VVV itself is GPL-2.0 (`vvvapp-code/COPYING`), rejected as Restricted/HIGH by the
  gate in `docs/legal/dependency-policy.md`; upstream code must not be
  copied, and GPL-licensed helpers are unavailable as dependencies.

### Schema and versions (`vvvapp-code/VVV.sql`, generated 2024-05-02)

| Table | Contents |
|---|---|
| `VOLUMES` | `VOLUME_ID`, `VOLUME_NAME` (unique), description, physical path, catalog/last-update dates |
| `PATHS` | `PATH_ID`, `VOLUME_ID`, `PATH_NAME`, `FATHER_ID` tree, description |
| `FILES` | `FILE_ID`, name, `BIGINT` size, extension, timestamp, `PATH_ID`, description |
| `FILES_AUDIO_METADATA` | 12 track fields (title/artist/album/year/comment/number/genre/bitrate/sample-rate/channels/length) per `FILE_ID` |
| `VIRTUAL_PATHS` | `PATH`, `FATHER_ID`, optional `PHYS_PATH_ID` link |
| `VIRTUAL_FILES` | `VIRTUAL_PATH_ID` to `PHYSICAL_FILE_ID` links |
| `SERVICE` | `DB_VERSION` for scripted migrations (`UPDATE_SCRIPTS`) |

Physical hierarchy is `VOLUMES → PATHS → FILES`; the virtual hierarchy is a
separate link layer over the same physical items. Exact per-release
`DB_VERSION` numbers live in the migration scripts, not in source; any native
reader would have to track them.

### What the CSV/TSV export carries (`vvvapp-code/src/exportdata.cpp`)

- Physical scope: `Volume | Path | Name | Size | Ext | Last modified |
  Description`; virtual scope: `Name | Size | Ext | Last modified |
  PhysicalPath | Description`.
- Separators `;` (default), TAB or `|`; headers optional; dates are
  locale-formatted (`FormatDate + FormatTime`); sizes are raw integers, so
  files over 4 GiB survive the export.
- Not exported: all audio metadata, volume catalog/update dates, path
  descriptions, virtual membership structure (virtual scope flattens to one
  physical path per file), `DB_VERSION`, internal IDs.

The in-repo `vvv` plugin parses exactly these two layouts
(`Plugins/SkyCD.Plugin.Vvv/VvvParser.cs`), including headerless files.

## Compatibility matrix

| Data | CSV/TSV today | Native `.vvv` potential | Notes |
|---|---|---|---|
| Physical tree, names, sizes, ext, mtimes, descriptions | Yes | Yes | Sizes exact in both (BIGINT vs raw integer text) |
| Virtual folder membership | Flattened only | Full link structure | Main structural benefit of native |
| Audio metadata (12 fields) | No | Yes | Second benefit; niche audience |
| Volume catalog/update dates, path descriptions | No | Yes | Minor |
| DB version / internal IDs | No | Yes | Diagnostics, not user data |
| Timestamps | Locale-formatted | Native `TIMESTAMP` | Robustness benefit |

## Why native is not justified now

1. **No compliant access path.** The managed Firebird client is distributed
   under the IDPL, which is neither allowed nor blocked by the dependency
   policy, and the policy fails closed on unknown licenses. Embedding the
   engine or shelling out to `gbak` adds native binaries and an external tool
   dependency for one format.
2. **No verifiable fixtures.** No redistributable catalogs from identified VVV
   versions could be obtained, so a native codec could not be tested against
   genuine files; the schema above comes from DDL, not from observed databases.
3. **Version drift without a reader.** `DB_VERSION` migrates via scripts;
   supporting "multiple versions" means tracking Firebird ODS plus
   application migrations.
4. **Hand-rolled page parsing is disproportionate.** A pure-managed Firebird
   page reader is thousands of lines for a niche benefit when CSV already
   imports the core catalog data.

## Proposed bounded follow-ups (only if the blockers lift)

- If redistributable fixtures appear: add them under
  `tests/SkyCD.Plugin.Vvv.Tests/Fixtures/` with provenance and extend the CSV
  plugin's locale/date coverage.
- If a license decision permits a Firebird client: spike a read-only
  `vvv-native` plugin reusing this report's schema map, gated on fixture
  availability; keep the CSV path as the offline fallback.
- Document the recommended VVV export settings (physical scope, headers on)
  in `docs/plugins/vvv.md`.

## Limits honoured

Original catalogs never modified; no embedded content executed (only header
bytes, DDL and export code paths were read); no upstream code copied; no
support claimed from the extension.
