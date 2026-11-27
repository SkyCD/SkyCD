# Plugin Assembly Naming

Every first-party plugin project under `Plugins/` and the `.dll` it produces use a name that states
the plugin's purpose:

```
SkyCD.Plugin.<Kind>.<Subject>[.<Variant>]
```

Third-party plugins are out of scope. Discovery scans `*.dll` and reads identity from assembly
metadata, so external authors can keep any file name.

## Kinds

`<Kind>` is a closed list. It describes what the plugin does, not which interface it implements.
The plugin manager shows the assembly file name, so the kind must be meaningful to a reader.

Format roles — what the plugin does with files:

| Kind | Meaning |
| --- | --- |
| `FileFormat` | A format the user opens through File > Open or writes through File > Save As, implemented as `IFileFormatPluginCapability` (read or write support is enough), including content-aware, folder-aware and index work-item companions that belong to the same reader. |
| `Metadata` | Data collected by inspecting files or media while a path is scanned: `IMediaMetadataCapability`, `ILocalMediaMetadataCapability`, `IMediaTargetMetadataCapability` (facts about any catalog medium), and entry listings contributed through `IIndexWorkItemProvider` (archives, disk images, containers) that become subentries of generated catalog entries. |
| `Preview` | Implements `IMediaPreviewCapability` or `ILocalMediaPreviewCapability` as the assembly's own purpose. |
| `Config` | Implements `IPluginConfigurationCapability` as the assembly's own purpose. |

Feature / domain roles — what the plugin adds to the application when it is not a format reader:

| Kind | Meaning |
| --- | --- |
| `Feature` | User-facing catalog tools and workflows that hang off a host surface (menu, modal, embedded window) but are features in their own right, such as motion-capture import or the web browser. |
| `Accounts` | Account sync, ownership and connectors. |
| `Ai` | Description generation implementations and providers. |
| `Volume` | Media metadata that only applies to mounted volumes and optical data discs, implemented through `IMediaTargetMetadataCapability` with a volume-only `Supports`. |
| `Sample` | Sample plugins. `<Subject>` names the capability it demonstrates (`Sample.Menu`, `Sample.Modal`, `Sample.Config`). |

`<Subject>` is the format, product, brand or feature the plugin handles (`Zip`, `EBook`, `AudioCD`,
`WinCatalog`, `MotionDetector`, `Tar`). It can be a generation marker plus format (`Legacy.Scd`).
`<Variant>` distinguishes supplementary assemblies of the same subject (`Xml`, `Csv`, `Native`).

## Multi-capability assemblies

One assembly may implement several capabilities. The name carries the most specific purpose using a
fixed precedence so the decision is not case-by-case:

```
FileFormat > Metadata > Preview > Config > Feature
```

A feature plugin that also contributes a configuration surface stays `Feature`. Archive and
disk-image indexers are `Metadata`: their output is the entry listing collected while scanning a
path, not a document the user opens or saves. Disk-image indexers are still registered as read-only
`FileFormat` plugins today and are being corrected in #868; name them `Metadata` once that lands.
`SkyCD.Plugin.FileFormat.Tar` stays the container plugin and `SkyCD.Plugin.Metadata.Archives` is the
single entry metadata provider for every archive format.

## Casing

- PascalCase tokens.
- Two-letter acronyms stay uppercase: `CD`, `UI`, `AI`, `MCP`.
- Three-or-more-letter acronyms use normal casing: `Iso`, `Pdf`, `Csv`, `Json`, `Xml`, `Zip`.
- Brand names keep their official single-token spelling: `GCstar`, `IWork`, `WhereIsIt`, `AbeMeda`,
  `NeoFinder`.

## Interfaces are evidence, not identity

`IMenuPluginCapability` says only where a plugin appears. `IIndexWorkItemProvider` says only how data
is produced. Decide the kind from the data or feature the plugin produces.

`Menu`, `Modal` and `Cli` are host surfaces, not kinds. They must not name a production plugin. They
remain acceptable as sample subjects because samples exist to demonstrate a surface.

