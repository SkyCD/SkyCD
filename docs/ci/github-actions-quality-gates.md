# GitHub Actions Quality Gates (v3)

This document describes the default CI checks for pull requests and pushes.
Default CI validates only the v3 solution path (`SkyCD.slnx`) and does not build legacy solutions.

## Workflows
- `ci`:
- `style` job runs `dotnet format --verify-no-changes`.
- `build-test` job restores, builds, and tests `SkyCD.slnx` on `ubuntu-latest`, `windows-latest`, and
  `macos-latest`, with at most two OS jobs running at a time (`max-parallel: 2`).
- `license-compliance` job scans dependency licenses with Trivy and fails on CRITICAL/HIGH licenses.
- `sbom` job (push to `main` only) generates the CycloneDX SBOM release artifact.
- `dependabot-automerge`:
- Dependabot-only metadata, approval, and auto-merge workflow.

## Required Branch Protection Checks
For `main`, branch protection currently requires only:
- `License Compliance`

The `Build & Test (...)` checks report the remaining gates but are not individually required.

## Test Results and Artifacts
- CI publishes per-OS TRX results under `artifacts/test-results/<os>/` and the test log under
  `artifacts/test-logs/`.
- The per-OS artifacts are named `test-results-<os>` and contain both.
- License compliance reports are published as `license-artifacts`.

## Timeouts and Hang Diagnostics
Every job declares `timeout-minutes` (style 10, build-test 30, license 10, sbom 20), and the test step
is capped at 20 minutes, so a stuck test host can no longer hold a run open until GitHub's 6-hour limit.
- A failing test, crashed test host, or failed build fails the job immediately; there is no automatic retry.
- The test step redirects output to a log file instead of piping into `tee`: an orphaned test host that
  inherited the pipe's write end cannot keep the step alive after `dotnet test` exits.
- `dotnet test` runs with `--blame-hang --blame-hang-timeout 5m --blame-hang-dump-type none`, so a test
  that never completes fails its project after five minutes and writes a sequence file naming it.
- On failure the captured log is printed in the job output and uploaded with the OS artifact.

## Runtime and Flaky-Risk Notes
- Matrix execution increases runtime during SDK/dependency update waves.
- Cross-platform differences (path handling, file locking, line endings) can cause intermittent failures.
- If a flaky test is suspected:
- inspect TRX artifacts from failed jobs.
- quarantine/annotate the test and link a tracking issue.
- require two consecutive green reruns before merge.
