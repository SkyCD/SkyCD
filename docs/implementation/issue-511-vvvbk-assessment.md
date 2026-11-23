# Issue #511: VVV backup (.vvvbk) import assessment

## Verdict

SkyCD directly imports `.vvvbk` backups read-only when a compatible Firebird 5
embedded engine is available. The importer is implemented in
`Plugins/SkyCD.Plugin.Vvv` and registered as the `vvv-backup` file format.
It restores the backup into a disposable temporary workspace, validates the VVV
schema, maps physical volumes, folders, files, virtual folders, descriptions and
audio metadata, and leaves the source file and existing catalogs untouched.

The engine is **not bundled**. Installing native Firebird engine binaries is a
separate licensing decision, so the plugin looks for a user-supplied engine and
returns an actionable error when none is found. Native `.vvv` catalogs remain
unsupported; see the
[native assessment](issue-455-vvv-compatibility.md).

## Evidence and format

- Issue #511 identifies `.vvvbk` as VVV 1.5's backup format. At upstream
  revision 467, the [Windows version resource](https://sourceforge.net/p/vvvapp/code/467/tree/trunk/src/vvv.rc)
  labels the source 1.5; its [backup and restore UI](https://sourceforge.net/p/vvvapp/code/467/tree/trunk/src/mainframe.cpp)
  uses the `.vvvbk` extension and routes operations through Firebird backup and
  database creation. The [database interface](https://sourceforge.net/p/vvvapp/code/467/tree/trunk/src/data_interface/firebird_db.cpp)
  uses Firebird's service API.
- A genuine VVV 1.5 backup fixture is checked in at
  `tests/SkyCD.Plugin.Vvv.Tests/Fixtures/VvvBackup/VVV-1.5-fixture-backup.vvvbk`
  (41,472 bytes, SHA-256
  `31739c79194b245a06f2fa1dcdd557c19c48e514982434daff67ca681c09706f`).
  It is a Firebird logical backup (gbak stream) with `SERVICE.DB_VERSION = 23`.
  Its provenance is recorded in the fixture directory. No VVV source code is
  copied into SkyCD.
- The fixture was restored and inspected with the Firebird 5 embedded engine.
  Restoring it produces one volume (`Fixture volume`), the folders `sample` and
  `folder`, `hello.txt` (39 bytes) and `nested.txt` (22 bytes), and the source
  file hash is unchanged after import.
- A derived negative fixture with `SERVICE.DB_VERSION = 999` is used only for
  unsupported-version tests. It is not genuine VVV output and is documented as
  such in the fixture provenance.
- [Firebird's gbak documentation](https://www.firebirdsql.org/file/documentation/html/en/firebirddocs/gbak/firebird-gbak.html)
  describes logical backups of metadata and data. Backup format, the restored
  database's on-disk structure, and VVV's `SERVICE.DB_VERSION` are separate
  compatibility dimensions.
- The reviewed [VVV schema](https://sourceforge.net/p/vvvapp/code/492/tree/trunk/VVV.sql)
  contains `SERVICE.DB_VERSION` and physical, virtual and audio tables. VVV's
  [version history](https://www.fsoft.it/VVV/versions.html) records backup and
  Unicode in 0.9 and optional audio metadata in 1.2.

## Compatibility

| Path | SkyCD support | Preserved data and limitations |
|---|---|---|
| `.vvvbk` direct import | Read-only, schema versions 14 through 23 | Physical volumes and hierarchy, folder and file descriptions, volume metadata, virtual folders and links, audio metadata, stable node ids and provenance properties. Requires a Firebird 5 embedded engine. |
| Native `.vvv` | No | A Firebird database may contain the same structures, but the [native assessment](issue-455-vvv-compatibility.md) found no compliant, verified import path. |
| VVV physical CSV/TSV export | Yes | Reconstructs physical volumes and folders, but loses empty folders, folder descriptions, volume metadata, virtual membership and audio metadata. |
| VVV virtual CSV/TSV export | Yes | Flattens virtual structure and does not preserve audio metadata. |

Schema version 23 is fixture-verified end to end. Versions 14 through 22 are
accepted by the supported range and validated structurally after restore, but
no genuine fixture for those versions is available yet. The version range must
not be widened without fixture evidence.

## Implementation and safety

- `VvvBackupPlugin` is a read-only `IFileFormatPluginCapability` for
  `.vvvbk`. It deliberately does not implement
  `IContentAwareFileFormatCapability`, so it cannot hijack CSV/TSV reads.
- `VvvBackupEngineLocator` resolves a Firebird 5 embedded engine from
  `SKYCD_FIREBIRD_ENGINE`, the plugin directory, the application base directory,
  or `%LOCALAPPDATA%\SkyCD\firebird\v5`. Missing client library or
  `Engine13` provider produces a `VvvBackupEngineUnavailableException` that
  lists the searched locations.
- `VvvBackupWorkspace` copies the engine and the backup into
  `%TEMP%\SkyCD\vvvbk\<guid>`. The backup stream is capped at 4 GiB and copied,
  never modified. The workspace is deleted on success, failure and
  cancellation, with a bounded retry for files still held by the engine.
- The staged `firebird.conf` restricts the engine: `DatabaseAccess` is limited
  to the workspace, `RemoteAccess = false`, `ExternalFileAccess = None`,
  `UdfAccess = None`, temporary files stay in the workspace, only the
  `Engine13` provider is enabled, and `WireCrypt = Disabled`.
- `VvvBackupReader` performs a create-only, read-only restore
  (`FbRestoreFlags.Create | NoShadow`, `ReadOnly = true`) and waits for
  completion. It attaches with `NoDatabaseTriggers`, lists user tables, requires
  `SERVICE`, `VOLUMES`, `PATHS` and `FILES`, accepts schema versions 14 through
  23, and reads audio and virtual tables only when present. No migrations are
  run.
- Limits: 4 GiB backup, 2,000,000 rows, and 256 hierarchy levels. Parent cycles
  are broken deterministically and reported in the import notes.
- `VvvBackupHierarchyBuilder` maps volumes to media nodes, paths to folder
  nodes and file rows to catalog files. Virtual folders become folder nodes
  under a `Virtual Folders` media root; additional placements of the same
  folder are emitted as explicit link rows. Provenance properties include VVV
  ids, scope, source file, schema version, physical paths, descriptions, dates
  and audio metadata keys used by other SkyCD plugins.
- Failures are returned as actionable messages: engine unavailable, empty
  stream, non-backup data, restore failure, missing VVV schema, unsupported
  schema version, malformed tables, or exceeded limits. Cancellation is
  observed before restore and while reading rows; a native restore call itself
  is synchronous and is not interrupted mid-call.

A backup truncated after all schema records can still restore a structurally
valid but incomplete database. The importer rejects files whose restore does not
produce the required VVV schema or version, but it cannot recover rows that are
absent from the file. Callers should treat a backup with uncertain integrity as
suspect.

The isolation is process-local, not OS-enforced. The engine guardrails reduce
filesystem, network and plugin exposure, but they do not protect against a
malicious native engine binary. Only a trusted Firebird distribution should be
configured.

## Licensing

[SkyCD's policy](../legal/dependency-policy.md) fails on licenses that Trivy
classifies as CRITICAL/HIGH and reports unknown licenses without failing.
VVV is [GPL-2.0-or-later](https://sourceforge.net/p/vvvapp/code/492/tree/trunk/COPYING),
so its code must not be copied into SkyCD; the importer uses only the documented
schema and fixture evidence.

The
[FirebirdSql.Data.FirebirdClient 10.3.4 license](https://www.nuget.org/packages/FirebirdSql.Data.FirebirdClient/10.3.4/License)
is IDPL 1.0, which has no SPDX identifier and is declared through a legacy
`licenseUrl`, so the license scan reports it as unknown and does not fail.
Issue [#618](https://github.com/SkyCD/SkyCD/issues/618) and
[PR #617](https://github.com/SkyCD/SkyCD/pull/617) record the IDPL 1.0 approval
for this package. The package is consumed unmodified as a separate library and
its notice and license text must be retained.

That exception does **not** cover Firebird engine binaries, IPL 1.0, or GPL VVV
source. The plugin therefore does not ship an engine. A user-supplied engine is
governed by its own distribution terms. Any future bundling or automatic
download needs its own policy and legal review, including the native engine
package and its transitive dependencies.

## Tests

`tests/SkyCD.Plugin.Vvv.Tests/VvvBackupImportTests.cs` covers:

- format registration and read-only behavior, including not being
  content-aware;
- cancellation before reading;
- physical hierarchy, descriptions, volume metadata and audio metadata mapping;
- virtual folders, links and secondary placements;
- stable ids, parent-cycle breaking and depth limits;
- rejection of invalid data, truncated input and unsupported schema versions;
- genuine fixture import with unchanged source hash;
- persistence of the imported hierarchy across Couchbase repository
  recreation, including tree and browser contents.

Engine-dependent tests return early when no engine is available, so CI still
validates the mapping logic. Running them with an engine is done by pointing
`SKYCD_FIREBIRD_ENGINE` at a Firebird 5 engine directory.

## Remaining work

1. Obtain genuine fixtures from additional VVV releases and schema versions,
   including Unicode names, multiple volumes, empty folders, duplicate virtual
   links and audio metadata.
2. Decide whether to bundle or download the Firebird 5 engine. That requires
   legal approval for the native binaries and, if installation is exposed to
   users, a consent flow that can deactivate the plugin on refusal.
3. Replace process-local guardrails with OS-enforced isolation if untrusted
   backups must be supported. An AppContainer proof of concept did not succeed
   in this task.
4. Add fixture evidence before widening the accepted schema range or claiming
   support for encrypted backups.
