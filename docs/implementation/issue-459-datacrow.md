# Issue #459: Data Crow catalog export formats and SkyCD import mapping

Investigation only. No parser was written and no Data Crow code was reused.
All format facts below come from the official Data Crow documentation and
the project's published source metadata; anything not confirmed there is
marked as such. In particular, no XML element schema is recorded here on
purpose: the exact export schema must come from a real export sample, which
could not be produced in this environment (see Provenance).

## Sources inspected

- Data Crow "Item Import & Export" guide (`datacrow.org/docs/item-import-export/`,
  docs build dated 2026-11-12): export wizard (Tools > Item Export Wizard),
  text vs XML formats, image handling, text-length option, import wizard
  formats and configuration, source-field mapping rules, list/number/date
  notations, limitations.
- Data Crow "Introduction" and module pages (`datacrow.org/docs/introduction/`,
  `datacrow.net`): standard collection modules (movies & video, TV series,
  books and eBooks, images, software/games, music), custom user modules
  defined in XML, online lookup services.
- Data Crow project metadata (SourceForge project page, Fossies
  `datacrow_4.11.0_source` package): license GNU GPLv3, embedded HSQLDB
  engine (`lib/hsqldb.jar` in the 3.x tree), Java application.

## Available export and native formats

- Item Export Wizard, per module: **text/CSV export** (flat, one item per
  line, user-defined value separator) and **XML export** (hierarchical,
  preferred; also the interchange format between Data Crow installations
  and for module export/import).
- Module Export Wizard (Modules > Export Module Wizard): exports module
  definitions; item data travels in the XML item format.
- Native storage is an embedded HSQLDB database plus image/resource folders.
  There is no supported external schema contract for it, and no .NET
  HSQLDB engine exists, so the native route is rejected outright.
- Report generators (PDF/HTML) are presentation output, not migration input.

## Schema, version markers, and encoding

- No global export version stamp is documented. Exports are scoped to one
  module at a time; module identity must be recovered from the export
  context (wizard selection, file naming) or from the XML itself once a
  real sample exists.
- Character set is auto-detected by Data Crow on import (UTF-8, UTF-16,
  Windows encodings); exports should be read the same way (BOM-aware,
  UTF-8 validated, legacy fallback).
- Text export limitations (documented): relationships between items are not
  exported, and newline characters are stripped from values. The CSV picture
  layout is two columns — Main picture plus Other pictures separated by a
  vertical bar — and round-trips back into Data Crow in order. When images
  are not copied, a link to the original location is exported instead.
- Import-side notations Data Crow itself accepts (useful when mapping):
  multi-value lists separated by vertical bar, semicolon, separate lines,
  or commas (with "Lastname, Firstname" kept together); numbers in
  `1,5` / `1.5` / `1.234,56` notations; dates as `2002-05-17`,
  `17/05/2002`, or `17 May 2002`; column names matched case- and
  space-insensitively across UI languages.

## Module, field, and relationship mapping (provisional)

- Standard modules map naturally onto SkyCD media entries: Movies, Music
  (audio), Books, Images, Software/Games, TV series. Custom modules need
  per-module mapping once their field sets are known.
- Custom fields, categories, and item relationships (references between
  items, e.g. a movie referencing its actors) are representable in the XML
  export only; the text export cannot carry relationships.
- Images arrive as file paths (copied sidecar folder preferred) plus
  web addresses; SkyCD should resolve them against the export folder and
  its subfolders, mirroring Data Crow's own lookup.
- Loan/ownership data: not confirmed in the inspected documentation.
  Whether the lend/loan administration fields appear in exports must be
  verified against a real sample before promising loan mapping.

## Recommended migration route

1. Primary route: Data Crow **XML export** (hierarchical, relationships,
   images, module interchange format). Import with content-aware detection,
   generic field mapping plus per-module overlays, image sidecar
   resolution, and unknown-field retention as extension metadata.
2. Fallback route: **CSV export** for users who only have flat files, with
   documented losses (no relationships, no newlines, module context from
   file naming or user selection).
3. Explicitly out of scope: native HSQLDB reads, write-back to Data Crow,
   loan mapping until confirmed, and any reuse of Data Crow source code.

## Import preview, cancellation, and summary strategy

Reuse the shared host flows used by the other file-format plugins: parse
to a report document first (counts, per-module mapping, warnings,
unsupported fields), surface that as the preview, then commit through the
normal catalog loader. Stream rows with progress reporting and cooperative
cancellation; a canceled or failed import must not partially commit.
Image references that do not resolve are kept as recorded paths with a
warning rather than failing the import.

## License assessment

Data Crow is licensed under the GNU General Public License v3.0, which is
blocked by the project dependency policy. No Data Crow source, schema file,
or sample derived from its distribution may be copied into this
repository. Only behavior facts from the public documentation (and, later,
from user-supplied export samples) may inform a clean-room implementation,
which needs no new dependencies beyond the existing XML/CSV parsing.

## Bounded implementation recommendation

- Phase 1: XML importer for the standard modules (Movies, Music, Books,
  Images, Software/Games) with generic field mapping, image resolution,
  and the shared preview/cancel/summary flow. Bounded once a real sample
  per module exists.
- Phase 2: custom-module support driven by the module XML, only if demand
  appears.
- Not goals: HSQLDB access, Data Crow write support, online-lookup parity.

## Sample provenance and open gaps

- No redistributable export sample was obtained: running Data Crow
  requires a Java GUI session, unavailable in this environment, and no
  vendor sample export is published with the documentation.
- Before any implementation PR: capture one XML and one CSV export per
  target module from a running Data Crow (Tools > Item Export Wizard, with
  and without copied images), record the Data Crow version, module, wizard
  settings, and file layout, strip personal data, and commit them as
  fixtures. Confirm the exact XML element/attribute names, the module
  identity marker, the loan-field question, and multi-value encoding from
  those fixtures — not from assumptions.
