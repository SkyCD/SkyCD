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
inference, and the processing destination. No command string is passed to a shell.

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

Only permitted cached context is sent: filename, extracted text, metadata, and
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
not already configured produces an actionable error; interactive authentication
and registry discovery remain separate work under #460.

Compatibility is verified with a real fake-agent subprocess and seeded ACP streams
covering normal responses, crash, timeout, cancellation, malformed/version/auth
errors, multimodal differences and permission/tool requests. No third-party agent
is claimed tested. Agents must support ACP v1, plain-text final responses and this
restricted client-capability combination; provider/model support is negotiated.

Protocol references: [initialization](https://agentclientprotocol.com/protocol/v1/initialization),
[session setup](https://agentclientprotocol.com/protocol/v1/session-setup).
