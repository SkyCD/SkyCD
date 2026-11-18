# Issue #340: Microsoft 365 Connector Plugin

`SkyCD.Plugin.Accounts.Microsoft365` adds an OAuth-first Microsoft 365 connector.
The existing sync provider keeps normalizing offline imports; a new connector
capability implements `IOAuthClient`, `IAccountConnector`, `IOwnershipProvider`
and `IActionProvider`, exposing `skycd microsoft365 connect|sync|status|launch|signout`.

OAuth uses the Microsoft identity platform public native client flow through the
host's encrypted session store; only a token-free ownership snapshot is written
to `%LocalAppData%/SkyCD/microsoft365.json`. Microsoft Graph
`/directory/subscriptions` supplies subscriptions, seats, renewal dates, trial
flags and lifecycle status, with `/subscribedSkus` as a fallback for tenants that
cannot read company subscriptions. Status is derived at read time
(active/expiring/expired/suspended/no-renewal).

Installed Office detection is Windows-only and reads the `App Paths` registry
entries for the classic Office applications plus the Click-to-Run product release
and version. Suite subscriptions link all detected applications; Visio and
Project plans link their own application. Microsoft Store parsing, Xbox and Azure
management are explicitly out of scope.

Tests cover OAuth login and sanitized failures, subscription import and
persistence, Graph fallback and failure handling, renewal/trial/suspension
states, installed application linking, launch actions, detector behavior and CLI
routing. Configuration and command details are documented in
`docs/plugins/accounts.md`.
