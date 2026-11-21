using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Works.Models;

/// <summary>
/// Header facts and document summaries parsed from one Microsoft Works word processor source.
/// </summary>
internal sealed class WorksDocument
{
    private static readonly IReadOnlyDictionary<uint, object> NoValues =
        new Dictionary<uint, object>();

    public WorksDocument(string formatVersion, string storage, byte[]? summary, byte[]? documentSummary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(storage);

        FormatVersion = formatVersion;
        Storage = storage;
        Summary = summary is null ? NoValues : WorksPropertySet.Parse(summary);
        DocumentSummary = documentSummary is null ? NoValues : WorksPropertySet.Parse(documentSummary);
    }

    /// <summary>Generation reported by the document header, for example <c>4</c> or <c>2000</c>.</summary>
    public string FormatVersion { get; }

    /// <summary>Whether the document was stored flat or inside an OLE2 compound file.</summary>
    public string Storage { get; }

    /// <summary>Standard summary information values keyed by OLE property identifier.</summary>
    public IReadOnlyDictionary<uint, object> Summary { get; }

    /// <summary>Document summary information values keyed by OLE property identifier.</summary>
    public IReadOnlyDictionary<uint, object> DocumentSummary { get; }

    /// <summary>
    /// Works 2 and some early Works 3 files are flat and start with a one-byte version followed by 0xFE.
    /// </summary>
    public static bool HasFlatHeader(ReadOnlySpan<byte> data) =>
        data.Length >= 2 && data[0] <= 5 && data[1] == 0xFE;
}
