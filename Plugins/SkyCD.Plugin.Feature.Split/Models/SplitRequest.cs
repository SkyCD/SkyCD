namespace SkyCD.Plugin.Feature.Split.Models;

/// <summary>One requested split run: which catalog, where the parts go, and where each part stops.</summary>
public sealed record SplitRequest
{
    /// <summary>Gets the legacy text catalog the parts are cut from. It is never modified.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Gets the folder the part files are written to.</summary>
    public required string OutputFolder { get; init; }

    /// <summary>Gets the part file naming pattern, for example <c>part #N.scd</c>.</summary>
    public required string FileNamePattern { get; init; }

    /// <summary>Gets the maximum entries per part, or 0 to leave the entry count unbounded.</summary>
    public int MaxEntriesPerPart { get; init; }

    /// <summary>Gets the maximum part size in bytes, or 0 to leave the part size unbounded.</summary>
    public long MaxPartSizeBytes { get; init; }
}
