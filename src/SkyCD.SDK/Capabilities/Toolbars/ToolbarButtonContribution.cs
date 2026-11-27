namespace SkyCD.SDK.Capabilities.Toolbars;

/// <summary>An entry command and the host icon used to represent it.</summary>
public sealed record ToolbarButtonContribution(string CommandId, string IconKey)
{
    /// <summary>Optional localized button label. Defaults to the entry command title.</summary>
    public string? Label { get; init; }
}
