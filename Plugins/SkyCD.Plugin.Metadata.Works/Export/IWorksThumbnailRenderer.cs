using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Metadata.Works.Export;

/// <summary>
/// Draws a Works document's leading text as a page-shaped PNG.
/// </summary>
/// <remarks>
/// The interface keeps a missing rasterizer separate from a document that cannot be drawn: an unavailable
/// renderer returns no image instead of failing preview generation, exactly like the RTF and PDF plugins.
/// </remarks>
public interface IWorksThumbnailRenderer
{
    /// <summary>False when the rasterizer cannot run in this environment.</summary>
    bool IsAvailable { get; }

    /// <summary>Renders the paragraphs, or returns null when nothing can be drawn.</summary>
    Task<byte[]?> RenderAsync(
        IReadOnlyList<string> paragraphs,
        int maxDimension,
        int maxCharacters,
        CancellationToken cancellationToken);
}
