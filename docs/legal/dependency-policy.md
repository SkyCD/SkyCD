# Dependency License Policy

## Purpose
Define a repeatable dependency license compliance process for SkyCD v3.

## Scope
- Applies to NuGet dependencies used by the v3 solution (`SkyCD.slnx`).
- Applies to direct and transitive dependencies resolved from the committed `packages.lock.json` files.

## Source of Truth
- CI gate: the `license-compliance` job in `.github/workflows/test.yml`.
- Tool: `aquasecurity/trivy-action` with `scanners: license`, `severity: CRITICAL,HIGH`, `exit-code: '1'`.
- Lock files: `packages.lock.json` in every project (enabled by `RestorePackagesWithLockFile` in
  `Directory.Build.props`).
- Release artifact: the CycloneDX SBOM produced by the `sbom` job (weekly `schedule` and
  `workflow_dispatch`) and used to maintain
  `src/SkyCD.App/THIRD-PARTY-NOTICES.txt`.

There is no allow list, exception list, or license-mapping file. The gate is the license
classification Trivy computes itself.

## Trivy Classification Gate

Trivy classifies every package license with the
[Google License Classification](https://opensource.google/documentation/reference/thirdparty/licenses)
and maps each class to a severity:

| Classification | Severity | Examples | Gate at `CRITICAL,HIGH` |
| --- | --- | --- | --- |
| Forbidden | CRITICAL | AGPL, SSPL | rejected |
| Restricted | HIGH | GPL-2.0, GPL-3.0, LGPL-2.1, LGPL-3.0, CC-BY-NC | rejected |
| Reciprocal | MEDIUM | MPL-2.0, EPL-2.0, CDDL | approved |
| Notice / Permissive / Unencumbered | LOW | MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, Zlib | approved |
| Unknown | UNKNOWN | unrecognized ids | reported, not failing at this threshold |

License findings count toward Trivy's exit code, so the job fails without any follow-up script.
A known-compatible license passes without a policy edit; adding or upgrading a dependency to
GPL/AGPL/LGPL/CC-BY-NC fails automatically.

## Lock Files

Trivy detects NuGet licenses only from `packages.lock.json` (or `packages.config`); it does not use
`*.deps.json`. It reads each package's license from the nuspec in the NuGet global packages folder,
so the `license-compliance` job restores the solution before scanning and restores with
`--locked-mode` to reject a stale committed lock file.

When a dependency changes, run `dotnet restore SkyCD.slnx` and commit the updated
`packages.lock.json` files in the same change. A lock file that is out of date fails CI.

## Known Deltas

- **LGPL is stricter than #925.** Trivy marks LGPL Restricted (HIGH), so it is rejected even though
  [#925](https://github.com/SkyCD/SkyCD/issues/925) conditionally allows weak-copyleft libraries. No
  LGPL package is currently approved, so the baseline is unaffected. If an approved LGPL package is
  adopted later, that is when a one-line classification override belongs in `trivy.yaml`.
- **Unknown licenses do not fail** at `CRITICAL,HIGH`. Add `UNKNOWN` to `severity` to restore
  fail-closed behavior, but only after confirming the baseline has no unclassifiable packages.
- **Full-tree scan, not a diff.** Every run scans all lock-file packages, so the baseline on `main`
  must stay clean; a pre-existing violation fails every run until fixed.
- **Standard scanning only.** Do not enable `--license-full` (it scans source, Markdown and license
  files and would flag third-party license texts in `docs/` and the notices).

## Release Notices

The CycloneDX SBOM is not the license gate. It is produced by the `sbom` job on a weekly schedule
and on demand (`workflow_dispatch`) and remains the input for maintaining `src/SkyCD.App/THIRD-PARTY-NOTICES.txt`. Whenever a package
that requires attribution or a license text is added, update the notices in the same change.

## Changing the Gate

Only these changes are supported:

1. **Version bump of the action or classification rules.** Update
   `.github/workflows/test.yml` after confirming the baseline on `main` is green, and record the
   effect in this document.
2. **A classification override.** If a specific approved license must pass or fail differently,
   add the smallest possible `trivy.yaml` override. Open an issue tagged `type:legal`, record the
   upstream license evidence, and include the override and this document's update in the consuming
   change.

Do not reintroduce per-package allow lists or a license-mapping file.
