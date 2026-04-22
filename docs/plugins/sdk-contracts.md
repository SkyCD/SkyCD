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

## Runtime Discovery
- Runtime scans assemblies for classes implementing `IPlugin`.
- Compatibility gate: plugin loads only when `HostVersion >= MinHostVersion`.
- Capabilities are discovered by implemented interfaces deriving from `IPluginCapability`.

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
