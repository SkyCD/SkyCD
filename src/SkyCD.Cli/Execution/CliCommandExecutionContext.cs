using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Cli.Execution;

internal sealed record CliCommandExecutionContext(
    CliHost Host,
    bool JsonOutput,
    FileFormatManager FileFormatManager,
    CliContributionRegistry Registry,
    IReadOnlyList<DiscoveredPlugin> DiscoveredPlugins,
    IReadOnlyList<string> PluginDirectories,
    CancellationToken CancellationToken);
