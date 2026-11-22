using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Plugin.Metadata.Archives.Arc.Parsing;

/// <summary>Formats diagnostic codes with their occurrence counts.</summary>
internal static class ArcDiagnosticText
{
    /// <summary>Formats a code with its count, for example <c>ARC_UNSUPPORTED (2 entries)</c>.</summary>
    public static string Format(ArcDiagnostics diagnostics, string code)
    {
        var count = diagnostics.Count(code);
        return count switch
        {
            0 => code,
            1 => $"{code} (1 entry)",
            _ => $"{code} ({count} entries)"
        };
    }

    /// <summary>Joins every recorded code into one sentence in first-seen order.</summary>
    public static string Join(ArcDiagnostics diagnostics) =>
        string.Join(", ", diagnostics.Codes.Select(code => Format(diagnostics, code)));
}
