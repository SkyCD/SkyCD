namespace SkyCD.SDK.Capabilities.Menu;

/// <summary>
/// Describes a host menu contribution exposed by a plugin. <paramref name="RequiresLocalFile"/> marks a
/// contribution that only makes sense for one selected local file; the host keeps it disabled for folders,
/// network entries, media entries and links. <paramref name="RequiresWritableFormat"/> marks a contribution
/// that writes the open catalog; the host keeps it disabled while no loaded file-format plugin can write.
/// </summary>
public sealed record MenuContribution(
    string CommandId,
    string Title,
    string Location,
    int Order = 0,
    bool RequiresLocalFile = false,
    bool RequiresWritableFormat = false)
{
    /// <summary>
    /// Binary-compatibility overload for plugins compiled against the pre-#1139 contract, which declared
    /// only the four leading parameters. Optional record parameters are baked into the call site, so
    /// without this overload those assemblies throw <see cref="System.MissingMethodException"/> at runtime.
    /// </summary>
    public MenuContribution(string commandId, string title, string location, int order)
        : this(commandId, title, location, order, false, false)
    {
    }

    /// <summary>
    /// Binary-compatibility overload for plugins compiled before <c>RequiresWritableFormat</c> was added.
    /// </summary>
    public MenuContribution(string commandId, string title, string location, int order, bool requiresLocalFile)
        : this(commandId, title, location, order, requiresLocalFile, false)
    {
    }
}