Example: `SkyCD.Plugin.MotionDetector` contributes a capture source to Add To List and imports the
captured files, so it is `Feature.MotionDetector`, not a menu plugin. `WebBrowser` opens a user-facing tool window, so it
is `Feature.WebBrowser`.

## Examples

| Old | New | Why |
| --- | --- | --- |
| `SkyCD.Plugin.Zip` | `SkyCD.Plugin.Metadata.Archives` | Archive indexer; one assembly provides entry listings for every archive format. |
| `SkyCD.Plugin.Iso` | `SkyCD.Plugin.Metadata.DiskImages` | Disk-image metadata provider; one assembly provides entry listings for every disk-image format. |
| `SkyCD.Plugin.WinCatalog.Xml` | `SkyCD.Plugin.FileFormat.WinCatalog` | File format, vendor subject; CSV/XML/native capabilities consolidated in one assembly. |
| `SkyCD.Plugin.Legacy.Scd` | `SkyCD.Plugin.FileFormat.Legacy.Scd` | File format, legacy generation. |
| `SkyCD.Plugin.EBook` | `SkyCD.Plugin.Metadata.EBook` | Metadata primary; preview and config secondary. |
| `SkyCD.Plugin.AudioCd` | `SkyCD.Plugin.Metadata.AudioCD` | Audio CD metadata primary; config secondary. |
| `SkyCD.Plugin.Markdown.Metadata` | `SkyCD.Plugin.Metadata.Markdown` | Metadata, kind first instead of aspect suffix. |
| `SkyCD.Plugin.Tar.Metadata` | `SkyCD.Plugin.Metadata.Archives` | Archive entry listing is metadata for generated entries' subitems. |
| `SkyCD.Plugin.VolumeSerial` | `SkyCD.Plugin.Metadata.Volume` | Volume metadata. |
| `SkyCD.Plugin.MotionDetector` | `SkyCD.Plugin.Feature.MotionDetector` | Capture/import feature reached from the Add To List capture source. |
| `SkyCD.Plugin.WebBrowser` | `SkyCD.Plugin.Feature.WebBrowser` | User-facing tool window. |
| `SkyCD.Plugin.AiDescription` | `SkyCD.Plugin.Ai.Description` | Description implementation. |
| `SkyCD.Plugin.AiProvider` | `SkyCD.Plugin.Ai.Provider` | Provider settings and planner. |
| `SkyCD.Plugin.Accounts` | `SkyCD.Plugin.Accounts.Core` | Core account commands. |
| `SkyCD.Plugin.Accounts.JetBrains` | `SkyCD.Plugin.Accounts.JetBrains` | Already conforms. |
| `SkyCD.Plugin.Sample.Configuration` | `SkyCD.Plugin.Sample.Config` | Sample of the config capability. |
| `SkyCD.Plugin.Sample.Menu` | `SkyCD.Plugin.Sample.Menu` | Sample of the menu surface. |

## Enforcement

- `Directory.Build.props` sets `AssemblyName` to `$(MSBuildProjectName)`, so the `.dll` name and the
  project/folder name cannot silently diverge.
- `tools/scripts/ci/check-naming.ps1` fails when a project folder, `.csproj` name and assembly name
  differ, when a plugin name does not match the grammar or uses an unknown kind, when a production
  plugin uses a surface word, when two projects produce the same assembly simple name, or when a test
  project does not follow `<subject>.Tests`. A test subject may also be a source-linked folder under
  `Plugins/Shared/`, which has no assembly of its own. It runs in `.github/workflows/test.yml` and
  `tools/scripts/ci/validate.ps1`.

## Plugin ids

Renaming an assembly must not change its `PluginId`. When a plugin has no explicit
`[assembly: PluginId]`, the id defaults to the assembly simple name, so pin the current id before
renaming. Persisted plugin documents, enabled states and configuration keys are keyed by id.
