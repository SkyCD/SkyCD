using System.Threading.Tasks;
using SkyCD.Presentation.MediaCache;

namespace SkyCD.Presentation.Operations;

/// <summary>
/// Extra capability of a progress surface that can display previews produced while an operation runs.
/// Kept apart from <see cref="IOperationProgress"/> so progress implementations that only report text
/// stay unchanged.
/// </summary>
public interface IThumbnailProgress
{
    /// <summary>Starts showing a newly produced thumbnail; at most one per processed item is reported.</summary>
    Task ReportThumbnail(MediaThumbnail thumbnail);
}
