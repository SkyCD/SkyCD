namespace SkyCD.Documents;

/// <summary>
/// One entry of the customizable application toolbar. The layout stores stable command ids, never UI objects,
/// so a saved toolbar survives element changes, localization switches and removed commands.
/// </summary>
public sealed class ToolbarItemDocument
{
    public const string CommandKind = "command";
    public const string SeparatorKind = "separator";

    public string Kind { get; set; } = CommandKind;

    public string CommandId { get; set; } = string.Empty;

    public static ToolbarItemDocument Command(string commandId) => new()
    {
        Kind = CommandKind,
        CommandId = commandId
    };

    public static ToolbarItemDocument Separator() => new() { Kind = SeparatorKind };
}
