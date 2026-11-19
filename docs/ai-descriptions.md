# ACP-generated file descriptions

The separate `skycd.ai-description` plugin contributes **Tools → Generate AI
description for selected file**. It implements an explicit generated-metadata
capability using the existing media schema and durable cache infrastructure.
Automatic metadata extraction never starts an AI agent. No vendor API, model
download, registry executable installation, or `plugin.json` discovery is used.

Select exactly one file, then use **AI description configuration**. The command
creates a disabled template at the location shown in the status bar, normally
`%APPDATA%/SkyCD/ai-descriptions.json`. Edit it to select an installed ACP
executable and its argument/environment arrays, then explicitly enable it.
The same file configures model (where advertised), output language, prompt and
version, context/output limits, timeout, permitted input sources, metadata-only
inference, an optional `AuthMethodId`, and the processing destination. No command
string is passed to a shell.

Default settings permit only filename context and leave generation disabled.
Enable `Sources.Text`, `Metadata`, `Images` or `Audio` individually to permit those
cached inputs. Aggregate hard caps are 64,000 text characters and 4 MiB of native
artifacts. Output is limited to 8,000 characters and agent timeout to five minutes.
Metadata-only inference requires `AllowMetadataOnlyInference: true`.

`ProcessingDestination` accepts `unknown`, `cloud`, or `local-inference`.
This is a user-configured declaration, not inferred from a local process: a local
ACP executable may call a cloud model. Configure a local-inference agent for offline
generation. Saved descriptions remain readable without an agent, network,
source medium, or plugin installation.

## Context and results

Adding files through a catalog scan starts a description pass for each new file
when an AI description provider is enabled. For local files, SkyCD attaches the
original directly from disk. Agents that advertise embedded context receive the
original bytes for files up to 4 MiB. Otherwise, SkyCD passes a link to the local
file for the agent to open. The original is neither copied into SkyCD's cache nor
replaced with extracted text or thumbnails. Remote or unreadable originals
report a per-file failure. Rescanned entries keep their existing descriptions.
Cancelling or an agent error leaves the added files in the catalog.

Manual generation uses permitted cached context: filename, extracted text, metadata, and
cached image thumbnails/posters/cover art or audio artifacts. No original source
files or filesystem paths are supplied. Native images/audio are sent only when
advertised. Unsupported context is omitted; generation fails explicitly if no
permitted content remains, unless metadata-only inference was separately enabled.
Stale cached data is eligible only while its source fingerprint still matches.

Content-based descriptions and metadata-only inference have distinct persisted
origins. Generated descriptions are unverified AI output. They live under
`AiDescription` and prefixed provenance fields in the media cache, separate from
user comments, properties and tags. Records include agent/version, negotiated
model when available, plugin/prompt version, language, source/context fingerprints,
generation time, and configured processing destination. Existing metadata browsing
and advanced metadata search expose these fields offline.

Results are saved only after complete valid plain-text output and another source
and context check. Cancellation, changes, permission/tool requests, invalid or
oversized output, crashes, authentication errors and timeouts retain earlier
accepted descriptions. A catalog switch cancels its active plugin command.
Clicking a plugin command while one is running cancels the active command.

## Properties and batch generation

The Properties dialog shows the stored description for a file in an **AI
description** tab together with the provider, configured context sources and
processing destination, plus provenance (agent, model, prompt version, language
and generation time). Generation runs only from the explicit Generate or
Regenerate button; opening Properties, reading metadata or searching never
starts an agent. A stored description whose source or context changed is shown as
stale but remains readable, and a failed regeneration reports the error while
keeping the earlier accepted text.

**Adopt as comment** copies the generated text into the comment editor. When the
entry already has a comment, a second confirmation is required, and the comment
is only written through the normal dialog confirm/undo path. The stored generated
description is never overwritten by adoption or by comment edits, so the AI and
user-authored metadata stay distinguishable in metadata browsing and search.

**Tools → Generate AI descriptions...** runs one explicit pass over the selected
files, showing configured provider/context, per-item progress, a
Cancel button and per-item outcomes. The pass skips entries whose stored
description still matches the current source and context unless regeneration is
requested, processes at most two entries at a time, and records failures per item
without aborting the rest. Cancelling marks the unfinished items as cancelled and
keeps all earlier descriptions.

## ACP registry browsing

**Tools → Browse cached ACP agent registry** reads an optional local copy of the
official [ACP registry](https://agentclientprotocol.com/get-started/registry)
index at `%APPDATA%/SkyCD/acp-registry.json`. Download
`https://cdn.agentclientprotocol.com/registry/v1/latest/registry.json` there
manually; SkyCD never fetches, verifies, installs, updates or launches registry
content. The registry is treated strictly as bounded display data: it cannot set
the executable, arguments or environment, and unknown or malformed entries are
ignored. The command lists cached names, identifiers, versions and distribution
types, and a suggested launch form for currently installed `npx`/`uvx` or a
platform binary. Any suggested launch remains a manual configuration hint; the
executable, arguments and environment are always taken from the user-edited
configuration file, and an archive or package must be installed by the user.

Cached browsing works fully offline. Registry membership is not a compatibility
claim: an entry still has to satisfy the negotiated requirements below.

## Authentication

After `initialize`, the client reads the agent's advertised `authMethods`. Set
`AuthMethodId` to an advertised method to run the agent-managed `authenticate`
flow before any session is created. The client does not advertise terminal
authentication support because it cannot present an interactive terminal; a
`terminal` method is rejected with instructions to complete that sign-in
manually. An `auth_required` response lists only the advertised method
identifiers, names and types, never the agent's error text. Credentials belong in
the agent's own authentication store or in the explicitly configured environment
and are never written to catalog metadata, generated descriptions, configuration
templates or logs.

## ACP compatibility and security boundary

The client implements ACP v1 initialization, session setup, streaming prompt
updates, advertised native modalities, optional advertised model selection, and
cancellation. It supplies no MCP servers or filesystem/terminal capabilities,
denies permission and client-tool requests, and aborts any tool-call update.
Context is serialized as untrusted file data after instructions to ignore embedded
commands. Agent stderr and raw remote errors are not logged or stored. Configure
credentials in the agent's supported setup or explicit environment settings;
never put tokens in prompts or catalog metadata.

The agent runs in an empty temporary directory and its process tree is terminated
on completion/cancellation. This ACP capability boundary is not an operating-system
sandbox for arbitrary executables: use a trusted agent that honors permission
negotiation. The plugin grants no catalog, source-file or shell API. Authentication
not already configured produces an actionable error listing the advertised
methods; agent-managed authentication runs only when `AuthMethodId` is configured,
and terminal authentication must be completed outside SkyCD.

Compatibility is verified with a real fake-agent subprocess and seeded ACP streams
covering normal responses, crash, timeout, cancellation, malformed/version/auth
errors, multimodal differences, agent-managed authentication and permission/tool
requests. No third-party agent is claimed tested. Agents must support ACP v1,
plain-text final responses and this restricted client-capability combination;
provider/model support is negotiated. Registry entries are checked only as data;
`npx`, `uvx` and `binary` distributions are not executed or verified. The cached
browser reads the stable `registry.json` index and does not distinguish musl Linux
targets or preview channels.

Protocol references: [initialization](https://agentclientprotocol.com/protocol/v1/initialization),
[session setup](https://agentclientprotocol.com/protocol/v1/session-setup).
