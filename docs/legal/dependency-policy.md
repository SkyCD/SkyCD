# Dependency License Policy

## Purpose
Define a repeatable dependency license compliance process for SkyCD v3.

## Scope
- Applies to NuGet dependencies used by the v3 solution (`SkyCD.slnx`).
- Applies to direct and transitive dependencies discovered in the generated SBOM.

## Source of Truth
- Policy file: `docs/legal/dependency-license-policy.json`
- Compliance report artifact: `artifacts/licenses/license-compliance.json`
- SBOM artifact: `artifacts/licenses/bom.json`

## Compatibility with SkyCD

SkyCD uses the BSD 2-Clause license. CI looks up each declared dependency license in the [OSADL compatibility matrix](https://www.osadl.org/Access-to-raw-data.oss-compliance-raw-data-access.0.html), with SkyCD's license as the outbound license. A known compatible license passes without a policy edit. An incompatible license fails. A missing or inconclusive result follows the unknown-license rules below. The explicit blocklist takes precedence. An exact conditional allowance can override a blocked or matrix-`No` weak-copyleft license for one package version, as described below.

The checked-in `osadl-bsd-2-clause.json` is an extract of the BSD-2-Clause outbound row of the Open Source Automation Development Lab (OSADL) eG compatibility matrix, licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The extract keeps the verdicts and omits the explanations and other outbound rows. Its source timestamp and SHA-256 identify the input, so CI can use the data offline and reproduce decisions.

The policy explicitly allows `CC-BY-4.0` for separately licensed artwork such as Twemoji because the OSADL matrix does not cover it. Preserve attribution and the license link, and disclose any changes to the artwork. CI recognizes official Creative Commons license URLs when the SBOM provides no usable license name or identifier. Release notices and custom EULAs still require review.

## Blocked License Families
- All `GPL`, `AGPL` variants
- All `LGPL` variants, except packages approved under the Conditional Weak-Copyleft Allowance below
- `SSPL-1.0`
- `CC-BY-NC-4.0`

## Conditional Weak-Copyleft Allowance

Decision for [#925](https://github.com/SkyCD/SkyCD/issues/925) (2026-11-23): the maintainer approved
using weak/file-scoped copyleft libraries as separate, unmodified, replaceable dependencies, provided
their obligations do not conflict with SkyCD's BSD-2-Clause outbound license. This covers LGPL and
licenses with equivalent per-component copyleft scope — MPL-2.0, EPL-2.0 and CDDL. Strong copyleft
(`GPL`, `AGPL`, `SSPL`) and noncommercial licenses are never eligible, even if listed by mistake.

This does not conflict with BSD-2-Clause: BSD-2-Clause code may be part of a larger work distributed
under other terms, and these licenses attach obligations to the library or to its own files, not to
SkyCD's files, when the library is used as a separate, replaceable module. For LGPL this is the FSF's
documented dynamic-linking position
([GNU LGPL FAQ, "statically vs dynamically linked modules"](https://www.gnu.org/licenses/gpl-faq.en.html#LGPLStaticVsDynamic));
MPL-2.0, EPL-2.0 and CDDL scope their copyleft to their covered files or modules and permit
distribution of the larger work under other terms. The OSADL matrix marks these licenses `No` for a
BSD-2-Clause outbound license because it models code combined under a single license, not separate
modules, so the allowance is explicit and per package instead of a matrix result.

Eligible license identifiers are listed in `conditionalLicenseIds`. Listing a license does not approve
any package: it remains blocked or incompatible until an `allowedWithConditions` entry matches the
component's package id, exact version and license id. Any other version, another license, or a
transitive package without its own entry still fails the gate.

### Conditions

Every `allowedWithConditions` entry must record the conditions it relies on:

1. **Dynamic linking / separate files only.** The library ships as a separate managed assembly or
   native module loaded at runtime. No static linking, no ILMerge, and no publish profile, trimming or
   single-file bundling that folds it into SkyCD or another assembly.
2. **Unmodified upstream binaries.** No patches. If a modification is ever required, the modified
   source must be published under the same license and the entry re-reviewed.
3. **Notices and source availability.** Release notices ship the full license text, the exact version,
   the copyright notice, and a source download link plus a written offer covering the library source
   (and build scripts/configure flags for native builds).
4. **Replaceability.** The shipped layout keeps the library file replaceable by the user, and the
   documentation names that file.
5. **Exact per-package approval.** Each entry pins one package id, one version and one license id, and
   names the approving `type:legal` issue. Renovating to a new version requires a new entry and review.
6. **Transitive check.** GPL/AGPL and any LGPL/MPL/EPL/CDDL package pulled in transitively without its
   own entry remain blocked; approving a package never approves its dependencies.

Entries carry the fields `package`, `version`, `licenseId`, `conditions` and `approvalIssue`, and may
add `evidence` notes. The checker rejects entries whose license is not listed in
`conditionalLicenseIds`, that omit conditions or the approval issue, or that duplicate another entry.

### Release notice requirements

For every approved library, `src/SkyCD.App/THIRD-PARTY-NOTICES.txt` must include:

- the library name, exact version and copyright holder;
- the full license text (or the LGPL/MPL/EPL/CDDL text as shipped upstream) and a license link;
- a source-availability statement: source URL plus a written offer, and for a native build the
  distribution and configure flags proving the build matches the declared license (for FFmpeg: no
  `--enable-gpl` and no GPL-only codecs);
- the shipped file names, and how a user replaces the library;
- confirmation that publish profiles keep the library as a separate file.

Native binaries bundled inside a NuGet package (for example OpenCvSharp's FFmpeg DLLs or LibVLCSharp's
`libvlc`) are not described by the SBOM license field; their provenance and build flags need manual
evidence before an entry is added. Codec patent licensing (H.264/AAC) is outside this policy and must be
assessed before a decoder is shipped.

### Candidate evaluations

These packages motivated the decision but are not approved by it; each needs its own entry and
evidence when a consuming change adopts it:

- **TagLibSharp** for audio/video tags, replacing the in-repo tag parsers (`docs/plugins/audio.md`,
  [#439](https://github.com/SkyCD/SkyCD/issues/439)).
- **OpenCvSharp4** native runtime for poster frames via its bundled FFmpeg backend
  (`Plugins/SkyCD.Plugin.Video`, [#440](https://github.com/SkyCD/SkyCD/issues/440)).
- **LibVLCSharp** for video preview.

## Unknown Licenses
- Unknown or missing licenses are treated as non-compliant (`failOnUnknownLicense=true`).
- Package-specific exceptions are permitted in `allowedUnknownLicensePackages` with explicit rationale.
  - SkyCD uses CsvHelper 34.0.0 under Apache-2.0. The package declares `MS-PL OR Apache-2.0`, and the checker flags the unapproved alternative even though Apache-2.0 is available.

### Current Unknown-License Exceptions
- `SQLite` (3.53.4 is reported by the SBOM as `Unknown - See URL`; the [official copyright statement](https://www.sqlite.org/copyright.html) dedicates all deliverable SQLite code to the public domain and permits commercial redistribution). This exception covers missing license metadata only; explicitly blocked licenses still fail validation.
- `Avalonia.Angle.Windows.Natives` (upstream metadata does not publish SPDX expression)
- `AvaloniaUI.DiagnosticsSupport` (upstream metadata does not publish SPDX expression)
- `Tmds.DBus.Generator` (0.92.0 omits license metadata from its NuGet manifest; the package's repository commit [282aceb](https://github.com/tmds/Tmds.DBus/blob/282acebca8d4e4e3ba211646ac650911ee94edfd/COPYING) publishes the MIT license). This exception applies only to unknown metadata; explicitly blocked licenses still fail validation.
- `FirebirdSql.Data.FirebirdClient` (10.3.4 publishes the [Initial Developer's Public License 1.0](https://www.nuget.org/packages/FirebirdSql.Data.FirebirdClient/10.3.4/License) as `license.txt`; IDPL 1.0 has no SPDX identifier and CycloneDX reports `Unknown - See URL`). IDPL 1.0 is the Firebird Project's MPL-1.1-derived, file-scoped weak-copyleft license. It permits distribution as part of a [Larger Work](https://firebirdsql.org/file/documentation/html/en/firebirddocs/qsg5/firebird-5-quickstartguide.html#qsg5-firebird-licenses) under other terms, provided the notices and license text are retained and source-availability and modification-disclosure obligations for covered code are met. The package is consumed unmodified as a separate library. This exception covers missing license metadata only; explicitly blocked licenses still fail validation. It does not approve IPL 1.0, Firebird engine binaries, or GPL-licensed VVV source.

## Exception Process
1. Open an issue tagged `type:legal` describing package name/version and business need.
2. Provide upstream license evidence (official package metadata or project LICENSE file).
3. If approved, update `dependency-license-policy.json` with the rationale and
   evidence. Include the update in the PR for the consuming issue when the
   approval is needed there.
4. For a weak-copyleft library, add an `allowedWithConditions` entry per the
   conditional allowance rules above, and add the release notices in the same PR.
   To make a new license family eligible, add it to `conditionalLicenseIds` only
   after the same review; strong-copyleft identifiers are rejected by the checker.

## CI Enforcement
- CI generates a CycloneDX JSON SBOM.
- CI evaluates each component license against this policy.
- Build fails when blocked, incompatible or unknown licenses are detected, unless
  the component matches a conditional allowance entry exactly (package, version
  and license). Those report `CompliantByCondition`.
