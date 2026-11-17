namespace SkyCD.Plugin.Abstractions.Capabilities.Cli;

/// <summary>
/// Opt-in contract for plugin CLI commands that can emit machine-readable JSON.
/// </summary>
/// <remarks>
/// The CLI host strips the global <c>--json</c> flag before dispatching and sets
/// <see cref="JsonOutput"/> on the resolved command instance when it implements this
/// contract. Commands write JSON to standard output themselves; the host only falls back
/// to a generic success object when a command produced no output at all.
/// </remarks>
public interface ISupportsJsonOutput
{
    /// <summary>
    /// Gets or sets whether the command emits JSON instead of human-readable text.
    /// </summary>
    bool JsonOutput { get; set; }
}
