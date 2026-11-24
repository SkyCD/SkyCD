# Issue #337: Expose ownership/account features through existing SkyCD CLI system

The shared `SkyCD.Plugin.Accounts` plugin provides `sync <provider-id>`,
`list licenses`, `list subscriptions`, and `export inventory`, backed by an atomic
JSON store. Human output and global `--json` use the existing CLI dispatch system.

Account providers implement `IAccountSyncProvider : IPluginCapability` from
the `SkyCD.SDK` package (namespace `SkyCD.Plugin.Abstractions`). Shared license, subscription, and import contracts
also live in the SDK package. JetBrains and Microsoft 365 are separate
plugin assemblies depending only on that package. The CLI host passes enabled,
discovered providers to account command consumers; provider IDs are resolved at
execution time. Missing and duplicate provider IDs return exit code 2 without
changing the store. No vendor types or command registrations are required in the
host or shared accounts plugin.

Sync currently normalizes optional offline JSON imports; it does not connect to
vendor APIs. `IAccountConnector` remains the separate account connection capability.
Status is derived at read time. Imports replace matching IDs and leave other
records unchanged. See `docs/plugins/accounts.md` for SDK and installation details.

Tests cover storage, normalization, CLI human/JSON output, invalid imports,
provider discovery and enablement, third-party provider dispatch, and missing or
ambiguous provider IDs. CLI tests cover host capability binding and JSON output.
