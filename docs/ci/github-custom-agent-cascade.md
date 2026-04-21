# GitHub Custom Agent: Cascade

This repository configures a repository-level custom agent for GitHub Copilot cloud agent:

- Agent profile file: `.github/agents/cascade.agent.md`
- Agent display name: `Cascade`
- Default model configured in profile: `swe-1.6`

## Verify Configuration

1. Open `https://github.com/copilot/agents`.
2. Select the `SkyCD/SkyCD` repository and the default branch.
3. Open the agents dropdown and confirm `Cascade` is listed.
4. Open `Cascade` details and verify the model is `swe-1.6`.

## Set As Default Agent In UI

If GitHub exposes a default-agent selector for your plan/UI version, set `Cascade` as the repository default there.  
The repository-managed source of truth for this agent remains `.github/agents/cascade.agent.md`.

## Maintainer Changes

To change the default model or behavior:

1. Edit `.github/agents/cascade.agent.md` frontmatter (`model`, `tools`, `target`) and prompt body.
2. Open a PR with the reason for the change and validation notes.
3. After merge to the default branch, refresh the Agents page to confirm the updated profile is loaded.
