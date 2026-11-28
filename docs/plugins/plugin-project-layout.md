# Plugin Project Layout

Every plugin project uses the same role-based folder layout, so a file's purpose is visible from
its path and namespace alone. The rules below are enforced by convention and review. Project and
assembly names follow [assembly-naming.md](assembly-naming.md).

## Layout

```
Plugins/SkyCD.Plugin.<Kind>.<Subject>/
  <Name>Plugin.cs              # capability entry points only (IPlugin metadata stays in the assembly)
  Assets/                      # multi-size icon.ico (see sdk-contracts.md)
  Configuration/               # configuration capability, settings editor, configuration window + code-behind
  Models/                      # documents, records, settings, schema, enums, path/limit value types
  Parsing/                     # format detection, readers, parsers, binary decoders, signatures
  Import/                      # import orchestration, mapping, hierarchy builders, work items
  Export/                      # renderers and writers
  Sources/                     # network/remote metadata providers, clients and external-system APIs
  Views/                       # other dialogs, windows, controls (axaml + code-behind)
  Exceptions/                  # exception types, one class per file
```

## Rules

1. Only capability entry types and plugin metadata belong in the project root. Capability entry
   types are the `IPlugin` implementation and types that directly implement a capability interface
   (for example `IAccountConnector`, `IOwnershipProvider` or `ICliPluginCapability`). `AssemblyInfo.cs`
   assembly metadata also stays in the root. Everything else goes in a role folder.
2. Each folder declares the matching namespace: `SkyCD.Plugin.<Kind>.<Subject>.<Folder>`.
   `RootNamespace` stays the project name and is never derived from role folders.
3. Create a folder only when it holds at least one type. Do not add empty scaffolding or a
   single-file `Misc/` bucket.
4. New files go in the role folder with the matching namespace. A flat root is a review finding.
5. Format-specific parents get a nested folder with its own namespace, for example
   `SkyCD.Plugin.FileFormat.DiskExplorer/Ded/` uses `SkyCD.Plugin.FileFormat.DiskExplorer.Ded`, not a
   name prefix in the parent namespace. Apply this same layout inside the nested folder: capability
   entry types in its root and role folders underneath, with the nested namespace as their base.
6. `Plugins/Shared` and the sample plugins follow the same layout.
7. Every plugin project ships `Assets/icon.ico`, embedded as the plugin icon resource. Keep only that
   icon in `Assets/`; see
   [sdk-contracts.md](sdk-contracts.md#plugin-icon-sdk-30).

Multiple capability entry types may share the root, including partial-class files of the same
plugin type. The configuration capability belongs in `Configuration/` together with its editor and
window, because that is the role whose contents it drives.

## Examples

```
src/SkyCD.Plugin.Metadata.EBook/  # standalone repository
  EBookCoverPlugin.cs
  EBookMetadataPlugin.cs
  Configuration/EBookConfigurationPlugin.cs
  Configuration/EBookConfigurationEditor.cs
  Configuration/EBookConfigurationPage.axaml(.cs)
  Models/EBookDocument.cs
  Models/EBookSettings.cs
  Parsing/EBookReader.cs
  Parsing/EpubReader.cs
  Export/EBookCoverRenderer.cs
  Exceptions/...

Plugins/SkyCD.Plugin.FileFormat.DiskExplorer/
  DiskExplorerPlugin.cs
  Ded/DedPlugin.cs                 # nested format parent: capability entry in the nested root
  Ded/Models/...
  Ded/Parsing/...

Plugins/Shared/MediaMetadata/
  Models/MetadataValues.cs
  Parsing/MetadataScan.cs
```

Shared files are linked into the consuming projects with `<Compile Include>`. When shared files
move, update the linked include paths in each consuming `.csproj` in the same commit.

## Migrating a plugin

- Move files with `git mv` so history follows the rename.
- Rewrite the namespace in the same commit as the move and update consumers' `using` directives.
- Keep capability sets and plugin ids unchanged. Discovery reads assembly metadata, not type
  names, but capability enumeration sorts by `type.FullName`, so verify that format preference
  order still resolves the intended handler.
- Run `dotnet build SkyCD.slnx --configuration Release`, `dotnet test SkyCD.slnx --configuration Release --no-build`
  and `dotnet format SkyCD.slnx --verify-no-changes`.
