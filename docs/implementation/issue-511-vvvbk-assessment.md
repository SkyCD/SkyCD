# Issue #511: VVV backup (.vvvbk) compatibility assessment

## Verdict

Do not implement direct `.vvvbk` import yet. The available evidence identifies a
Firebird backup/restore stream, but neither a policy-compliant restoration path
nor genuine, redistributable, version-identified backup fixtures has been
established. Native `.vvv` import is also unsupported; see the
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
  invokes Firebird service backup and restore. These observations identify a
  backup stream, not a renamed database file; they do not establish which
  exact source revision produced a particular binary release.
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
so its code must not be copied into SkyCD. The
[FirebirdSql.Data.FirebirdClient 10.3.4 license](https://www.nuget.org/packages/FirebirdSql.Data.FirebirdClient/10.3.4/License)
is IDPL 1.0, which is not allowlisted or covered by an explicit package
exception. Its NuGet metadata lists .NET 10 as computed compatible, not a
verified runtime test; moreover, the client alone is not a backup reader or
Firebird engine. This is not a compliant managed restoration path under the
current policy.

[Firebird's licensing documentation](https://firebirdsql.org/file/documentation/html/en/firebirddocs/qsg5/firebird-5-quickstartguide.html#qsg5-firebird-licenses)
identifies IPL and IDPL licenses for Firebird, neither of which is on the
allowlist. Shipping or invoking isolated tooling does not itself resolve
licensing, compatibility, or safety. A specific proposed tool, distribution,
and all direct and transitive dependencies would need policy and legal review;
using a user-supplied tool has not been assessed. The policy's
[exception process](../legal/dependency-policy.md#exception-process) requires
separate approval; this issue does not change the policy.

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
3. Establish a maintained, .NET 10-compatible and policy-compliant access and
   restoration approach, with an independently reviewed isolation design. Do
   not weaken blocked-license, unknown-license or whitelist handling.
4. Design physical and virtual hierarchy, descriptions, recoverable audio and
   provenance mapping into the SkyCD domain. Add integration tests for real
   fixtures, malformed and truncated input, unsupported versions, encryption,
   resource limits, cancellation, and persisted hierarchy after repository
   recreation before claiming support.
