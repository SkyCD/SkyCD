namespace SkyCD.Plugin.Feature.Split.Models;

/// <summary>Progress of a running split, reported once per finished part.</summary>
public sealed record SplitProgress(int PartsWritten, int TotalParts, int EntriesWritten, int TotalEntries)
{
    /// <summary>Gets the share of planned parts already written, as a whole percentage.</summary>
    public int Percent => TotalParts <= 0 ? 0 : PartsWritten * 100 / TotalParts;
}
