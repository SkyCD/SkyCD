using System.Collections.Generic;
using SkyCD.Plugin.Ai.GenDesc.Import;

namespace SkyCD.Plugin.Ai.GenDesc.Sources;

/// <summary>
/// The built-in set of web sources the plugin ships with. Adding a source means adding an importer here; the
/// orchestrator and the plugin resolve sources only through <see cref="IWebSourceImporter.SourceId"/>.
/// </summary>
public static class WebSourceCatalog
{
    /// <summary>Creates a fresh importer instance per known source id (importers are stateless).</summary>
    public static IReadOnlyList<IWebSourceImporter> CreateAll() =>
    [
        new WikipediaImporter(),
        new ImdbImporter(),
        new AniDbImporter(),
        new AnimeNewsNetworkImporter(),
        new TvComImporter()
    ];
}
