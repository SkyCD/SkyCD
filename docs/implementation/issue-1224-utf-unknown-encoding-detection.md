# Issue #1224: Replace hand-rolled legacy encoding detectors with UTF.Unknown

## Summary

The WinCatalog, DiskCatalogMaker and abeMeda/NeoFinder catalog importers each carried their own
copy of a hand-rolled encoding detector: about 167 lines of BOM checks, a UTF-16 NUL pattern
probe, a strict UTF-8 validator and a Windows-1252 fallback, plus a matching `*EncodingProfile` and
`*EncodingKind` type. The three copies differed only by type names and XML docs, and the 8-bit
heuristics could only recognise UTF-8, UTF-16 and Windows-1252.

Detection is now delegated to [UTF.Unknown](https://github.com/CharsetDetector/UTF-unknown) 2.8.0,
a port of the Mozilla universal charset detector. The three duplicate detectors and their six
profile/kind types are deleted and replaced by one shared helper.

## Shared helper

`Plugins/Shared/EncodingProfiles/` holds the source-linked helper that the three plugins compile
into their own assemblies:

| Type | Purpose |
| --- | --- |
| `LegacyEncodingDetector` | Resolves the encoding of a payload, or reports that none was found. |
| `DetectedEncoding` | The resolved encoding, its label, the strict decoder and the detection confidence. |
| `ByteOrderMark` | Recognises the byte order mark a payload starts with. |

`LegacyEncoding` (the existing gateway to the legacy Windows code pages) gained a
`GetStrict(string name)` overload, so the helper resolves a code page with exception fallbacks.

## Detection rules

1. **A byte order mark is authoritative.** UTF-8, UTF-16 LE/BE and UTF-32 LE/BE are resolved from the
   marker before any statistical detection runs.
2. **UTF-16 without a byte order mark** is recognised from the NUL pattern of the leading bytes. The
   statistical detector reports such a payload as an 8-bit code page, which would decode a UTF-16
   report into mojibake. The rule only applies when the detector named a single byte code page (or
   named nothing), so a confidently detected multi-byte encoding is never overridden.
3. **Content detection** resolves the legacy code pages the old heuristics missed: Windows-1251,
   Shift-JIS, GB18030, the other Windows code pages and the ISO-8859 parts. Two normalisations keep
   the result readable:
   - an ASCII payload is decoded as UTF-8, because ASCII is a subset of UTF-8 and the UTF-8 decoder
     is the stricter of the two;
   - an ISO-8859-1 or ISO-8859-9 payload is decoded as Windows-1252, which carries the same
     repertoire above 0x9F and maps the undefined 0x80-0x9F positions to printable characters. The
     other ISO-8859 parts are left alone, because their repertoire above 0x9F differs.
4. **No lossy decoding.** The decoder is always created with exception fallbacks, so bytes that do
   not match the detected encoding fail the import instead of being replaced by U+FFFD. When no
   encoding is resolved at all, the importer fails with the plugin's actionable
   `*UnsupportedEncodingException`.

## Metadata.Text was deliberately left unchanged

`Plugins/SkyCD.Plugin.Metadata.Text/Parsing/TextEncodingDetector.cs` was evaluated for folding into
the shared helper and kept separate. Its contract differs from the catalog importers:

- it must never fail, because it falls back to ISO-8859-1 for any payload that is not valid UTF-8,
  while the catalog importers must fail on an unresolved encoding;
- it rejects binary content by counting control characters in the decoded text, which the shared
  helper does not do;
- it decodes a bounded scan window and trims an incomplete trailing UTF-8 sequence, a case in which
  statistical detection over a truncated payload is unreliable;
- it reports the byte order mark length, which the metadata schema publishes.

Folding it in would change how every text file in the catalog is decoded, which is outside the
scope of this issue.

## License

`UTF.Unknown` 2.8.0 is licensed under **MPL-1.1**. The Trivy gate in
`docs/legal/dependency-policy.md` classifies MPL-1.1 as *Reciprocal* and therefore **MEDIUM**, so it
is approved at the `CRITICAL,HIGH` threshold and does not fail the license job. The package has no
transitive dependencies, and it ships unmodified as a separate assembly next to the plugins that
use it, which is the same arrangement the project already applies to other weak-copyleft packages.
`src/SkyCD.App/THIRD-PARTY-NOTICES.txt` records the attribution.

## Tests

- `WinCatalogCsvLegacyEncodingTests`, `DiskCatalogMakerLegacyEncodingTests` and
  `NeoFinderTextLegacyEncodingTests` import UTF-8 (with and without BOM), UTF-16 LE/BE (with and
  without BOM), Windows-1252, Windows-1251, Shift-JIS and GB18030 payloads and assert the reported
  encoding, the decoded names and the absence of U+FFFD.
- New binary fixtures `wincatalog-cp1251.csv`, `wincatalog-shift-jis.csv` and
  `wincatalog-gb18030.csv` hold the bytes of their code page, so the importer is exercised on real
  legacy payloads.
- Each importer has a test for the unresolved encoding path, asserting the actionable error message.
