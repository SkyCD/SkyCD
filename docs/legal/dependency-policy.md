# Dependency License Policy

## Purpose
Define a repeatable dependency license compliance process for SkyCD v3.

## Scope
- Applies to NuGet dependencies used by the v3 solution (`SkyCD.slnx`).
- Applies to direct and transitive dependencies resolved from the committed `packages.lock.json` files.

## Source of Truth
- CI gate: the `license-compliance` job in `.github/workflows/test.yml`.
- Tool: `aquasecurity/trivy-action` with `scanners: license`, `severity: CRITICAL,HIGH`,
  `exit-code: '1'`, `trivy-config: trivy.yaml`.
- License rules: the root `trivy.yaml` states the rules as categories (no per-package entries,
  no package exceptions).
- Lock files: `packages.lock.json` in every project (enabled by `RestorePackagesWithLockFile` in
  `Directory.Build.props`).
- Release artifact: the CycloneDX SBOM produced by the `sbom` job (weekly `schedule` and
  `workflow_dispatch`) and used to maintain
  `src/SkyCD.App/THIRD-PARTY-NOTICES.txt`.

There is no allow list or exception list. The gate is Trivy's license classification as
remapped by the category lists in `trivy.yaml`.

## Trivy Classification Gate

Trivy classifies every package license with the
[Google License Classification](https://opensource.google/documentation/reference/thirdparty/licenses)
and maps each class to a severity:

| Classification | Severity | Members | Gate at `CRITICAL,HIGH` |
| --- | --- | --- | --- |
| Forbidden | CRITICAL | AGPL-1.0/-3.0 (`-only` and `-or-later`), CC-BY-NC-1.0/2.0/3.0/4.0 including all `-ND-` and `-SA-` variants, WTFPL, SSPL-1.0 | rejected |
| Restricted | HIGH | GPL (1.0/2.0/3.0, `-only`/`-or-later`, `-with-bison/classpath/font/GCC/autoconf-exception`), OSL-1.0/1.1/2.0/2.1/3.0, QPL-1.0, Sleepycat, NPL-1.0/1.1, CC-BY-ND-1.0/2.0/2.5/3.0/4.0, CC-BY-SA-1.0/2.0/2.5/3.0/4.0 | rejected |
| Reciprocal | MEDIUM | APSL-1.x/2.0, CDDL-1.0/1.1, CPL-1.0, EPL-1.0/2.0, IPL-1.0, MPL-1.0/1.1/2.0, Ruby, LGPL-2.0/2.1/3.0 (`-only` and `-or-later`) | approved |
| Notice / Permissive / Unencumbered | LOW | MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, Zlib (Trivy defaults) | approved |
| Unknown | UNKNOWN | unrecognized ids (including EUPL, which Trivy does not classify) | reported, not failing at this threshold |

License findings count toward Trivy's exit code, so the job fails without any follow-up script.
Two deliberate deltas from Trivy's defaults, both stated in `trivy.yaml`:
- **LGPL-2.0/2.1/3.0 pass as Reciprocal.** SkyCD ships NuGet assemblies as separate,
  replaceable DLLs, so unmodified weak-copyleft dependencies are compatible (#925). An
  `-or-later` grant includes the corresponding `-only` terms, so both forms are listed.
- **SSPL-1.0 fails as Forbidden.** Trivy has no default category for it, so without the
  explicit entry it would land in Unknown and pass the gate silently.

## Lock Files

Trivy detects NuGet licenses only from `packages.lock.json` (or `packages.config`); it does not use
`*.deps.json`. It reads each package's license from the nuspec in the NuGet global packages folder,
so the `license-compliance` job restores the solution before scanning and restores with
`--locked-mode` to reject a stale committed lock file.

When a dependency changes, run `dotnet restore SkyCD.slnx` and commit the updated
`packages.lock.json` files in the same change. A lock file that is out of date fails CI.

## Known Deltas

- **trivy.yaml replaces three default lists.** `forbidden`, `restricted` and `reciprocal` each
  replace Trivy's whole default list for that category, so the file restates the defaults plus
  the LGPL and SSPL deltas. `notice` and `unencumbered` are omitted and keep Trivy's defaults.
- **Unknown licenses do not fail** at `CRITICAL,HIGH`. Add `UNKNOWN` to `severity` to restore
  fail-closed behavior, but only after confirming the baseline has no unclassifiable packages.
- **Full-tree scan, not a diff.** Every run scans all lock-file packages, so the baseline on `main`
  must stay clean; a pre-existing violation fails every run until fixed.
- **Standard scanning only.** Do not enable `--license-full` (it scans source, Markdown and license
  files and would flag third-party license texts in `docs/` and the notices).
- **CC-BY-NC is Forbidden, not Restricted.** Trivy 0.76.2 classifies every CC-BY-NC variant
  (including `-ND-`/`-SA-` suffixed ones) as Forbidden/CRITICAL. The `trivy.yaml` file restates
  this; do not "downgrade" these to `restricted`.

## Release Notices

The CycloneDX SBOM is not the license gate. It is produced by the `sbom` job on a weekly schedule
and on demand (`workflow_dispatch`) and remains the input for maintaining `src/SkyCD.App/THIRD-PARTY-NOTICES.txt`. Whenever a package
that requires attribution or a license text is added, update the notices in the same change.

## Changing the Gate

Only these changes are supported:

1. **Version bump of the action or classification rules.** Update
   `.github/workflows/test.yml` after confirming the baseline on `main` is green, and record the
   effect in this document.
2. **A category change.** If a license must pass or fail differently, move its exact SPDX id(s)
   between the `forbidden`, `restricted` and `reciprocal` lists in the root `trivy.yaml`. Open an
   issue tagged `type:legal`, record the upstream license evidence, and include the `trivy.yaml`
   edit and this document's update in the consuming change.

Do not reintroduce per-package allow lists or a license-mapping file. Do not add new packages to
prove the gate: validate `trivy.yaml` edits with a synthetic CycloneDX SBOM (see below), never by
depending on a real GPL/SSPL package.

## Re-syncing After a Trivy Upgrade

New Trivy versions can add, remove or reclassify ids, which silently changes the gate even when
`trivy.yaml` is untouched. When the pinned Trivy version changes:

1. Download that exact Trivy release and run it with `--offline-scan` (results are cached, so use
   a fresh `--cache-dir` for every comparison run — a stale cache ignores `--config` changes).
2. Build a synthetic CycloneDX SBOM with one component per SPDX id under review (a `purl`
   `bom-ref` per component is required for Trivy to pick it up) and scan it twice: once without
   `--config` (defaults) and once with `--config trivy.yaml`.
3. Confirm every id in `trivy.yaml` still classifies as the file states, and diff the default
   classification of the repo's real dependency set (the full `trivy fs --offline-scan
   --scanners license --severity CRITICAL,HIGH` tree scan must stay green).
4. Record the new Trivy version and any list changes in this document and in the `trivy.yaml`
   header.
