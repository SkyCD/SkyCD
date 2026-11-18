# Issue #589: abeMeda/NeoFinder native catalog fixtures

## Status (2026-11-18)

As of 2026-11-18, **no genuine application-created native catalogs are present
in this repository**. This covers all three catalog generations of the
abeMeda/NeoFinder/CDFinder family: classic, extended, and pre-CDFinder-2.8
legacy. Every committed abeMeda/NeoFinder artifact today is either a synthetic
XML fixture (`tests/SkyCD.Plugin.AbeMeda.Tests/Fixtures/AbeMeda`) or a
container generated at runtime by `SyntheticCatalog`
(`tests/SkyCD.Plugin.AbeMeda.Native.Tests/AbeMedaNativeFixture.cs`). No vendor
catalog bytes are committed.

The classic header offsets used by
[`abemeda-native`](../plugins/abemeda-native.md) were observed from a
vendor-shipped `abeMeda Demo Catalog.cdw` (7.8.7 line). That file is not
committed, has no recorded redistribution permission, and is not part of the
fixture set described here; it does not satisfy this issue.

Consequently, **all three generations remain closed to native-reader work until
verified, redistributable fixtures with provenance are donated**. This is a
project availability gap, not a finding that any generation is technically
impossible to read. Support through the XML and text export importers is
unaffected; see [abeMeda XML](../plugins/abemeda-xml.md) and
[NeoFinder text](../plugins/neofinder-text.md).

This note is the written record for #589. A future donation reopens the
corresponding generation through a new tracked change; this note must then be
updated with the accepted files and their provenance.

## Required fixture set

| Generation | Accepted suffixes | Version requirement | Why it is required |
|---|---|---|---|
| Classic | `.neofinder` or `.cdw` | Identified NeoFinder, CDFinder, or abeMeda version | Validates the classic container and, once available, the item tables that are currently undecoded. |
| Extended | `.neofinder7` or `.abemeda7` | Identified version; the Extended Catalog File Format arrived with NeoFinder 7 | The extended generation is separate. The classic layout must never be applied to it. |
| Legacy (pre-CDFinder 2.8) | Not prescribed by the issue; record the donated suffix | Identified pre-CDFinder-2.8 version | A separate generation. The vendor converts these only through its own Importer, so a native conversion route cannot be verified without an original. |

Notes:

- The minimum set is one classic and one extended catalog. A legacy catalog is
  desirable but not required; if none can be sourced, a written statement
  recording that it is unavailable to the project keeps that generation closed.
- Prefer catalogs created over a dedicated synthetic volume (fake or empty
  folders and files) so that the donated content is redistributable from the
  start and nothing has to be stripped afterwards.
- The current `abemeda-native` descriptor registers `.cdw` (classic),
  `.abeMeda7`, and `.neofinder7`. A classic donation using the bare
  `.neofinder` suffix is accepted by this issue but would need suffix
  registration before a reader could reach it through the normal open flow.
  That is reader work, not fixture work, and is out of scope here.

## Provenance record (required per file)

The provenance note must accompany every committed fixture. A file with an
incomplete record is not accepted, and a file that may not be committed cannot
close a generation even if it is available for local inspection.

| Field | Requirement |
|---|---|
| Creating application | NeoFinder, CDFinder, abeMeda, or CDWinder, as reported by the donor. "Unknown" is not acceptable. |
| Application version | Full version (and build if shown) of the creating application. The generation cannot be closed without an identified version. |
| Platform | macOS or Windows, with the OS version used to create the catalog. |
| Suffix | Exact suffix as donated, case preserved. |
| Generation | Classic, extended, or legacy, as stated by the donor and corroborated by the file where possible. |
| Donor permission | Donor identity (or "anonymous") plus an explicit written statement that SkyCD may commit and redistribute the exact file under the repository [license](../../LICENSE) (BSD-2-Clause), and that the catalog content is synthetic or otherwise redistributable. |
| Content summary | What the catalog holds (for example: one synthetic volume, folders/files, Unicode names, duplicate names, empty folders) and confirmation that it contains no personal or copyrighted file payloads. |
| Capture date | ISO 8601 date on which the catalog was created or exported by the creating application. |
| Record date | ISO 8601 date on which the provenance record was written. |
| Checksum | SHA-256 of the exact committed bytes, computed on the donated copy before commit. |
| Size | Byte length of the file. |
| Sanitization | Actions taken, ideally "none" because the catalog was created over synthetic content. See the rule below on byte editing. |
| Notes | Known limitations, features intentionally included, and anything a reader test should target. |

## Donation and sanitization process

1. Ask the donor to create a **new** catalog with their installed application
   over a dedicated synthetic or throwaway volume. Do not ask for edits to an
   existing personal catalog, and do not accept a catalog that mixes personal
   content with synthetic content.
