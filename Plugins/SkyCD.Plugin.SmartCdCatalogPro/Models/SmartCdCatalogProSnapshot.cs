using System.Collections.Generic;

namespace SkyCD.Plugin.SmartCdCatalogPro.Models;

/// <summary>
/// Raw table contents read from a Smart CD Catalog PRO catalog. Optional tables are
/// empty when the source predates them or never used them.
/// </summary>
internal sealed class SmartCdCatalogProSnapshot
{
    public required IReadOnlyList<SmartCdRow> Cds { get; init; }

    public required IReadOnlyList<SmartCdRow> Paths { get; init; }

    public required IReadOnlyList<SmartCdRow> Elements { get; init; }

    public IReadOnlyList<SmartCdRow> Categories { get; init; } = [];

    public IReadOnlyList<SmartCdRow> Boxes { get; init; } = [];

    public IReadOnlyList<SmartCdRow> Borrows { get; init; } = [];

    public IReadOnlyList<SmartCdRow> LoanHistory { get; init; } = [];

    public SmartCdAccessMode? AccessMode { get; init; }
}

internal sealed record SmartCdAccessMode(
    bool DefaultIsFull,
    bool HasFullPassword,
    bool HasReadOnlyPassword);
