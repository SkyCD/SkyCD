namespace SkyCD.Plugin.Host.Menu;

/// <summary>
/// Result envelope for guarded menu command execution.
/// </summary>
public sealed class MenuCommandExecutionResult
{
    public required bool Success { get; init; }

    public string? Error { get; init; }

    public bool TimedOut { get; init; }
}
