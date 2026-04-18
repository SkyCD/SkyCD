namespace SkyCD.Plugin.Runtime.Loading;

/// <summary>
/// Captures plugin load warning/error details.
/// </summary>
public sealed class PluginLoadDiagnostic
{
    public required string PluginId { get; init; }

    public required string Message { get; init; }

    public required bool IsError { get; init; }
}
