# GitHub Actions Quality Gates (v3)

This document describes the default CI checks for pull requests and pushes.
Default CI validates only the v3 solution path (`SkyCD.V3.slnx`) and does not build legacy solutions.

## Workflows
- `v3-ci`:
- `style` job runs `dotnet format --verify-no-changes`.
- `build-test` job restores/builds/tests `SkyCD.V3.slnx` on `ubuntu-latest`, `windows-latest`, and `macos-latest`.
- `license-compliance` job generates CycloneDX SBOM and validates dependency policy.
- `dependabot-automerge`:
- Dependabot-only metadata, approval, and auto-merge workflow.

## Required Branch Protection Checks
For `main`, configure branch protection to require:
- `v3 Style (.NET format)`
- `v3 Build & Test (ubuntu-latest)`
- `v3 Build & Test (windows-latest)`
- `v3 Build & Test (macos-latest)`
- `v3 License Compliance`

## Test Results and Artifacts
- CI publishes per-OS test artifacts under `artifacts/test-results/<os>/`.
- License compliance reports are published as `license-artifacts`.

## Runtime and Flaky-Risk Notes
- Matrix execution increases runtime during SDK/dependency update waves.
- Cross-platform differences (path handling, file locking, line endings) can cause intermittent failures.
- If a flaky test is suspected:
- inspect TRX artifacts from failed jobs.
- quarantine/annotate the test and link a tracking issue.
- require two consecutive green reruns before merge.
