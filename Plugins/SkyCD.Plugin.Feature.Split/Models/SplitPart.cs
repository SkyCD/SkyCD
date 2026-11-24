namespace SkyCD.Plugin.Feature.Split.Models;

/// <summary>A part file that was written and verified.</summary>
public sealed record SplitPart(string Path, int EntryCount, long Bytes);
