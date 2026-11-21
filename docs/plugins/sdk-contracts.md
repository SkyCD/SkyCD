# Plugin SDK Contracts (vNext)

Project structure and role folders are described in [plugin-project-layout.md](plugin-project-layout.md).

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
- `IIndexWorkItemProvider` (`Capabilities.Indexing`)
  - Hooks into the host indexing sequence by contributing extra sources (`IndexWorkItem`) for one input,
    such as files stored inside an archive.
  - The host runs its existing metadata providers over every contributed item and disposes the provider's
    `IndexWorkItemSet` afterwards, so temporary files never outlive indexing, including on failure or cancellation.
  - Plugins return an empty set for inputs they do not handle; hosts never run contributed items without
    an enabled metadata provider.
- `IOwnershipProvider` (`Capabilities.Ownership`)
  - Synchronizes a store/library through `SyncAsync(CancellationToken)`.
- `IAccountConnector` (`Capabilities.Accounts`)
  - Connects a provider account through `ConnectAsync(CancellationToken)` and returns
    `AccountConnectionResult` with success, an optional account identifier, and an optional error.
  - Credentials stay with the connector and must never be returned in the result.
- `IOAuthClient` (`OAuth`)
  - Receives a plugin-scoped `IOAuthService` through `InitializeOAuth` before host service registration.
  - Provides browser login, secure local sessions, multiple account slots and automatic token refresh.
  - See [OAuth foundation](oauth.md) for provider configuration, storage support and lifecycle details.
- `IActionProvider` (`Capabilities.Actions`)
  - Returns item-specific `SkyAction` instances through `GetActions(Item)`.
  - `Item` is an SDK context containing a stable identifier and provider metadata;
    it does not depend on persistence or UI models.
  - Actions have provider-scoped identifiers (for example `install` and `launch`),
    display titles, and asynchronous handlers receiving host cancellation.
  - Enumerating actions does not execute them; the host explicitly invokes a selected handler.
  - These contracts do not add a store UI, credential storage, or an execution scheduler.

## Embedded Options configuration (SDK 2.0 migration, #828)

`IPluginConfigurationCapability.CreatePageAsync(request, cancellationToken)` replaces the former
modal `ConfigureAsync` contract. Recompile configuration plugins against SDK 2.0; remove their
configuration Windows and the descriptor's Width/Height arguments. Other capabilities are unchanged.

- `PluginConfigurationDescriptor(ConfigurationId, Title)` identifies one page per plugin. Keep the ID
  stable; the host uses plugin ID plus configuration ID, not the possibly duplicated/translated title.
- Return a fresh `PluginConfigurationPage` for each Options session. Its `createContent` callback must
  return an embeddable Avalonia `Control`, never a `Window`. The framework-neutral `object` boundary
  keeps Avalonia out of the abstractions/runtime assemblies. Content is created lazily on the UI thread.
- Load `request.CurrentSettings` into a private editor. The page and control are retained when navigating,
  searching, or disabling/re-enabling the plugin within the same dialog.
- The page's `AcceptAsync` callback validates and returns a snapshot in `PluginConfigurationResult`.
  It may run repeatedly after a failed Save. It must not save, change consumers, or perform external side
  effects. A validation failure stays inline on that page and prevents Options from closing.
- A draft that cannot be created or embedded is reported inline on its page but contributes no settings,
  so it does not block saving built-in or other plugin settings. Its category remains available for review.
- The host validates all visited, enabled pages before committing through `PluginConfigurationStore`,
  then applies settings to `IPluginSettingsConsumer` instances. Unvisited or disabled pages are not saved.
  Cancel/close disposes drafts without accepting them; supply a disposal callback for owned resources.
- Creation and acceptance must honor cancellation and return promptly. Asynchronous timeouts appear inline;
  the host disposes late-created pages. Synchronous UI callbacks must not block: in-process plugins cannot
  be forcibly terminated. A failed or timed-out load is retried when the page is selected again, without
  retrying on a timer.
- CLI/headless hosts use `PluginConfigurationStore.GetSettings` / `SaveSettings` without creating a page.
  They may also create/accept a page without accessing `Content` to normalize a settings dictionary.
  `PluginConfigurationManager.SaveSettings` additionally refreshes currently loaded consumers.

See `Plugins/SkyCD.Plugin.Sample.Configuration/SampleConfigurationPlugin.cs` and
`SampleConfigurationPage.axaml` for the editor, lazy control and acceptance callbacks. The four production
examples are AudioCd, EBook, EComic and Fonts. Discovery remains assembly/capability based.

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
- `Plugins/SkyCD.Plugin.Json` compiles against `SkyCD.Plugin.Abstractions` and demonstrates `IFileFormatPluginCapability`.
- `Plugins/SkyCD.Plugin.Sample.Modal` demonstrates modal registration and typed request/response payload contracts (`IModalPluginCapability`).
- `Plugins/SkyCD.Plugin.Sample.Configuration` demonstrates embedded Options drafts (see above).
