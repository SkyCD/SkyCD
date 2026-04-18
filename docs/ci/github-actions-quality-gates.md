# GitHub Actions Quality Gates (v3)

This document describes the CI checks required for v3 pull requests and pushes.

## Workflows
- `v3-ci`:
- `style` job: `dotnet format --verify-no-changes`
- `build-test` job: restore/build/test on `ubuntu-latest`, `windows-latest`, and `macos-latest`
- `license-compliance` job: CycloneDX SBOM generation and policy verification
- `dependabot-automerge`:
- dependabot-only metadata + approval + auto-merge workflow

## Required Branch Protection Checks
For `main`, configure branch protection to require these status checks:
- `Style (.NET format)`
- `Build & Test (ubuntu-latest)`
- `Build & Test (windows-latest)`
- `Build & Test (macos-latest)`
- `License Compliance`

## Test Results and Artifacts
- CI publishes per-OS test result artifacts from:
- `artifacts/test-results/<os>/`
- License compliance reports are published as:
- `license-artifacts`

## Runtime and Flaky-Risk Notes
- Matrix execution increases runtime; expect slower queues during dependency or SDK updates.
- Cross-platform differences (path handling, file locking, line endings) can cause intermittent failures.
- If flaky tests are observed:
- capture TRX artifacts from failed jobs
- quarantine/annotate test with linked issue
- require two consecutive green reruns before merge for suspected flakes
