# Issue #916: Loans vCard codec library evaluation

## Verdict

The Loans plugin delegates vCard parsing and serialization to
`vCardLib.dll` 6.3.2 (MIT) and keeps `VCardCodec` as a thin normalization and
mapping layer. The library handles field parsing, quoted-printable decoding and
canonical vCard 3.0 serialization; the layer handles the legacy inputs the
library rejects, such as grouped properties, missing `VERSION` and
unterminated cards. `BorrowerDocument`, the menu contracts and the persisted
contact shape are unchanged.

The evaluation also exposed a gap that the old custom codec had: quoted-printable
values were decoded for `FN`, `NOTE`, `TEL`, `EMAIL`, `URL`, `IMPP` and `TITLE`
but not for `N`, `ORG` or `ADR`. The normalization layer now decodes every
quoted-printable property before the library sees it, so legacy 2.1 exports with
encoded names and organisations import correctly.

## Candidates

Versions, licenses and maintenance were checked on 2026-11-23 against the NuGet
registration API and the upstream repositories.

| Package | Latest | License | Maintenance | Dependency weight |
|---|---|---|---|---|
| `vCardLib.dll` | 6.3.2, 2026-11-18 | MIT — compliant | Active (last push 2026-11-18) | `System.Text.Encoding.CodePages`, `System.ValueTuple`; netstandard2.0 |
| `MixERP.Net.VCards` | 1.0.7, 2017-02-20 | Apache-2.0 — compliant | Stale (last push 2019-10-05, 16 open issues) | none; netstandard1.4 maximum |
| `FolkerKinzel.VCards` | 8.2.0, 2026-07-12 | MIT — compliant | Active (last push 2026-07-26) | `FolkerKinzel.DataUrls` → `MimeTypes` → `Strings` → `Helpers`, all MIT; net10.0 target |

The issue expected MIT for both named candidates; `MixERP.Net.VCards` is
Apache-2.0, which is also allowed. License was not the blocker for any candidate.

## Feature matrix

Evidence comes from a throwaway console harness that referenced the three
packages and parsed the checked-in fixtures, the existing inline cards, and
extra edge cases. The custom codec that existed before this issue is shown as
the baseline the import contract was written against.

| Behaviour | Custom codec (before) | vCardLib 6.3.2 | MixERP 1.0.7 | FolkerKinzel 8.2.0 |
|---|---|---|---|---|
| vCard 2.1 / 3.0 properties | Yes | Yes | Yes | Yes |
| Quoted-printable UTF-8 (`Jos=C3=A9`) | Yes | Yes | No — returns `Jos=C3=A9` | Yes |
| Quoted-printable ISO-8859-1 for `N`/`FN` | Yes | Yes | No | Yes |
| Charset applied to `NOTE` (`Caf=E9`) | Yes | No — returns `Caf�` | No | Yes |
| Space/tab line folding | Yes | Yes | Yes | Yes |
| QP soft line break (line ending `=`) | Yes, LF and CRLF | Yes | No | CRLF only; LF input drops the continuation text |
| Grouped properties (`item1.TEL`) | Yes | No — kept as custom field | No — kept as custom extension | Yes |
| Base64 `PHOTO` present | Ignored without corrupting the card | Parsed | Parsed | Parsed |
| Card without `VERSION` | Accepted | Throws `A vCard must contain a 'VERSION'` | Accepted | Accepted |
| Truncated card followed by a valid card | Drops the unterminated card, keeps the valid one | Throws `A vCard must end with 'END:VCARD'` for the file | Throws `ArgumentNullException` | Merges the next card's properties into the unterminated card |
| Export changes | Canonical 3.0, stable | — | — | Adds `REV`, emits messenger as `IMPP` + `X-JABBER`, writes `EMAIL;TYPE=INTERNET` |

`MixERP.Net.VCards` also threw `ArgumentNullException` on the Latin-1 fixture,
so it fails more than quoted-printable decoding.

## Why vCardLib

- **vCardLib** is the library chosen for adoption. It decodes the legacy
  encodings and is actively maintained. Its strictness is confined to
  file-level validation (`VERSION`, `END:VCARD`) and property grouping, which
  the normalization layer absorbs before parsing; the parsing of individual
  fields stays inside the library.
