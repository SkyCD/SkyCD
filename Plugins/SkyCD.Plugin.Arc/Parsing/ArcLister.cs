using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SkyCD.Plugin.Arc.Exceptions;
using SkyCD.Plugin.Arc.Models;

namespace SkyCD.Plugin.Arc.Parsing;

/// <summary>
/// Lists every header of an archive, resolving the two conditions that must not fail a whole catalog: a
/// placeholder end-of-archive byte at offset zero (written by some tools for empty archives) and a truncated
/// tail. Both are reported through the listing diagnostics while the entries before them are kept.
/// </summary>
internal static class ArcLister
{
    public static ArcListing List(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            throw new ArgumentException("ARC listing requires a seekable stream.", nameof(stream));
        }

        stream.Position = 0;
        var diagnostics = new ArcDiagnostics();
        if (stream.Length == 0)
        {
            // An empty placeholder is the documented way to "delete" an ARC file component; nothing to list.
            return new ArcListing([], IsDamaged: false, diagnostics);
        }

        if (stream.ReadByte() == ArcArchiveReader.EndOfArchive)
        {
            return new ArcListing([], IsDamaged: false, diagnostics);
        }

        stream.Position = 0;
        var entries = new List<ArcEntry>();
        var isDamaged = false;
        try
        {
            ArcArchiveReader.ReadEntries(stream, entries, cancellationToken);
        }
        catch (ArcDamagedArchiveException)
        {
            isDamaged = true;
            diagnostics.Add(DiagnosticCodes.Damaged);
        }

        Classify(entries, diagnostics);
        return new ArcListing(entries, isDamaged, diagnostics);
    }

    /// <summary>Counts entries whose data cannot be decoded; they stay listed but are never extracted.</summary>
    private static void Classify(IEnumerable<ArcEntry> entries, ArcDiagnostics diagnostics)
    {
        foreach (var entry in entries)
        {
            if (entry.Method is ArcMethod.Packed or ArcMethod.Unsupported)
            {
                diagnostics.Add(DiagnosticCodes.Unsupported);
            }
        }
    }
}
