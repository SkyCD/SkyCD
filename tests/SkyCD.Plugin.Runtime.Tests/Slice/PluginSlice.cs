using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;

namespace Tests.PluginSlice;

// The test project copies this assembly into lib/<tfm>/ of fixture packages. It is never loaded by the installer.

/// <summary>Plugin container marker used only as package content.</summary>
public sealed class SlicePlugin;

/// <summary>File format capability used only as package content.</summary>
public sealed class SliceFormatCapability : IFileFormatPluginCapability
{
    public FileFormatDescriptor SupportedFormat { get; } = new("slip.slice", "Slip slice", [".slip"],
        ["application/x-slip"], true, false);

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatReadResult { Success = false, Error = "Slice content is never executed." });

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatWriteResult { Success = false, Error = "Slice content is never executed." });
}

/// <summary>Marker that would run code if the installer loaded assemblies.</summary>
[Obsolete("Not executed by the installer.", error: false)]
public static class SliceMarkers
{
    public static IReadOnlyCollection<string> All => ["slice"];
}
