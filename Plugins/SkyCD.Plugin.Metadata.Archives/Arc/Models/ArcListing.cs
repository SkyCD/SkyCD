using System;
using System.Collections.Generic;
using SkyCD.Plugin.Metadata.Archives.Arc.Parsing;

namespace SkyCD.Plugin.Metadata.Archives.Arc.Models;

/// <summary>Every header found in an archive plus the structural problems the listing tolerated.</summary>
internal sealed record ArcListing(IReadOnlyList<ArcEntry> Entries, bool IsDamaged, ArcDiagnostics Diagnostics)
{
    /// <summary>Gets the recorded codes, in first-seen order.</summary>
    public IReadOnlyCollection<string> DiagnosticCodes => Diagnostics.Codes;

    /// <summary>Gets the diagnostic summary, for example <c>ARC_UNSUPPORTED (2 entries), ARC_DAMAGED (1 entry)</c>.</summary>
    public string Summary => ArcDiagnosticText.Join(Diagnostics);
}

/// <summary>The diagnostic codes this plugin reports.</summary>
internal static class DiagnosticCodes
{
    /// <summary>The archive is truncated or structurally invalid.</summary>
    public const string Damaged = "ARC_DAMAGED";

    /// <summary>An entry uses a compression method this plugin does not decode.</summary>
    public const string Unsupported = "ARC_UNSUPPORTED";

    /// <summary>Rejects codes outside the published set so failures stay typed and searchable.</summary>
    public static void Validate(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (code is not (Damaged or Unsupported))
        {
            throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown ARC diagnostic code.");
        }
    }
}
