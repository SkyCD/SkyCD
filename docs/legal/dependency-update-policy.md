# Dependency Update Policy

This policy defines how automated dependency update pull requests are handled in v3.

## Automation Scope
- Dependabot is enabled for:
- NuGet (`package-ecosystem: nuget`).
- GitHub Actions (`package-ecosystem: github-actions`).
- All update PRs target `main` and are validated by CI workflows on pull request events.

## Cadence and Noise Control
- Routine updates run weekly.
- Open PR limits are enforced to avoid queue flooding.
- Routine updates are grouped by stack: .NET runtime/platform packages, test tooling packages, and GitHub Actions dependencies.

## Security Updates
- Security updates are separated from routine updates using Dependabot security groups.
- Security PRs are intentionally broad (`*`) to avoid delaying vulnerable package remediation.

## Metadata Rules
- Dependabot PRs must include:
- `dependencies` label.
- `type:platform` label.
- Conventional commit-style prefix (`chore(deps)` or `chore(ci-deps)`).

## License Guardrails (BSD-2)
- Every dependency update PR must pass the license compliance gate defined in:
- [dependency-license-policy.json](dependency-license-policy.json)
- [.github/workflows/v3-ci.yml](../../.github/workflows/v3-ci.yml)
- Packages violating policy are blocked until approved policy changes are merged.

## Changelog Policy
- Routine dependency PRs do not require changelog entries.
- Security updates and major-version updates must include a short impact summary in the PR body.
