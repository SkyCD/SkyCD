namespace SkyCD.Plugin.Feature.Split.Models;

/// <summary>Progress of a split run, reported per written part.</summary>
/// <param name="PartsWritten">The parts completed so far.</param>
/// <param name="TotalParts">The planned part count.</param>
/// <param name="EntriesWritten">The entries written so far.</param>
/// <param name="TotalEntries">The entry count of the source catalog.</param>
public sealed record SplitProgress(int PartsWritten, int TotalParts, int EntriesWritten, int TotalEntries)
{
    /// <summary>Gets the completion percent.</summary>
    public int Percent => TotalParts == 0 ? 0 : PartsWritten * 100 / TotalParts;
}
