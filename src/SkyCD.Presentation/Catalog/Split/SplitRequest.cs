namespace SkyCD.Presentation.Catalog.Split;

/// <summary>One requested split run: which catalog entries, where the parts go, and where each part stops.</summary>
public sealed record SplitRequest
{
    /// <summary>Gets the display name of the source catalog the parts are cut from.</summary>
    public required string SourceName { get; init; }

    /// <summary>Gets the folder the part files are written to.</summary>
    public required string OutputFolder { get; init; }

    /// <summary>Gets the part file naming pattern, for example <c>part #N.scd</c>.</summary>
    public required string FileNamePattern { get; init; }

    /// <summary>Gets the saving format the parts are written in.</summary>
    public required string FormatId { get; init; }

    /// <summary>Gets the maximum entries per part. Splitting always stops at entry boundaries.</summary>
    public required int MaxEntriesPerPart { get; init; }

    /// <summary>Gets the maximum part size in bytes, or 0 to leave the part size unbounded.</summary>
    public long MaxPartSizeBytes { get; init; }
}
