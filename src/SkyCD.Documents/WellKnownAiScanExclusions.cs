using System.Collections.Generic;

namespace SkyCD.Documents;

/// <summary>Default MIME types excluded from AI scanning: executables and archives.</summary>
public static class WellKnownAiScanExclusions
{
    /// <summary>Executable and installer MIME types.</summary>
    public static IReadOnlyList<string> ExecutableMimeTypes { get; } =
    [
        "application/x-msdownload",
        "application/x-msdos-program",
        "application/vnd.microsoft.portable-executable",
        "application/x-executable",
        "application/x-sharedlib",
        "application/x-mach-binary",
        "application/vnd.android.package-archive",
        "application/vnd.debian.binary-package",
        "application/x-rpm"
    ];

    /// <summary>Archive and disk-image MIME types.</summary>
    public static IReadOnlyList<string> ArchiveMimeTypes { get; } =
    [
        "application/zip",
        "application/x-zip-compressed",
        "application/x-7z-compressed",
        "application/vnd.rar",
        "application/x-rar-compressed",
        "application/gzip",
        "application/x-gzip",
        "application/x-tar",
        "application/x-bzip2",
        "application/x-xz",
        "application/x-lzma",
        "application/zstd",
        "application/vnd.ms-cab-compressed",
        "application/java-archive",
        "application/x-iso9660-image",
        "application/x-apple-diskimage"
    ];

    /// <summary>Default AI scan exclusions: every executable and archive MIME type.</summary>
    public static IReadOnlyList<string> GetDefaultExcludedMimeTypes()
    {
        var all = new List<string>(ExecutableMimeTypes.Count + ArchiveMimeTypes.Count);
        all.AddRange(ExecutableMimeTypes);
        all.AddRange(ArchiveMimeTypes);
        return all;
    }
}
