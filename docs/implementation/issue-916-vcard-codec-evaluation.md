# Issue #916: Loans vCard codec library evaluation

## Verdict

The Loans plugin keeps its custom `VCardCodec`. Neither candidate named in the
issue covers the legacy inputs the contact import must tolerate, and the only
feature-complete alternative found during the evaluation damages malformed
multi-card input and changes the exported cards. No package is added, so the
dependency policy result is unchanged.

The evaluation did expose one real gap in the custom codec: quoted-printable
values were decoded for `FN`, `NOTE`, `TEL`, `EMAIL`, `URL`, `IMPP` and `TITLE`
but not for `N`, `ORG` or `ADR`. Legacy 2.1 exports encode names and
organisations, so those properties now share the same decoding path
(`VCardCodec.DecodeRaw`).

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
extra edge cases.

| Behaviour | Custom codec | vCardLib 6.3.2 | MixERP 1.0.7 | FolkerKinzel 8.2.0 |
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

## Decision

- **vCardLib** decodes legacy encodings well but is strict exactly where the
  import must be lenient: a missing `VERSION` or any unterminated card aborts
  the whole file, and grouped properties are lost. Pre-processing the text to
  inject `VERSION`/`END` would only mask those failures.
- **MixERP.Net.VCards** does not decode quoted-printable at all, loses grouped
  properties, and has had no release since 2017. Its Apache-2.0 license is fine,
  but the functional and maintenance gaps rule it out.
- **FolkerKinzel.VCards** handles the read matrix best, but an unterminated card
  silently absorbs the properties of the following card, which corrupts data
  instead of dropping the bad card. Its serializer also changes user-visible
  export output (`REV`, duplicated `X-JABBER`, `TYPE=INTERNET`), and adopting it
  pulls a five-package chain for a narrow string mapping. That does not
  meaningfully reduce code or maintenance compared with the existing codec.

Revisit this decision if the plugin needs vCard 4.0 fields, photo retention, or
if an adopted library can guarantee per-card tolerance and byte-stable export.

## Codec behaviour contract

- Reads the subset used by contact exchange: `FN`, `N`, `NOTE`, `TEL` (voice and
  fax), `EMAIL`, `URL`, `IMPP`/`X-MESSENGER`/`X-SKYPE`, `ADR`, `ORG`, `TITLE`.
  Unknown properties, groups and `PHOTO`/`LOGO` are ignored.
- Localises quoted-printable using the declared charset when present, defaulting
  to UTF-8, and handles both LF and CRLF input.
- Flattens structured values for `BorrowerDocument`: `N` becomes
  prefix/given/family/suffix, `ADR` joins non-empty components with `, `, `ORG`
  joins units with `, `, and repeated single-valued properties join with `, `.
- Skips a card that never ends with `END:VCARD`; it never fails the whole import.
- Writes canonical vCard 3.0 with 75-character folding; `BorrowerDocument`
  persistence is unchanged.

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
