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

SkyCD uses the BSD 2-Clause license. CI looks up each declared dependency license in the [OSADL compatibility matrix](https://www.osadl.org/Access-to-raw-data.oss-compliance-raw-data-access.0.html), with SkyCD's license as the outbound license. A known compatible license passes without a policy edit. An incompatible license fails. A missing or inconclusive result follows the unknown-license rules below. The explicit blocklist takes precedence.

The checked-in `osadl-bsd-2-clause.json` is an extract of the BSD-2-Clause outbound row of the Open Source Automation Development Lab (OSADL) eG compatibility matrix, licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The extract keeps the verdicts and omits the explanations and other outbound rows. Its source timestamp and SHA-256 identify the input, so CI can use the data offline and reproduce decisions.

The policy explicitly allows `CC-BY-4.0` for separately licensed artwork such as Twemoji because the OSADL matrix does not cover it. Preserve attribution and the license link, and disclose any changes to the artwork. CI recognizes official Creative Commons license URLs when the SBOM provides no usable license name or identifier. Release notices and custom EULAs still require review.

## Blocked License Families
- All `GPL`, `LGPL`, `AGPL` variants
- `SSPL-1.0`
- `CC-BY-NC-4.0`

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

## CI Enforcement
- CI generates a CycloneDX JSON SBOM.
- CI evaluates each component license against this policy.
- Build fails when blocked or unknown licenses are detected.
