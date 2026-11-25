# GitHub Actions Quality Gates (v3)

This document describes the default CI checks for pull requests and pushes.
Default CI validates only the v3 solution path (`SkyCD.slnx`) and does not build legacy solutions.

## Workflows
- `ci`:
- `style` job runs `dotnet format --verify-no-changes`.
- `build` job restores and builds `SkyCD.slnx` with warnings as errors on `ubuntu-latest`, `windows-latest`, and `macos-latest`.
- `test` job splits the test projects into eight shards per OS and runs at most five shard jobs at a
  time. Each shard runs its projects one after another with `--blame-hang`; a failing project is
  reported by name and does not rerun.
- `license-compliance` job scans dependency licenses with Trivy and fails on CRITICAL/HIGH licenses.
- `sbom` job (push to `main` only) generates the CycloneDX SBOM release artifact.
- `dependabot-automerge`:
- Dependabot-only metadata, approval, and auto-merge workflow.

## Required Branch Protection Checks
For `main`, branch protection currently requires:
- `License Compliance`

The `Build (...)` and `Test (...)` checks report the remaining gates but are not individually required.

## Test Results and Artifacts
- CI publishes each shard's TRX results under `artifacts/test-results/<os>/shard-<n>/<project>/` and the
  per-project `dotnet test` logs under `artifacts/test-logs/`.
- The shard artifacts are named `test-results-<os>-shard-<n>`.
- License compliance reports are published as `license-artifacts`.

## Timeouts and Hang Diagnostics
Every job declares `timeout-minutes` (style 10, build 20, test 20, license 10, sbom 20), and each
`Test shard` step is capped at 15 minutes, so a stuck test host can no longer hold a run open until
GitHub's 6-hour limit.
- Sharding limits the blast radius: a hung or failing project only fails its own shard job, and the job
  name identifies the OS and shard.
- The test step redirects each project's output to a log file instead of piping into `tee`: an orphaned
  test host that inherited the pipe's write end cannot keep the step alive after `dotnet test` finishes.
- `dotnet test` runs with `--blame-hang --blame-hang-timeout 5m --blame-hang-dump-type none`, so a test
  that never completes fails the project run after five minutes and writes a sequence file naming it.
- The sequence file is uploaded with the shard artifact; the failed project's log is also printed in
  the job output.

## Runtime and Flaky-Risk Notes
- Matrix execution increases runtime during SDK/dependency update waves.
- Cross-platform differences (path handling, file locking, line endings) can cause intermittent failures.
- If a flaky test is suspected:
- inspect TRX artifacts from failed jobs.
- quarantine/annotate the test and link a tracking issue.
- require two consecutive green reruns before merge.
