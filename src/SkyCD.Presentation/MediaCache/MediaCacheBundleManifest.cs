using System;
using System.Collections.Generic;

namespace SkyCD.Presentation.MediaCache;

/// <summary>Manifest of a portable media cache bundle.</summary>
public sealed class MediaCacheBundleManifest
{
    public int FormatVersion { get; set; } = MediaCacheBundle.FormatVersion;

    public DateTimeOffset ExportedAtUtc { get; set; }

    public List<MediaCacheRecord> Records { get; set; } = [];
}
