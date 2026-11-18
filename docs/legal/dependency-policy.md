# Dependency License Policy

## Purpose
Define a repeatable dependency license compliance process for SkyCD v3.

## Scope
- Applies to NuGet dependencies used by the v3 solution (`SkyCD.V3.slnx`).
- Applies to direct and transitive dependencies discovered in the generated SBOM.

## Source of Truth
- Policy file: `docs/legal/dependency-license-policy.json`
- Compliance report artifact: `artifacts/licenses/license-compliance.json`
- SBOM artifact: `artifacts/licenses/bom.json`

## Allowed License Families
- `MIT`
- `Apache-2.0`
- `BSD-2-Clause`
- `BSD-3-Clause`
- `ISC`
- `MPL-2.0`
- `Zlib`

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
