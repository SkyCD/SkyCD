using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

/// <summary>
/// Capability contract for plugins that provide file format read/write support.
/// </summary>
public interface IFileFormatPluginCapability : IPluginCapability
{
    /// <summary>
    /// Gets file format supported by this capability.
    /// </summary>
    FileFormatDescriptor SupportedFormat { get; }

    /// <summary>
    /// Reads structured payload from source stream.
    /// </summary>
    Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes structured payload to target stream.
    /// </summary>
    Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default);
}
