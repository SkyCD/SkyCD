namespace SkyCD.Plugin.Metadata.Archives.Models;

/// <summary>An entry that was listed but contributed no work item, with its typed diagnostic code.</summary>
internal sealed record ArchiveSkipEntry(string Name, string Code, string Reason)
{
    /// <summary>Formats the entry as one host-facing diagnostic line.</summary>
    public string Describe() =>
        string.IsNullOrEmpty(Name) ? $"{Code}: {Reason}" : $"{Code}: {Name}: {Reason}";
}
