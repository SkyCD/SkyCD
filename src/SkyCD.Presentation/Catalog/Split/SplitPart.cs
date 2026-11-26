namespace SkyCD.Presentation.Catalog.Split;

/// <summary>A written part catalog.</summary>
/// <param name="Path">The full path of the part file.</param>
/// <param name="EntryCount">The number of catalog entries the part holds.</param>
/// <param name="Bytes">The part file size on disk.</param>
public sealed record SplitPart(string Path, int EntryCount, long Bytes);
