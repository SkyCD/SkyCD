# Plugin SDK Contracts (vNext)

Project structure and role folders are described in [plugin-project-layout.md](plugin-project-layout.md).

## The `SkyCD.SDK` package

`SkyCD.SDK` is the single, versioned, dependency-free library a third-party author references to build a
SkyCD plugin. It is produced by [SkyCD/SkyCD.SDK](https://github.com/SkyCD/SkyCD.SDK) (assembly and NuGet package id `SkyCD.SDK`, and the same
root for every contract namespace: `SkyCD.SDK.*`). See
[sdk-versioning-policy.md](sdk-versioning-policy.md) for the versioning rules. Follow the [SDK installation guide](https://github.com/SkyCD/SkyCD.SDK#install)
to configure the GitHub Packages feed and install the current alpha.

The package contains only the plugin-authoring surface:

- lifecycle and identity: `IPlugin`, `IPluginCapability`, `PluginIdAttribute`, `MinHostVersionAttribute`, `MaxHostVersionAttribute`;
- the capability contracts under `Capabilities/**` and the host services plugins call back into
  (`ICatalogApi`, `IHostCommandApi`, `IHostDialogApi`, `IPluginStorageApi` / `IPluginStorageConsumer`, `IOAuthService`);
  plugin translations use `ILocalizationPluginCapability`, while the host localizer lives in `SkyCD.UI.Localization`;
- the request/result/descriptor models exchanged across those contracts.

The package deliberately contains **no third-party helper types**. SkiaSharp, XmpCore, Avalonia and
host-internal packages must not appear in its dependency list, and no helper from `Plugins/Shared`
(XMP/Skia readers, the temp database, encodings, export formatting, package-path rules) is part of the SDK
public API. Preview rendering, media-metadata/XMP parsing and temp-workspace handling are reached through
host service contracts implemented by the host, not by copied helper source.

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
- `IPluginConfigurationCapability` (`Capabilities.Configuration`)
  - The single mechanism for contributing pages to Tools > Options (`PluginConfigurationDescriptor`)
  - Contributes one or more pages; each page is rendered in the Options sidebar and owns its content
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
  - The desktop host calls it through `IAccountSession`, which extends this contract; there is no
    standalone ownership UI.
- `IAccountConnector` (`Capabilities.Accounts`)
  - Connects a provider account through `ConnectAsync(CancellationToken)` and returns
    `AccountConnectionResult` with success, an optional account identifier, and an optional error.
  - Credentials stay with the connector and must never be returned in the result.
- `IOAuthClient` (`OAuth`)
  - Receives a plugin-scoped `IOAuthService` through `InitializeOAuth` before host service registration.
  - Provides browser login, secure local sessions, multiple account slots and automatic token refresh.
  - See [OAuth foundation](oauth.md) for provider configuration, storage support and lifecycle details.
- `IOnlineMetadataSourceCapability` (`Capabilities.Media`)
  - Declares a stable `SourceId`, display name and owned hosts; `FetchAsync` returns named
    facts or a failure result for one lookup and never throws for ordinary site failures.
  - Sources are aggregated host-side and handed to generators through
    `IOnlineMetadataConsumer.InitializeOnlineMetadata` and `IOnlineMetadataService`, so a
    generator owns templates and rendering, never site parsing or fetching.
  - See [online metadata sources](online-metadata.md) for the shipped source plugins.
- `IMediaTargetMetadataCapability` (`Capabilities.Media`)
  - Collects metadata addressed by a catalog media root instead of by file bytes. `MediaTarget` carries
    the medium `Kind` (volume, optical disc, folder, file, network folder, network resource or
    plugin-filled source folder), a nullable `Location` (path, UNC path or URL), the root name and the
    facts the host already knows. `Supports(MediaTarget)` lets one capability serve several kinds, and
    captures are merged into the root catalog entry by the scanning, folder, remote and disc flows.
- `IDescriptionGeneratorCapability` (`Capabilities.Media`)
  - Carries a stable `ImplementationId`, a user-facing `ImplementationName`, the generated-metadata
    `Schema` and `GenerateAsync(DescriptionContext, CancellationToken)`.
  - One contract covers template-backed and AI-backed generators; whether a generator calls a model is an
    implementation detail. The host persists `ImplementationId` to pick among installed generators, so keep
    it stable, and shows `ImplementationName` in the Options implementation picker.
  - `IDescriptionGenerationPlanner.GetPlan()` stays optional and provider-neutral: it discloses the
    configured provider and context limits without contacting it.

## Embedded Options configuration (SDK 2.0 migration, #828)

`IPluginConfigurationCapability.CreatePageAsync(request, cancellationToken)` replaces the former
modal `ConfigureAsync` contract. Recompile configuration plugins against SDK 2.0; remove their
configuration Windows and the descriptor's Width/Height arguments. Other capabilities are unchanged.

SDK 3.0 (#1340) makes this the only Options mechanism: plugins contribute one or more pages through
`Configurations`, operational surfaces such as the catalog contacts manager or the AI provider settings
are configuration pages too, and `IOptionsPageCapability` no longer exists.

- `PluginConfigurationDescriptor(ConfigurationId, Title, Group, Order)` identifies one page. Keep the ID
  stable; the host uses plugin ID plus configuration ID, not the possibly duplicated/translated title.
  `Group` is optional: pages sharing a group are nested under one parent in the Options sidebar, while
  pages without a group stay top-level. `Order` sorts plugin pages; lower values come first.
- Return a fresh `PluginConfigurationPage` for each Options session. Its `createContent` callback must
  return an embeddable Avalonia `Control`, never a `Window`. The framework-neutral `object` boundary
  keeps Avalonia out of the abstractions/runtime assemblies. Content is created lazily on the UI thread.
- Load `request.CurrentSettings` into a private editor. The page and control are retained when navigating,
  searching, or disabling/re-enabling the plugin within the same dialog.
- The page's `AcceptAsync` callback validates and returns a snapshot in `PluginConfigurationResult`.
  It may run repeatedly after a failed Save. Plugin-side work belongs here, not in `CreatePageAsync`:
  a page that manages host state accepts by performing that work and returning `request.CurrentSettings`
  unchanged, while a settings page must not persist settings or change consumers itself.
  A validation failure stays inline on that page and prevents Options from closing.
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

See the [Sample Configuration source](https://github.com/SkyCD/SkyCD.Plugin.Sample.Config/tree/main/Configuration) for the editor, lazy control and acceptance callbacks. The four production
examples are AudioCd, EBook, EComic and Fonts. Discovery remains assembly/capability based.

## Navigation into contributed pages (SDK 3.0, #1105, #1340)

`IOptionsNavigationConsumer` lets a plugin ask the host to open one of its configuration pages, for
example from a menu command. The host passes a callback that takes a
`PluginConfigurationDescriptor.ConfigurationId`, opens Options with that page selected, and completes when
Options closes.

- The page must be listed in the plugin's `IPluginConfigurationCapability.Configurations`; an unknown
  configuration ID is reported inline on the plugin page instead of opening a window.
- Other feature code reads host state through SDK contracts (for example `ILocationApi.IsCatalogOpen`) and
  renders its own empty/unavailable states; the host stays feature-agnostic.

See `Plugins/SkyCD.Plugin.Feature.Locations/LocationsPlugin.cs` and
`Plugins/SkyCD.Plugin.Feature.Loans/LoansPlugin.cs` for reference implementations.

## Catalog data-loss preflight (SDK 3.0)

Before writing a catalog, the host asks the routed writer what the target format cannot represent and
shows a confirmation dialog when anything would be lost. Declining never creates, truncates or replaces
the file.

- `IDataLossAwareFileFormatCapability` (optional, extends `IFileFormatPluginCapability`)
  - `PreservesCatalogScope` declares whether the format stores catalog-scope data (virtual collections,
    custom statuses, loans, borrowers, scan and AI exclusions). When `false`, the host appends its
    catalog-scope loss items to the plugin's list.
  - `AssessDataLoss(FileFormatDataLossRequest)` must be pure: no file access, no side effects. It receives
    the same entry snapshot and catalog-scope items that the following write receives.
- Plugins without the interface are treated as lossless for entry data; any catalog-scope data present is
  still reported as lost.
- `FileFormatDataLossRequest` carries `FormatId`, `FileName`, `Entries` (`FileFormatDataLossEntry`),
  `CatalogScopeItems` and `CultureName`. Return `FileFormatDataLossAssessment.Lossless` when nothing is
  lost. Each `FileFormatDataLossItem` has `Category`, `Name`, `AffectedCount` and an optional `Sample`.
- Use `FileFormatDataLossAssessmentBuilder.AssessEntries(request, represented)` with a
  `FileFormatEntryFieldSupport` combination (`Id`, `ParentId`, `Kind`, `Size`, `Status`, `Properties`) to
  report only fields that are actually present and actually dropped.
- Categories come from `FileFormatDataLossCategories` (`entries`, `collections`, `statuses`, `loans`,
  `borrowers`, `scanExclusions`, `aiScanExclusions`). Entry item names come from
  `FileFormatDataLossFields`; custom property losses use one item per key named `property:<key>`.
- Feature plugins can contribute catalog-scope groups by implementing
  `ICatalogAuxiliaryDataProvider.GetAuxiliaryData()` and returning `CatalogAuxiliaryDataGroup` values.
- The host dialog localizes categories and fields, groups items with counts and examples, and defaults to
  "No, Do Not Save" (Esc closes without writing).
- `FileFormatWriteRequest.CatalogScopeItems` carries the same scope snapshot to writers. Formats that
  preserve scope (currently the native JSON catalog) serialize it; see [json-catalog.md](json-catalog.md).

## Plugin icon (SDK 3.0)

Options → Plugins shows one 24 px icon slot in the installed list and in the plugin details header.
Every plugin library ships its own icon inside the assembly, so the icon travels with the DLL to the
build output, the install folder and the NuGet package.

Add `Assets/icon.ico` to the plugin project and embed it under the fixed logical name:

```xml
<ItemGroup>
  <EmbeddedResource Include="Assets/icon.ico" LogicalName="SkyCD.Plugin.Icon.ico" />
</ItemGroup>
```

First-party projects get that item from `Plugins/Directory.Build.props` automatically. Their ICO files
contain 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96 and 256 px images. The CI icon check
verifies the ICO and its sizes.

- The ICO must be at most 256 KiB and contain at least one PNG-encoded frame; the host displays its
  largest square PNG frame. Separate PNG files are unnecessary.
- Use full-color artwork with transparency and enough edge contrast to read on both light and dark themes.
  The host displays the image's original colors.
- Discovery reads the resource from the plugin assembly; there is no attribute, no adjacent file and
  no assembly-name or capability fallback. A plugin without the resource shows the neutral puzzle
  glyph.
- Marketplace catalog entries may declare a `Lucide:<Name>` or `ext:<extension>` key so the browse row
  has an icon before the package is installed; see [marketplace-catalog.md](marketplace-catalog.md).

## Runtime Discovery
- Runtime scans assemblies for concrete capability classes with public parameterless constructors.
- Identity and version constraints come from assembly metadata; no manifest is required.
- Compatibility gate: plugin loads only when `HostVersion >= MinHostVersion`.
- Capabilities are discovered by implemented interfaces deriving from `IPluginCapability`.
- Ownership and account providers use the same discovery, compatibility,
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

Standalone plugin repositories can run SkyCD's host integration tests against their checked-out
source. From a plugin checkout beside a SkyCD checkout, run the host's test script with the path
to the plugin project. For example, from the Sample Modal repository:

```sh
pwsh -File ../SkyCD/tools/scripts/ci/test-standalone-plugin.ps1 -PluginProject src/SkyCD.Plugin.Sample.Modal/SkyCD.Plugin.Sample.Modal.csproj
```

The script substitutes a project reference for that plugin's NuGet package in the host tests.
It also runs the Options tests for Sample Configuration. Ordinary SkyCD test runs continue to
use the published packages.

- `Plugins/SkyCD.Plugin.FileFormat.Json` compiles against the `SkyCD.SDK` package and demonstrates `IFileFormatPluginCapability`.
- [Sample Modal](https://github.com/SkyCD/SkyCD.Plugin.Sample.Modal) demonstrates modal registration and typed request/response payload contracts (`IModalPluginCapability`).
- [Sample Configuration](https://github.com/SkyCD/SkyCD.Plugin.Sample.Config) demonstrates embedded Options drafts (see above).
