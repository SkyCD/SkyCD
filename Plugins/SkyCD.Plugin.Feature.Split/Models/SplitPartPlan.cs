namespace SkyCD.Plugin.Feature.Split.Models;

/// <summary>A planned part: the entry range it owns and the file it will be written to.</summary>
/// <param name="PartNumber">The 1-based part number the naming pattern is expanded with.</param>
/// <param name="FileName">The part file name, filled in once the total part count is known.</param>
/// <param name="FirstEntry">The index of the first catalog entry the part owns.</param>
/// <param name="EntryCount">The number of catalog entries the part owns.</param>
/// <param name="Bytes">The projected part size on disk, format preamble included.</param>
public sealed record SplitPartPlan(
    int PartNumber,
    string FileName,
    int FirstEntry,
    int EntryCount,
    long Bytes);
