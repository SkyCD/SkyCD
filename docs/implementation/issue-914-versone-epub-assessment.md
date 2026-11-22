# Issue #914: VersOne.Epub replacement assessment

## Verdict

Keep the custom EPUB reader. VersOne.Epub 3.3.6 cannot replace it without losing behavior the plugin
guarantees today: the shallowest-OPF fallback when `META-INF/container.xml` is missing, case-insensitive
entry lookup, per-entry decompression and entry-count bounds, `META-INF/encryption.xml` reporting, and a
read path limited to the parts the plugin uses. Adoption would remove `EpubPackage` and most of
`EpubReader`, but the DRM scan, container fallback, cover checks, field normalization and the mapping from
library types back to `EBookDocument` stay custom — a dependency plus a wrapper, not less code.

The candidate is policy-compliant and .NET 10 compatible; the rejection is behavioral, not legal.

## Candidate facts

| Fact | Value |
|---|---|
| Package / version | `VersOne.Epub` 3.3.6 |
| Published | 2026-03-15 |
| License | **Unlicense** — the issue assumed MIT; the NuGet manifest, the repository licence and the package copyright all say Unlicense |
| Licence policy | The OSADL extract for BSD-2-Clause outbound marks `Unlicense` compatible (`docs/legal/osadl-bsd-2-clause.json`), so `tools/scripts/ci/check-license-compliance.ps1` accepts it without a policy edit |
| Maintenance | Repository pushed 2026-04-18, 553 stars, not archived |
| Targets | `net46`, `netstandard1.3`, `netstandard2.0`; no `net10.0` build. The `netstandard2.0` asset is consumed from `net10.0`; verified by building and running a `net10.0` probe against 3.3.6 |
| Dependencies | None on `netstandard2.0`; the `netstandard1.3` group alone pulls `System.IO.Compression` 4.3.0 |

## Behavior probe

A scratch `net10.0` console app referenced 3.3.6 and opened packages shaped like the fixtures in
`tests/SkyCD.Plugin.Metadata.EBook.Tests/EBookFixtures.cs`. `STRICT` is the default preset;
`IGNORE_ALL_ERRORS` is the only preset that salvages a package carrying a manifest item with no entry.

| Package | `STRICT` | `IGNORE_ALL_ERRORS` | Current reader |
|---|---|---|---|
| No `container.xml` | `EpubContainerException` | `null` (no metadata) | Falls back to the shallowest `.opf` |
| Manifest lists `toc.ncx` with no entry | `Epub2NcxException` | Book read | Book read (NCX never opened) |
| Cover entry cased `Images/Cover.JPG` vs manifest `images/cover.jpg` | `Epub2NcxException` before cover | No cover (`0` bytes, silently) | Cover found (lookup is case-insensitive) |
| Font-obfuscation `encryption.xml` | `Epub2NcxException` | Book read, no protection flag | Book read, not protected |
| AES `encryption.xml` | `Epub2NcxException` | Book read, no protection flag | Book read, `Protected` published |
| DTD entity in the package | XML parse failure | `null` | Package dropped, no values |
| 32 MiB package document from a 325 KB archive | Rejected for the probe's missing spine | Book read | Skipped by the 2 MiB part bound before parsing |

Reading the inflated package document allocated roughly 640 MiB across both preset attempts, even though
the source archive is 325 KB; the current reader drops the part before parsing.

## Gaps that options cannot close

- **Container fallback.** `ContainerFileReaderOptions` can only ignore the missing/invalid container and
  return `null` (`Source/VersOne.Epub/Options/ContainerFileReaderOptions.cs`). There is no way to supply
  the package path, so the fallback itself would have to stay custom or be dropped.
- **Case-insensitive entries.** The library resolves entries through `ZipArchive.GetEntry`
  (`Source/VersOne.Epub/Environment/Implementation/ZipFile.cs`), which is ordinal and case-sensitive.
  Manifest hrefs whose case does not match the archive entry lose the cover or the package.
- **Decompression bounds.** `XmlUtils.LoadDocumentAsync` copies the whole entry into memory before
  parsing, and the archive/environment is a private static in `EpubReader`, so no per-entry byte bound,
  entry count cap, or read instrumentation can be injected. The existing `MaxMetadataPartBytes` and
  `MaxCoverBytes` checks happen after the bytes are already materialized.
- **DRM reporting.** The library never reads `META-INF/encryption.xml`; nothing in the package source
  mentions encryption. Font obfuscation versus content encryption would stay custom.
- **Read scope.** `SchemaReader.ReadSchemaAsync` opens the NCX, the EPUB 3 navigation document and every
  SMIL document, and `ContentReader.ParseContentMap` builds refs for the whole manifest. The plugin needs
  the container, the package and the cover, so adoption reads more of the source on every book.
- **Cover bound.** `ReadCoverAsync` returns the full entry as a byte array; the 8 MiB cover bound could
  only be enforced after decompression.
- **Cancellation.** The public `OpenBookAsync(Stream, …)` overloads take no `CancellationToken`; only the
  `MediaSourceStream` reads would observe a cancel.

## What would still be custom

Adoption removes `EpubPackage.cs` (~180 lines) and most of `EpubReader.cs` (~310 lines), but keeps or adds:

- encryption.xml parsing and the font-versus-content decision (to publish `Protected`);
- container fallback (to keep the documented behavior) or its loss;
- cover signature and byte checks, so a non-image payload still yields no cover rather than a decode failure;
- field normalization to `MaxFieldCharacters`, blank-value omission and timestamp parsing;
- an adapter mapping `EpubBookRef`/`EpubSchema` types to `EBookDocument`, without exposing library types.

That is a third-party dependency plus a wrapper around behavior the plugin already owns, which the issue
explicitly says to avoid.

## Revisit triggers

Reconsider when upstream ships an injectable (and boundable) archive abstraction, a package-path override
or container fallback, encryption reporting, a cancellation-aware API, and a metadata-and-cover-only read
path that does not open the NCX, navigation and SMIL documents.

## Verification

- `dotnet test tests/SkyCD.Plugin.Metadata.EBook.Tests/SkyCD.Plugin.Metadata.EBook.Tests.csproj`:
  64 passed, 0 failed — including `MissingContainerXml_FallsBackToThePackagedOpf`,
  `DocumentTypeDeclarations_AreNotExpanded`, `FontObfuscation_IsNotReportedAsProtection`,
  `EncryptedContent_IsReportedAsProtectedWithItsMetadata`, `UnreadableSources_YieldNoValuesInsteadOfAFailure`
  and `CancelledToken_StopsBeforeReadingTheSource`.
- The probe project built and ran on `net10.0` against 3.3.6; it is a scratch artifact and is not committed.
