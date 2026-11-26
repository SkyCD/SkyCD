using System;

namespace SkyCD.Plugin.Metadata.Notebook.Models;

/// <summary>Bounds the notebook plugin applies on top of the host's own limits.</summary>
public sealed record NotebookPluginOptions
{
    public NotebookPluginOptions(
        long maxSourceBytes = 16 * 1024 * 1024,
        int maxCells = 10_000,
        int maxCellCharacters = 1_000_000,
        int maxMetadataValueCharacters = 2048,
        int maxCellTitles = 8,
        int maxTitleCharacters = 200)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCells, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxCells, 100_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCellCharacters, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxCellCharacters, 16_000_000);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMetadataValueCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCellTitles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTitleCharacters);

        MaxSourceBytes = maxSourceBytes;
        MaxCells = maxCells;
        MaxCellCharacters = maxCellCharacters;
        MaxMetadataValueCharacters = maxMetadataValueCharacters;
        MaxCellTitles = maxCellTitles;
        MaxTitleCharacters = maxTitleCharacters;
    }

    /// <summary>Sources larger than this are rejected before any byte is read.</summary>
    public long MaxSourceBytes { get; }

    /// <summary>Cells inspected before the reader stops, matching the reader's own cell budget.</summary>
    public int MaxCells { get; }

    /// <summary>Source characters retained per cell before the reader truncates it.</summary>
    public int MaxCellCharacters { get; }

    /// <summary>Upper bound applied to each published text value.</summary>
    public int MaxMetadataValueCharacters { get; }

    /// <summary>Upper bound of the cell titles published as one list.</summary>
    public int MaxCellTitles { get; }

    /// <summary>Upper bound of a single published cell title.</summary>
    public int MaxTitleCharacters { get; }

    public static NotebookPluginOptions Default { get; } = new();
}
