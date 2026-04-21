# GitHub Custom Agent: kilo (Free-Model-Only)

This repository configures a repository-level custom agent profile for cost-controlled usage:

- Agent profile file: `.github/agents/kilo.agent.md`
- Agent display name: `kilo`
- Configured model: `claude-sonnet-4.5` (intended non-premium/free-tier usage)

## Verify Configuration

1. Open `https://github.com/copilot/agents`.
2. Select repository `SkyCD/SkyCD` and default branch.
3. Confirm `kilo` appears in the custom agents list.
4. Open `kilo` details and verify model is `claude-sonnet-4.5`.

## Free-Only Policy Guardrails

- Repository profile guardrail: the agent profile pins `model` to `claude-sonnet-4.5`.
- Organization policy guardrail: in GitHub organization Copilot/Models settings, keep only allowed free models enabled and block premium models.

Together these guardrails keep premium/paid models unavailable for `kilo`.

## Maintainer Updates

To update allowed free models for `kilo`:

1. Edit `.github/agents/kilo.agent.md` and change `model` to another approved free model.
2. Update this document's "Configured model" line.
3. Open a PR with rationale and verification notes.
4. After merge, refresh Agents UI and re-check model/policy behavior.
