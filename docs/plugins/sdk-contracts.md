# Plugin SDK Contracts (vNext)

## Base Lifecycle
- `IPlugin`
  - `Descriptor` metadata (`Id`, `Version`, `MinHostVersion`)
  - `OnLoadAsync`
  - `OnInitializeAsync`
  - `OnActivateAsync`
  - `DisposeAsync`

## Capability Interfaces
- `IFileFormatPluginCapability`
  - Declares supported formats (`FileFormatDescriptor`)
  - Read/write operations with typed request/response payloads
- `IMenuPluginCapability`
  - Declares menu contributions and command execution
  - Uses `MenuCommandContext.HostApi` so plugins call host through explicit public APIs only
- `IModalPluginCapability`
  - Declares modal descriptors with size hints, permission requirements, and typed input/output contracts
  - Uses `ModalPayload` (`TypeId` + value) for input/output envelopes
- `ICliPluginCapability`
  - Declares CLI command contributions (`CliCommandContribution`)
  - Supports new command registration and extension-point contributions for built-in host commands
  - Executes contributed command handlers through `ExecuteCliCommandAsync` and `CliCommandContext`
- `IOwnershipProvider` (`Capabilities.Ownership`)
  - Synchronizes a store/library through `SyncAsync(CancellationToken)`.
- `IAccountConnector` (`Capabilities.Accounts`)
  - Connects a provider account through `ConnectAsync(CancellationToken)` and returns
    `AccountConnectionResult` with success, an optional account identifier, and an optional error.
  - Credentials stay with the connector and must never be returned in the result.
- `IActionProvider` (`Capabilities.Actions`)
  - Returns item-specific `SkyAction` instances through `GetActions(Item)`.
  - `Item` is an SDK context containing a stable identifier and provider metadata;
    it does not depend on persistence or UI models.
  - Actions have provider-scoped identifiers (for example `install` and `launch`),
    display titles, and asynchronous handlers receiving host cancellation.
  - Enumerating actions does not execute them; the host explicitly invokes a selected handler.
  - These contracts do not add a store UI, credential storage, or an execution scheduler.

## Runtime Discovery
- Runtime scans assemblies for concrete capability classes with public parameterless constructors.
- Identity and version constraints come from assembly metadata; no manifest is required.
- Compatibility gate: plugin loads only when `HostVersion >= MinHostVersion`.
- Capabilities are discovered by implemented interfaces deriving from `IPluginCapability`.
- Ownership, account, and action providers use the same discovery, compatibility,
  enabled-state, and registration paths as file format capabilities.
- A class can implement several capability interfaces. Discovery constructs it once,
  and `DiscoveredPlugin.RegisterPluginServices` registers that same instance for each
  capability interface. A plugin assembly can also contain separate capability classes.
- Hosts retrieve enabled capabilities with `PluginManager.GetCapabilities<TCapability>()`
  or resolve registered capability interfaces from the existing container.

## Guardrails
- Host executes menu commands through `MenuExtensionService` with timeout and exception isolation.
- Plugin exceptions are converted to result failures and should not crash the host UI thread.
- Host executes modals through `ModalExtensionService` with permission checks, typed payload validation, timeout/cancellation propagation, and reentrancy guards.
- Host executes CLI plugin handlers with timeout and exception isolation, including deterministic command collision checks.

## Sample Plugin
- `Plugins/SkyCD.Plugin.Json`
- Compiles against `SkyCD.Plugin.Abstractions` and demonstrates `IFileFormatPluginCapability`.
- `Plugins/samples/SkyCD.Plugin.Sample.Modal`
- Demonstrates modal registration + typed request/response payload contracts (`IModalPluginCapability`).
