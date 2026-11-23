using System;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// One freshly produced preview artifact reported while an entry is being extracted, so a running
/// progress surface can show visual confirmation without reading the cache back.
/// </summary>
public sealed record MediaThumbnail
{
    public MediaThumbnail(string name, byte[] data, string contentType)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(data);
        Name = name;
        Data = data;
        ContentType = contentType ?? string.Empty;
    }

    /// <summary>Display name of the source the thumbnail was produced from.</summary>
    public string Name { get; }

    public byte[] Data { get; }

    public string ContentType { get; }
}
