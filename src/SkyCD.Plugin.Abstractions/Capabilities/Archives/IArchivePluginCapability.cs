using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.Capabilities.Archives;

/// <summary>
/// Capability contract for plugins that inspect and safely extract archive contents.
/// </summary>
public interface IArchivePluginCapability : IPluginCapability
{
    /// <summary>Gets the archive format supported by this capability.</summary>
    ArchiveFormatDescriptor SupportedArchive { get; }

    /// <summary>
    /// Lists archive entries without extracting them.
    /// </summary>
    Task<ArchiveInspectionResult> InspectAsync(ArchiveInspectionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts supported entries into a unique temporary directory. The returned result owns the directory
    /// and deletes it on disposal; callers must dispose the result when metadata indexing completes,
    /// including on failure or cancellation.
    /// </summary>
    Task<ArchiveExtractionResult> ExtractToTemporaryDirectoryAsync(ArchiveExtractionRequest request,
        CancellationToken cancellationToken = default);
}
