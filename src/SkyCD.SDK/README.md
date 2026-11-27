# SkyCD.SDK

The plugin-authoring contract surface for [SkyCD](https://github.com/SkyCD/SkyCD), the catalog app for
physical and digital media. Reference this package to build a plugin; the host discovers plugins from
assembly metadata and the capability interfaces they implement.

## What is in the package

- **Identity and lifecycle** — `IPluginCapability`, `PluginIdAttribute`, `MinHostVersionAttribute`,
  `MaxHostVersionAttribute`.
- **Capabilities** under `SkyCD.SDK.Capabilities.*` — file formats, media sources, previews and metadata,
  online metadata sources, catalog features and imports, menus, modals, configuration pages, CLI commands
  and capture source factories.
- **Host services** a plugin calls back into — `ICatalogApi`, `IHostCommandApi`, `IHostDialogApi`,
  `IPluginStorageApi`, `IOAuthService`, `ILocationApi`, `IDescriptionEntryFactsApi` and their
  `*Consumer` initialization hooks.
- The request, result and descriptor models exchanged across those contracts.

The package contains no third-party or host implementation types. Rendering, parsing and persistence stay
behind host services, so a plugin never takes a dependency on Avalonia, SkiaSharp, XmpCore or database
libraries.

## Minimal plugin

```csharp
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.SDK.Lifecycle;

[assembly: PluginId("com.example.myplugin")]
[assembly: MinHostVersion("3.0.0")]

public sealed class MyPlugin : IPluginConfigurationCapability
{
    // Implement the capability interfaces the host should discover.
}
```

Build the assembly and place it in its own folder under the host's `Plugins` directory. Plugin identity,
version and capabilities come from assembly metadata — there is no manifest file.

## Compatibility

Every plugin declares a minimum host version; an incompatible plugin is skipped instead of crashing the
host. Contract changes follow the SemVer rules in the
[SDK versioning policy](https://github.com/SkyCD/SkyCD/blob/main/docs/plugins/sdk-versioning-policy.md).

Authoring guides: <https://github.com/SkyCD/SkyCD/tree/main/docs/plugins>

## License

BSD-3-Clause
