# Ownership and account CLI commands

The `SkyCD.Plugin.Accounts` plugin exposes ownership and account features through
the existing SkyCD CLI. Commands are plugin-defined (`ICliPluginCapability`) and
discovered like any other plugin capability; no changes to the host command tree
were needed. Global `--json` output is honoured through the `ISupportsJsonOutput`
contract: the host sets it before dispatch, and the command emits JSON instead
of human-readable text.

## Commands

All commands accept `--store <accounts.json>` to select the account database
(default: `%LocalAppData%/SkyCD/accounts.json` on Windows,
`~/.local/share/SkyCD/accounts.json` on Linux/macOS). The store is a plain JSON
file written atomically, so it stays usable by scripts and editable by hand.

```bash
skycd sync jetbrains [--from <records.json>] [--store <accounts.json>]
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
merged into the store with per-record add/update counts. Records already in the
store keep any fields the provider does not manage. Statuses are derived at
read time (active/expiring/expired, or no-expiry/no-renewal), never stored.

The optional `--from` file holds a JSON array of
`{ "product": "...", "seats": n, "expiresUtc": "...", "renewsUtc": "...", "notes": "..." }`.
Missing files and invalid rows fail with exit code 2 and a message; store IO
failures fail with exit code 3.

## Covered by this plugin

- `sync jetbrains` manages JetBrains licenses; `sync microsoft365` manages
  Microsoft 365 subscriptions.
- `list licenses` / `list subscriptions` print tables or `--json` arrays with
  derived statuses.
- `export inventory` writes `{ version, exportedAtUtc, licenses, subscriptions }`.

## Not covered

No network access and no vendor APIs: sync works offline against the local
store plus optional `--from` files. Passwords, tokens and keys are never
stored — only product, seat, date and note metadata.
