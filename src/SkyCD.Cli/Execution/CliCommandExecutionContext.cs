using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Cli.Execution;

internal sealed record CliCommandExecutionContext(
    CliHost Host,
    bool JsonOutput,
    FileFormatManager FileFormatManager,
    IHostCliApi HostApi,
    CliContributionRegistry Registry,
    IReadOnlyList<DiscoveredPlugin> DiscoveredPlugins,
    IReadOnlyList<string> PluginDirectories,
    CancellationToken CancellationToken);
