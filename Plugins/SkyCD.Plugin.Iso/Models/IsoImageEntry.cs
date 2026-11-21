using System;

namespace SkyCD.Plugin.Iso.Models;

/// <summary>One folder or file discovered inside an ISO 9660 or UDF image.</summary>
internal sealed record IsoImageEntry(
    string Path,
    string ReaderPath,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset? ModifiedUtc);
