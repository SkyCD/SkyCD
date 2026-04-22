using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.Abstractions.Capabilities.Cli;

/// <summary>
/// Safe host operations available for CLI plugins.
/// </summary>
public interface IHostCliApi
{
    /// <summary>
    /// Returns file formats that currently support read operations.
    /// </summary>
    IReadOnlyList<FileFormatDescriptor> GetReadableFormats();

    /// <summary>
    /// Returns file formats that currently support write operations.
    /// </summary>
    IReadOnlyList<FileFormatDescriptor> GetWritableFormats();

    /// <summary>
    /// Reads input using the selected format handler.
    /// </summary>
    Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes output using the selected format handler.
    /// </summary>
    Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default);
}
