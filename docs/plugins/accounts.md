# Ownership and account CLI commands

The `SkyCD.Plugin.Accounts` plugin exposes ownership and account features through
the existing SkyCD CLI. Commands are plugin-defined (`ICliPluginCapability`) and
discovered like any other plugin capability; no changes to the host command tree
were needed. Global `--json` output is honoured through the `ISupportsJsonOutput`
contract: the host sets it before dispatch, and the command emits JSON instead
of human-readable text.

## Account provider plugins

`SkyCD.Plugin.Accounts` supplies the shared CLI and store. Providers are separate
assemblies, such as `SkyCD.Plugin.Accounts.JetBrains` and
`SkyCD.Plugin.Accounts.Microsoft365`. Install the shared plugin and the desired
provider assemblies in the configured plugin directory. Disabled or unavailable
plugins do not participate in sync.

A provider implements `IAccountSyncProvider : IPluginCapability` from
`SkyCD.Plugin.Abstractions.Capabilities.Accounts`. Its public implementation must
have a parameterless constructor for assembly discovery. The provider declares
`ProviderId` and `DisplayName` and normalizes imported rows into shared
`LicenseRecord` and `SubscriptionRecord` contracts. Provider IDs must be unique
among enabled providers; ambiguous IDs fail before modifying the store.

The CLI host supplies discovered providers through `IAccountSyncProviderConsumer`.
`sync <provider-id>` resolves those capabilities dynamically; adding a provider
requires no changes to the CLI or shared accounts plugin. Providers depend only
on the abstractions package, with no dependency on the shared accounts plugin.
Assembly metadata and implemented capability types provide discovery information;
no plugin manifest is needed. The existing `IAccountConnector` capability remains
available for account connection; offline normalization does not imply a login.

## Commands

All commands accept `--store <accounts.json>` to select the account database
(default: `%LocalAppData%/SkyCD/accounts.json` on Windows,
`~/.local/share/SkyCD/accounts.json` on Linux/macOS). The store is a plain JSON
file written atomically, so it stays usable by scripts and editable by hand.

```bash
skycd sync <provider-id> [--from <records.json>] [--store <accounts.json>]
skycd sync microsoft365 [--from <records.json>] [--store <accounts.json>]
skycd list licenses [--store <accounts.json>]
skycd list subscriptions [--store <accounts.json>]
skycd export inventory [--store <accounts.json>] [--out <inventory.json>]
```

Append the global `--json` flag to `sync` and `list` for machine-readable output;
`export inventory` always writes JSON (to standard output, or to `--out`).

## Sync semantics

`sync <provider>` reconciles that provider's records: imported rows are
normalized (JetBrains product codes and Microsoft 365 SKU part numbers map to
display names; unknown values pass through), deduped by deterministic IDs, and
merged into the store with per-record add/update counts. Imported records replace existing records with the same ID; records with other
IDs remain in the store. Statuses are derived at
read time (active/expiring/expired, or no-expiry/no-renewal), never stored.

The optional `--from` file holds a JSON array of
`{ "product": "...", "seats": n, "expiresUtc": "...", "renewsUtc": "...", "notes": "..." }`.
Missing files and invalid rows fail with exit code 2 and a message; store IO
failures fail with exit code 3.

## Covered by this plugin

- The JetBrains provider manages licenses; the Microsoft 365 provider manages
  subscriptions. Each is installed and enabled separately.
- `list licenses` / `list subscriptions` print tables or `--json` arrays with
  derived statuses.
- `export inventory` writes `{ version, exportedAtUtc, licenses, subscriptions }`.

## Not covered

No network access and no vendor APIs: sync works offline against the local
store plus optional `--from` files. Passwords, tokens and keys are never
stored — only product, seat, date and note metadata.
