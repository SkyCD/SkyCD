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

### Current Unknown-License Exceptions
- `Avalonia.Angle.Windows.Natives` (upstream metadata does not publish SPDX expression)
- `AvaloniaUI.DiagnosticsSupport` (upstream metadata does not publish SPDX expression)

## Exception Process
1. Open an issue tagged `type:legal` describing package name/version and business need.
2. Provide upstream license evidence (official package metadata or project LICENSE file).
3. If approved, update `dependency-license-policy.json` in a dedicated PR with rationale.

## CI Enforcement
- CI generates a CycloneDX JSON SBOM.
- CI evaluates each component license against this policy.
- Build fails when blocked or unknown licenses are detected.
