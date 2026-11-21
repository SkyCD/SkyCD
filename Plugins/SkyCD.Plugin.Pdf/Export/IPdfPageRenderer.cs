using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Pdf.Export;

/// <summary>
/// Renders one page of a PDF to an encoded image.
/// </summary>
/// <remarks>
/// Rendering needs a native PDF rasterizer, which is a heavier and more failure-prone dependency than the
/// managed parser. Keeping it behind this seam means a missing or unloadable rasterizer degrades to a
/// metadata-only result instead of failing the extraction, and the plugin stays testable without one.
/// </remarks>
public interface IPdfPageRenderer
{
    /// <summary>True when a rasterizer is usable on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Renders <paramref name="pageIndex"/> (zero-based) no larger than <paramref name="maxDimension"/> on
    /// its longest edge, returning PNG bytes, or null when the page cannot be rendered.
    /// </summary>
    Task<byte[]?> RenderPageAsync(byte[] document, int pageIndex, int maxDimension, CancellationToken cancellationToken);
}
