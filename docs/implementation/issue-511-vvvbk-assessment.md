# Issue #511: VVV backup (.vvvbk) compatibility assessment

## Verdict

Do not implement direct `.vvvbk` import yet. The available evidence identifies a
Firebird backup/restore stream, and IDPL 1.0 for the managed Firebird client is
now an approved package exception ([#618](https://github.com/SkyCD/SkyCD/issues/618),
[PR #620](https://github.com/SkyCD/SkyCD/pull/620)), but genuine,
redistributable, version-identified backup fixtures and a verified restore path
have not been established. Native `.vvv` import is also unsupported; see the
[separate assessment](issue-455-vvv-compatibility.md). For now, restore the
backup with a compatible VVV installation to a **new** catalog, then export
its physical data for the [existing CSV/TSV importer](../plugins/vvv.md).
Keep the original backup unchanged. This route loses data listed below.

This assessment adds no importer, dependencies, or fixtures. It is based on
source and documentation review, not an executed restore: no genuine `.vvvbk`
was inspected or tested, and none was found among tracked repository fixtures
or the reviewed sources. No claim is made about all available releases or
fixtures elsewhere.

## Format, signatures and versions

- Issue #511 identifies `.vvvbk` as VVV 1.5's backup format. At upstream
  revision 467, the [Windows version resource](https://sourceforge.net/p/vvvapp/code/467/tree/trunk/src/vvv.rc)
  labels the source 1.5; its [backup and restore UI](https://sourceforge.net/p/vvvapp/code/467/tree/trunk/src/mainframe.cpp)
  uses the `.vvvbk` extension and routes operations through Firebird backup
  and database creation. The [restore dialog](https://sourceforge.net/p/vvvapp/code/467/tree/trunk/src/restore.cpp)
  selects a backup and a new `.vvv` catalog. The [database interface](https://sourceforge.net/p/vvvapp/code/467/tree/trunk/src/data_interface/firebird_db.cpp)
  routes backup and restore through Firebird's service API, which requires a
  compatible Firebird server or engine, not only a client library. These
  observations identify a backup stream, not a renamed database file; they do
  not establish which exact source revision produced a particular binary
  release.
- [Firebird's gbak documentation](https://www.firebirdsql.org/file/documentation/html/en/firebirddocs/gbak/firebird-gbak.html)
  describes logical backups of database metadata and data. Transportable
  backups are the default, but nontransportable backups can be requested.
  The reviewed VVV sources and absence of fixtures do not establish the actual
  backup format version, platform portability, or required restoring Firebird
  versions for each VVV release. Firebird backup format, the restored database's
  on-disk structure (ODS), and VVV's application schema version are separate
  compatibility dimensions.
- No `.vvvbk` header bytes or reliable signature were verified. A file
  extension alone is not sufficient to identify a valid or supported backup.
  Encryption is a possible Firebird backup option, not a demonstrated property
  of VVV backups; detection and rejection need real test evidence.
- The reviewed [VVV schema](https://sourceforge.net/p/vvvapp/code/492/tree/trunk/VVV.sql)
  contains `SERVICE.DB_VERSION` as an integer and physical, virtual, and audio
  tables. The 1.5-labeled [database interface](https://sourceforge.net/p/vvvapp/code/467/tree/trunk/src/data_interface/firebird_db.cpp)
  reads that marker and applies ordered upgrade scripts; it also handles very
  old databases without `SERVICE`. The schema is not a verified per-release
  version map. Do not assume a missing marker proves corruption, or that all
  versions have this exact schema; supported versions must be established from
  fixtures without upgrading the source catalog.
- VVV's [version history](https://www.fsoft.it/VVV/versions.html) records
  backup/restore and Unicode in 0.9 and optional audio metadata in 1.2. This
  is feature history, not evidence of cross-version backup compatibility.

## Compatibility matrix

| Path | SkyCD support today | Potential data and limitations |
|---|---|---|
| `.vvvbk` direct import | No | Restore could expose catalog data, including physical and virtual structure and audio metadata, subject to the source schema and a safe compatible engine; none is verified by a fixture. |
| Native `.vvv` | No | A Firebird database may contain the same structures, but the [native assessment](issue-455-vvv-compatibility.md) found no compliant, verified import path. |
| VVV physical CSV/TSV export | Yes | Reconstructs non-empty physical folders and volumes with names, sizes, extensions, raw locale-formatted modification text and file descriptions. Empty folders, folder descriptions, volume metadata, virtual membership and audio metadata are not exported. |
| VVV virtual CSV/TSV export | Yes | Includes physical origin paths but flattens virtual structure; it does not preserve virtual hierarchy or audio metadata. |

The current exporter/importer path retains export scope and relevant volume or
physical-path properties, but not native catalog IDs or complete provenance.
The reviewed schema includes `VOLUMES`, `PATHS`, `FILES`,
`FILES_AUDIO_METADATA`, `VIRTUAL_PATHS` and `VIRTUAL_FILES`. Some `FILES` and
virtual membership records represent folders rather than ordinary files, so
future mapping must distinguish them and preserve links rather than infer a
simple file-only hierarchy.

## Licensing and restoration safety

[SkyCD's policy](../legal/dependency-license-policy.json) allowlists specific
licenses, rejects unknown licenses by default and blocks GPL-family licenses.
VVV is [GPL-2.0-or-later](https://sourceforge.net/p/vvvapp/code/492/tree/trunk/COPYING),
so its code must not be copied into SkyCD.

The
[FirebirdSql.Data.FirebirdClient 10.3.4 license](https://www.nuget.org/packages/FirebirdSql.Data.FirebirdClient/10.3.4/License)
is IDPL 1.0, which has no SPDX identifier, so CycloneDX reports it as unknown.
Issue [#618](https://github.com/SkyCD/SkyCD/issues/618) and
[PR #620](https://github.com/SkyCD/SkyCD/pull/620) record IDPL 1.0 approval for
this package as an explicit unknown-license exception under the policy's
[exception process](../legal/dependency-policy.md#exception-process). Any use
must consume the package unmodified as a separate library, retain its notice
and license text, and meet source-availability and modification-disclosure
obligations for covered code. This exception covers missing license metadata
only; explicitly blocked licenses still fail validation. Its NuGet metadata
lists .NET 10 as computed compatible, not a verified runtime test.

The approval does not cover Firebird engine binaries, IPL 1.0, or GPL VVV
source, and the managed client alone is not a backup reader or a Firebird
engine: gbak backup and restore require a compatible Firebird service. Shipping
or invoking isolated tooling therefore still needs its own license review. A
specific proposed tool, distribution, and all direct and transitive dependencies
would need policy and legal review; using a user-supplied tool has not been
assessed.

Restoring an untrusted backup also needs stronger safeguards than choosing a
temporary filename. Firebird documents create versus replacement modes and
that restore can recreate shadow files; `-KILL` suppresses shadow recreation.
A future implementation must verify safe create-only behavior, constrain
filesystem and process/network access with OS-enforced isolation, suppress or
block unsafe external references, plugins and database triggers, and enforce
input, disk, memory, CPU, time, row count and hierarchy-depth limits. It must
leave the source and existing catalogs untouched, read the restored database
without migrations, validate schema and records, and clean up on cancellation
or failure. Malformed, truncated, encrypted, unsupported and resource-exhausting
inputs need actionable diagnostics. These are requirements to verify, not
protections implemented by this assessment.

## Revisit conditions

1. Obtain genuine backups from identified VVV releases, including 1.5 and,
   where available, another version. Record author permission and redistribution
   terms, source application and Firebird versions, provenance and checksums.
   Include multiple volumes, Unicode names, descriptions, empty and virtual
   folders, duplicate links and audio metadata where available.
2. Independently inspect and restore those fixtures in a controlled environment
   to establish backup signatures and versions, compatible Firebird versions,
   ODS and `SERVICE.DB_VERSION`/schema mappings, including older schemas.
3. Establish a maintained and .NET 10-compatible access and restoration
   approach with an independently reviewed isolation design. The client's IDPL
   1.0 exception covers the managed client only; any Firebird engine or helper
   tooling used for restore needs its own policy review. Do not weaken
   blocked-license, unknown-license or whitelist handling.
4. Design physical and virtual hierarchy, descriptions, recoverable audio and
   provenance mapping into the SkyCD domain. Add integration tests for real
   fixtures, malformed and truncated input, unsupported versions, encryption,
   resource limits, cancellation, and persisted hierarchy after repository
   recreation before claiming support.
