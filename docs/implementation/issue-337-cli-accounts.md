# Issue #337: Expose ownership/account features through existing SkyCD CLI system

## Summary

Implemented ownership/account CLI features as plugin-defined commands plus a
small host contract for structured output. No network access and no vendor
APIs: everything works offline against a local JSON account store.

## New plugin: `Plugins/SkyCD.Plugin.Accounts`

- Commands (all `ICliPluginCapability`, discovered by assembly scan):
  `sync jetbrains`, `sync microsoft365`, `list licenses`,
  `list subscriptions`, `export inventory`. Routing reuses the existing
  longest-prefix registry plus CommandDotNet nested dispatch.
- `AccountStore` — atomic JSON-file store (temp file + move) with a
  well-known default path and a `--store` override for isolation.
- `JetBrainsSyncProvider` / `Microsoft365SyncProvider` — normalize
  product codes / SKU part numbers to display names (unknown values pass
  through), classify licenses vs subscriptions, assign deterministic IDs,
  dedupe by ID with add/update counts.
- Sync merges `--from` import files (validated schema) and refreshes
  `LastSyncedUtc` per provider; statuses derive at read time and are never
  stored. `export inventory` writes versioned JSON to stdout or `--out`.
- Human tables by default; `--json` on `sync`/`list` for script-consumable
  output; exit codes 0/2/3 follow `CliExitCodes`.

## Host change: `src/SkyCD.Cli`, `src/SkyCD.Plugin.Abstractions`

- New `ISupportsJsonOutput` contract in the abstractions package.
- `CliHost` sets it on resolved command instances before dispatch (internal
  `ApplyJsonOutput` helper), so the global `--json` flag — which the host
  strips before dispatch — now reaches plugin commands. Commands without the
  contract are unaffected, and the host fallback for silent commands is
  unchanged.

## Tests

- `tests/SkyCD.Plugin.Accounts.Tests` — store round-trip/missing/upsert,
  provider code maps and deterministic IDs, command flows (human + JSON,
  invalid input exit codes, stdout/file export) with isolated temp stores and
  serialized console capture.
- `tests/SkyCD.Cli.Tests/CliJsonOutputTests.cs` — JSON flag propagation and
  nested command-path registration contracts.

## Docs

- `docs/plugins/accounts.md` — commands, store location, import schema,
  sync semantics, scope limits.
