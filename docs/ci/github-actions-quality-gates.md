# GitHub Actions Quality Gates (v3)

This document describes the default CI checks for pull requests and pushes.
Default CI validates only the v3 solution path (`SkyCD.slnx`) and does not build legacy solutions.

## Workflows
- `v3-ci`:
- `style` job runs `dotnet format --verify-no-changes`.
- `build-test` job restores/builds/tests `SkyCD.slnx` on `ubuntu-latest`, `windows-latest`, and `macos-latest`.
- `license-compliance` job scans dependency licenses with Trivy and fails on CRITICAL/HIGH licenses.
- `sbom` job (push to `main` only) generates the CycloneDX SBOM release artifact.
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
- CI publishes per-OS test artifacts under `artifacts/test-results/<os>/` and the per-attempt
  `dotnet test` logs under `artifacts/test-logs/` (uploaded with the same per-OS artifact).
- License compliance reports are published as `license-artifacts`.

## Timeouts and Hang Diagnostics
Every job declares `timeout-minutes`, and the `Test solution` step is capped at 20 minutes, so a stuck
test host can no longer hold a run open until GitHub's 6-hour limit.
- The test step redirects output to a log file instead of piping into `tee`: an orphaned test host
  that inherited the pipe's write end cannot keep the step alive after `dotnet test` finishes.
- `dotnet test` runs with `--blame-hang --blame-hang-timeout 5m --blame-hang-dump-type none`, so a test
  that never completes fails the run after five minutes and writes a sequence file naming it.
- The sequence file is uploaded with the per-OS test artifact; the failed attempt's log is also printed
  in the job output.

## Runtime and Flaky-Risk Notes
- Matrix execution increases runtime during SDK/dependency update waves.
- Cross-platform differences (path handling, file locking, line endings) can cause intermittent failures.
- If a flaky test is suspected:
- inspect TRX artifacts from failed jobs.
- quarantine/annotate the test and link a tracking issue.
- require two consecutive green reruns before merge.