2. The donor closes the creating application, copies the catalog, and keeps the
   original. Record the creating application, version, platform, capture date,
   and content summary while the context is still known.
3. Compute the SHA-256 and byte length of the donated copy and record them.
   Inspect the leading bytes for analysis; for the classic generation, confirm
   the `DWDRDNBA` signature and the `0xA5A5A5A5` sentinel rather than trusting
   the suffix. Extended-generation recognition is currently suffix-based and
   remains unverified until the first fixture arrives.
4. **Never execute the catalog or any embedded content, and never follow
   external references or paths.** Do not open the file in the vendor
   application, do not resolve previews or thumbnails, and treat all embedded
   payloads as opaque bytes.
5. **Do not edit the bytes of a native catalog.** Stripping strings, zeroing
   regions, or any other byte-level "sanitization" destroys the
   application-created property and makes the file useless as evidence. If a
   catalog cannot be redistributed as created, it is not accepted; the remedy is
   a new catalog over synthetic content.
6. Obtain the written donor permission described in the provenance record
   before the file enters the repository. Without it, the file may be inspected
   locally but must not be committed or referenced by CI.
7. Add the file and its provenance note, then have the checksum verified again
   during review. Only after that may reader work cite the fixture.
8. Keep originals and working copies outside the repository. Temporary copies
   are never committed.

Do not download catalogs from third-party sites or accept files of unknown
provenance.

## Where accepted fixtures live

Accepted fixtures belong in the native plugin's test project under its
`Fixtures/` directory, following the repository convention used by other format
tests (`tests/<Plugin>.Tests/Fixtures/<Plugin>/` with a README that records
provenance):

```
tests/SkyCD.Plugin.AbeMeda.Native.Tests/Fixtures/AbeMedaNative/<original-file-name>
tests/SkyCD.Plugin.AbeMeda.Native.Tests/Fixtures/AbeMedaNative/README.md
```

The README carries one provenance entry per file using the fields above, in the
style of `tests/SkyCD.Plugin.BrokenX.Tests/Fixtures/README.md` and
`tests/SkyCD.Plugin.DiskCatalogMaker.Tests/Fixtures/DiskCatalogMakerV5/README.md`.
If a file needs a longer record, add a sidecar `<file>.provenance.md` next to
it. No such directory or placeholder is created by this issue.

## Compatibility and readiness matrix (2026-11-18)

| Generation | Accepted suffixes | Fixture status | Native reader status |
|---|---|---|---|
| Classic | `.neofinder`, `.cdw` | **None in repository; blocked on donation** | Header and 36-byte record layer implemented and tested against synthetic bytes only. Item hierarchy, names, paths, comments, labels, ratings, and media metadata are not decoded. The bare `.neofinder` suffix is not registered. |
| Extended | `.neofinder7`, `.abeMeda7` | **None in repository; blocked on donation** | Recognized and rejected with an actionable diagnostic. No extended layout is claimed. |
| Legacy (pre-CDFinder 2.8) | Record the donated suffix | **None in repository; blocked on donation** | Not recognized. No parsing is claimed. |

"None in repository" means zero files with a complete provenance record and
donor permission; it does not mean the format is unsupported or unreadable.

## Relationship to existing import routes

- [abeMeda XML export import](../plugins/abemeda-xml.md) (`abemeda-xml`) and
  [NeoFinder text export import](../plugins/neofinder-text.md)
  (`neofinder-text`, implementation note
  [issue-454](issue-454-neofinder-text-import.md)) are the supported routes
  today. Both also rely on synthetic fixtures because genuine, redistributable
  exports were unavailable.
- Native import is described in [abeMeda/NeoFinder native catalog import](../plugins/abemeda-native.md)
  and [issue-514](issue-514-abemeda-native-import.md). Its deliberate limits —
  no item decoding, no payload extraction, extended catalogs gated, legacy
  catalogs unrecognized — stay in place until this fixture set exists.
- Item-table and media-metadata gaps beyond fixture acquisition are tracked by
  #590 and #591.

This issue adds no reader behavior, dependencies, fixture files, placeholder
plugin, `plugin.json`, or binaries.

## Closing conditions

A generation may be reopened only when all of the following hold:

1. At least one file with an accepted suffix from an identified application
   version.
2. A complete provenance record, including donor permission to redistribute.
3. A verified SHA-256 for the committed bytes.
4. The file committed under the native plugin's `Fixtures/` directory with its
   provenance note.

Fixture presence alone does not make item decoding "supported"; the reader and
its tests must still be added and validated against the fixture. A generation
without an obtainable file stays closed, and the vendor-app conversion or an
export route remains the documented fallback.

## Out of scope

- Native catalog export.
- Implementing or changing any reader or tests.
- Executing catalogs in vendor applications or in a restore or conversion
  sandbox.
- Adding dependencies, placeholder projects, fake fixtures, or binaries.
- Sourcing catalogs from third-party downloads or unknown provenance.