- **MixERP.Net.VCards** does not decode quoted-printable at all, loses grouped
  properties, and has had no release since 2017. Its Apache-2.0 license is fine,
  but the functional and maintenance gaps rule it out.
- **FolkerKinzel.VCards** handles the read matrix best, but an unterminated card
  silently absorbs the properties of the following card, which corrupts data
  instead of dropping the bad card. Its serializer also changes user-visible
  export output (`REV`, duplicated `X-JABBER`, `TYPE=INTERNET`), and adopting it
  pulls a five-package chain for a narrow string mapping.

## Normalization layer

`VCardCodec` keeps the following responsibilities; everything else is vCardLib:

- Unfolds folded lines (leading space/tab) and quoted-printable soft breaks for
  both LF and CRLF input.
- Strips group prefixes (`item1.TEL` → `TEL`), decodes quoted-printable values
  using the declared charset (ISO-8859-1/Latin or UTF-8; UTF-8 when absent),
  escapes decoded newlines, and removes the consumed `ENCODING`/`CHARSET`
  parameters.
- Splits the stream into complete `BEGIN`/`END:VCARD` units and drops
  unterminated cards instead of failing the file.
- Injects `VERSION:3.0` when missing and a placeholder `N:;;;;` when missing.
  The placeholder is required because vCardLib resolves properties with
  `StartsWith`, so an `N` line must precede `NOTE` and `NICKNAME`, and a
  missing `N` would otherwise be matched by any later `N…` property.
- Parses each normalized card in isolation; a card the library still rejects is
  skipped. Maps the result to `BorrowerDocument` and trims/derives
  name/display-name fallbacks.
- Writes with `vCardSerializer.Serialize(cards, vCardVersion.v3)`, including
  phone/fax types, a single address line in the street component, and the
  messenger value as an `IMPP` custom field.

## Known behaviour changes

- Exported `TEL` parameters are now lowercase (`TEL;TYPE=voice`, `TEL;TYPE=fax`)
  instead of `TYPE=VOICE`/`TYPE=FAX`. Import is case-insensitive, and the
  catalog integration test asserts case-insensitively.
- Property order and formatting inside exported cards come from vCardLib.
  `ContactVCardLegacyFixtureTests` asserts export → import → export produces
  identical text and unchanged field semantics, so exports remain stable.
- A `TEL` line carrying both `VOICE` and `WORK` remains a phone number; only
  `FAX` routes to `BorrowerDocument.Fax`.

## Licensing

- The packaged commit `2a9253a1c457b4516d634f5370038ae3dd6460c0` ships a plain
  MIT `LICENSE`. The nuspec declares only a legacy `licenseUrl`, so Trivy
  reports the package license as UNKNOWN. The gate in
  [dependency-policy.md](../legal/dependency-policy.md) fails only on
  CRITICAL/HIGH classifications, so this package passes; the upstream `LICENSE`
  remains the evidence behind that decision.
- The declared dependencies (`System.Text.Encoding.CodePages`,
  `System.ValueTuple`) resolve from the .NET 10 shared framework and do not
  appear in the license scan.
- The Trivy license scan passes for `vCardLib` 6.3.2; its unknown license is
  reported without failing the gate.

## Fixtures and tests

Checked-in under `tests/SkyCD.Plugin.Feature.Loans.Tests/Fixtures`:

- `legacy-vcard-21-quoted-printable.vcf` — UTF-8 QP names/note/title/org/address,
  folded URL, QP soft break, grouped phone, multiple phones/emails, base64 photo,
  unknown `X-` property.
- `legacy-vcard-21-latin1.vcf` — ISO-8859-1 quoted-printable name, formatted name
  and note.
- `truncated-malformed.vcf` — valid card, unterminated card, junk line, and a
  following valid card.

`ContactVCardLegacyFixtureTests` parses those files and asserts
export → import → export stability (identical export text and unchanged field
semantics). `InlineCard_ExportImportExport_PreservesSemantics` applies the same
check to the existing inline cards. The scratch probe used for the candidate
matrix is not committed.
