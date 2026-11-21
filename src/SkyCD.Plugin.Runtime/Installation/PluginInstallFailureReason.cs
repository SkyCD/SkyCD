using System;

namespace SkyCD.Plugin.Runtime.Installation;

/// <summary>
/// Stable failure categories reported by staged plugin installs and updates.
/// </summary>
public enum PluginInstallFailureReason
{
    /// <summary>The operation did not fail.</summary>
    None = 0,

    /// <summary>The NuGet service index could not be used or did not expose a package base address.</summary>
    FeedUnavailable,

    /// <summary>The package download failed, timed out, was cancelled, or exceeded the size limit.</summary>
    DownloadFailed,

    /// <summary>The downloaded package is not a readable NuGet package archive.</summary>
    ArchiveCorrupt,

    /// <summary>The package contains entries that would escape the staging or install location.</summary>
    UnsafeArchivePath,

    /// <summary>The package manifest is missing or declares a different package id or version.</summary>
    PackageIdentityMismatch,

    /// <summary>The plugin assembly is missing, unreadable, or does not declare a plugin identity.</summary>
    InvalidPluginAssembly,

    /// <summary>The plugin declares a host version range that excludes the running host.</summary>
    IncompatibleHostVersion,

    /// <summary>The package would replace a different plugin identity or a non-catalog plugin.</summary>
    IdentityConflict,

    /// <summary>The plugin files are in use or the install location cannot be written.</summary>
    DestinationUnavailable
}
