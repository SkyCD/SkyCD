using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Metadata.AbiWord.Export;

/// <summary>
/// Turns a bounded text sample into a page-shaped PNG thumbnail.
/// </summary>
/// <remarks>
/// Rendering is behind an interface so the capability can run without a rasterizer: when no renderer is
/// available the document keeps its metadata and simply reports that no preview exists.
/// </remarks>
public interface IAbiWordThumbnailRenderer
{
    /// <summary>False when the rasterizer cannot run on this platform, so nothing should be read for it.</summary>
    bool IsAvailable { get; }

    /// <summary>Returns the PNG bytes, or null when nothing drawable could be produced.</summary>
    Task<byte[]?> RenderAsync(string text, int maxDimension, int maxCharacters, CancellationToken cancellationToken);
}
