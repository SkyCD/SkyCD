namespace SkyCD.SDK.Capabilities.FileFormats;

/// <summary>
/// Optional capability for file formats that can report which catalog data a write would drop.
/// Formats that do not implement it are treated as lossless by the host.
/// </summary>
public interface IDataLossAwareFileFormatCapability : IFileFormatPluginCapability
{
    /// <summary>
    /// Gets whether writes carry catalog-scope data such as collections, statuses, loans and scan rules.
    /// When false, the host reports every catalog-scope item supplied in the request.
    /// </summary>
    bool PreservesCatalogScope { get; }

    /// <summary>
    /// Computes the data the format cannot represent from the supplied snapshot.
    /// Implementations must not perform I/O.
    /// </summary>
    FileFormatDataLossAssessment AssessDataLoss(FileFormatDataLossRequest request);
}
