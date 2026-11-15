# Media extraction and previews

Implement `IMediaMetadataCapability` and/or `IMediaPreviewCapability` in the plugin assembly.
The existing assembly scanner discovers these marker-derived interfaces; normal host-version
compatibility, availability and enabled-state filtering apply before host registration.
No manifest or separate registry is involved. Existing plugin interfaces are unchanged.

Descriptors declare extensions (including the leading dot), MIME content types and a versioned
schema. Either extension or MIME match selects the provider, case-insensitively. Schema IDs
and field keys must remain stable; incompatible schema changes require a new schema version.
Values declare exactly one matching primitive type. Units use stable symbols, for example
`s`, `px`, or `byte`; changing a field's meaning or unit is a schema change.

`MediaExtensionManager.ProcessAsync` returns every matching provider's result. Higher descriptor
priority wins precedence; ties use ordinal plugin ID then capability type name. Consumers that
need a single value select the first successful provider with that field. Results remain
separate: the identity of a value includes plugin ID, plugin version, capability type, schema
ID, schema version and field key. Never flatten results into a dictionary keyed only by field
name, or silently replace previously stored metadata. A failed provider does not suppress others.

The caller supplies a factory for a fresh readable stream per provider. The host owns each
returned stream and disposes it after the worker exits. Plugins receive bounded read access,
not a path, UI control or persistence entity. Reads charge a cumulative byte budget, including
repeated reads. Non-seekable streams support sequential reads. Preview data is copied before
return, with an aggregate artifact byte limit; metadata text is also bounded. Progress and
cancellation use the standard .NET interfaces.

Defaults are 16 MiB source reads per provider, 4 MiB preview/metadata output, two simultaneous
workers per manager, and five seconds per provider (including admission wait). Limits are
configurable through `MediaExecutionLimits`. Exceptions, invalid output and timeouts return
safe failure results and publish sanitized `PluginFailure` telemetry through the existing
failure boundary. Caller cancellation propagates.

Timeouts return promptly even for synchronous or cancellation-ignoring plugin code. Such a
worker retains its concurrency slot and resources until it actually exits; the host observes
late exceptions and disposes its stream then. This is in-process cooperative isolation, not
a security sandbox: terminating arbitrary plugin CPU work or bounding plugin allocations
requires a separate process. Plugins must honor cancellation and avoid retaining source access.

The host stores results durably and keeps them available offline; plugins hold no state of their
own. See `docs/architecture/media-cache-lifecycle.md` for how a descriptor's schema id and version
key the stored result, and why bumping the schema version is what invalidates cached data.
