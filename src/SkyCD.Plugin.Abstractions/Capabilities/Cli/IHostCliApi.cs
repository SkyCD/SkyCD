using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.Abstractions.Capabilities.Cli;

/// <summary>
/// Safe host operations available for CLI plugins.
/// </summary>
public interface IHostCliApi
{
    IReadOnlyList<FileFormatDescriptor> GetReadableFormats();

    IReadOnlyList<FileFormatDescriptor> GetWritableFormats();

    Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default);

    Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default);
}
