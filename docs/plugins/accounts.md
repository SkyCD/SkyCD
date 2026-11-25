# Ownership and account CLI commands

The `SkyCD.Plugin.Accounts.Core` plugin exposes ownership and account features through
the existing SkyCD CLI. Commands are plugin-defined (`ICliPluginCapability`) and
discovered like any other plugin capability; no changes to the host command tree
were needed. Global `--json` output is honoured through the `ISupportsJsonOutput`
contract: the host sets it before dispatch, and the command emits JSON instead
of human-readable text.

## Account provider plugins

`SkyCD.Plugin.Accounts.Core` supplies the shared CLI and store. Providers are separate
assemblies, such as `SkyCD.Plugin.Accounts.Core.JetBrains` and
`SkyCD.Plugin.Accounts.Core.Microsoft365`. Install the shared plugin and the desired
provider assemblies in the configured plugin directory. Disabled or unavailable
plugins do not participate in sync.

A provider implements `IAccountSyncProvider : IPluginCapability` from
`SkyCD.SDK.Capabilities.Accounts`. Its public implementation must
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

## Shared SDK contracts

Public account contracts are documented here. Their source files suppress only
CS1591 (missing XML documentation) locally; other compiler diagnostics remain
subject to the existing CI warning-as-error policy.

| Contract | Members and behavior |
| --- | --- |
| `IAccountSyncProvider` | `ProviderId` is a unique, case-insensitive routing ID; `DisplayName` is the human-readable provider name. `NormalizeLicenses` and `NormalizeSubscriptions` convert imported rows to canonical records. Unsupported record kinds return an empty list. |
| `IAccountSyncProviderConsumer` | The host sets `Providers` to the account sync capabilities from enabled, discovered plugins before CLI execution. |
| `ImportedAccountRow` | `Product`, `Seats` (default 1), optional `ExpiresUtc`, optional `RenewsUtc`, and optional `Notes` describe an imported entitlement. |
| `LicenseRecord` | Required `Id`, `Provider`, and `Product`; `Seats` defaults to 1. Optional `ExpiresUtc` and `Notes` describe a license. `EvaluateStatus(asOf)` returns `no-expiry`, `expired`, `expiring` (within 30 days), or `active`. |
| `SubscriptionRecord` | Required `Id`, `Provider`, and `Product`; `Seats` defaults to 1. Optional `RenewsUtc` and `Notes` describe a subscription. `EvaluateStatus(asOf)` returns `no-renewal`, `expired`, `expiring` (within 30 days), or `active`. |
| `AccountNormalization` | `Slug(value)` trims and lowercases product text and normalizes separators. `BuildId(providerId, kind, product)` creates a deterministic identifier. `ResolveName(product, codeMap, fallbackKind)` maps known product codes and preserves unknown names. |

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

## JetBrains connector

`SkyCD.Plugin.Accounts.Core.JetBrains` additionally implements `IOAuthClient`,
`IAccountConnector`, `IOwnershipProvider` and `IActionProvider`, so a single
discovered capability provides the JetBrains-specific connection, ownership and
launch behavior. OAuth tokens stay in the host's encrypted OAuth store; the
plugin only persists a token-free ownership snapshot
(`%LocalAppData%/SkyCD/jetbrains.json`, or `--store`).

```bash
skycd jetbrains connect [--account <name>] [--store <jetbrains.json>]
skycd jetbrains sync    [--account <name>] [--store <jetbrains.json>]
skycd jetbrains status  [--account <name>] [--store <jetbrains.json>]
skycd jetbrains launch <product-code> [--store <jetbrains.json>]
skycd jetbrains signout [--account <name>]
```

`sync` calls the JetBrains Account API with the current access token, records
license type, expiration, renewal, trial/suspension state and optional assignee
email, and scans for locally installed IDEs (Toolbox and standalone, Windows and
macOS layouts). `status` derives subscription state at read time, links owned
products to installed IDEs, and reports installed products without a license.
`connect`/`sync` exit with code 2 when sign-in is required, 3 on API or store
failures. `launch` starts the detected IDE for a product code.

Endpoints are public-native-client defaults and can be overridden with
`SKYCD_JETBRAINS_OAUTH_CLIENT_ID`, `SKYCD_JETBRAINS_OAUTH_AUTHORIZATION_ENDPOINT`,
`SKYCD_JETBRAINS_OAUTH_TOKEN_ENDPOINT`, `SKYCD_JETBRAINS_OAUTH_SCOPES`,
`SKYCD_JETBRAINS_API_BASE` and `SKYCD_JETBRAINS_LICENSES_PATH`.

## Microsoft 365 connector

`SkyCD.Plugin.Accounts.Core.Microsoft365` implements the same `IOAuthClient`,
`IAccountConnector`, `IOwnershipProvider` and `IActionProvider` capabilities. It
signs in through the Microsoft identity platform, reads company subscriptions
from Microsoft Graph, and detects classic Office applications installed through
Click-to-Run on Windows. Scope is limited to Microsoft 365 subscriptions and
installable Office applications: Microsoft Store package parsing, Xbox and Azure
management are not included.

```bash
skycd microsoft365 connect [--account <name>] [--store <microsoft365.json>]
skycd microsoft365 sync    [--account <name>] [--store <microsoft365.json>]
skycd microsoft365 status  [--account <name>] [--store <microsoft365.json>]
skycd microsoft365 launch <app-code> [--store <microsoft365.json>]
skycd microsoft365 signout [--account <name>]
```

`sync` reads `/directory/subscriptions`, which supplies total seats, renewal
date, trial flag and provider lifecycle status; when the tenant cannot read
company subscriptions it falls back to `/subscribedSkus`, which supplies seats
and capability status but no renewal date. Status is derived at read time:
suspended/deleted/locked-out plans are `suspended`, plans past
`nextLifecycleDateTime` are `expired`, plans renewing within 30 days are
`expiring`, and plans without a renewal date are `no-renewal`.

Installed Office applications are detected from the `App Paths` registry entries
for Word, Excel, PowerPoint, Outlook, Access, OneNote, Publisher, Visio and
Project, together with the Click-to-Run product release and version. Suite
subscriptions link every detected Office app; Visio and Project plans link only
their own app. `status` reports subscriptions with linked applications, or
installed applications when no session exists. The token-free snapshot is stored
in `%LocalAppData%/SkyCD/microsoft365.json` (or `--store`).

Sign-in requires a public native client ID. Configuration can be overridden with
`SKYCD_MICROSOFT365_OAUTH_CLIENT_ID`, `SKYCD_MICROSOFT365_TENANT`,
`SKYCD_MICROSOFT365_OAUTH_AUTHORIZATION_ENDPOINT`,
`SKYCD_MICROSOFT365_OAUTH_TOKEN_ENDPOINT`, `SKYCD_MICROSOFT365_OAUTH_SCOPES`,
`SKYCD_MICROSOFT365_GRAPH_BASE`, `SKYCD_MICROSOFT365_SUBSCRIPTIONS_PATH` and
`SKYCD_MICROSOFT365_SKUS_PATH`. `connect`/`sync` exit with code 2 when sign-in is
required or denied, and 3 on Graph or store failures; provider response bodies
and tokens are never written to output or disk.
