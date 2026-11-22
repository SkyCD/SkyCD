using SharpCompress.Common;

namespace SkyCD.Plugin.FileFormat.Rar.Exceptions;

/// <summary>
/// Raised when an archive entry continues in a companion volume that is not available.
/// </summary>
internal sealed class MultiVolumeMissingException()
    : MultiVolumeExtractionException("The archive entry continues in a volume that is not available.");
