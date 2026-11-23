using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Metadata.Text.Export;

/// <summary>Renders a bounded text excerpt to an encoded PNG thumbnail.</summary>
public interface ITextThumbnailRenderer
{
    /// <summary>False when the host has no usable rasterizer, so no page image can be produced.</summary>
    bool IsAvailable { get; }

    /// <summary>Returns encoded PNG bytes, or null when no image can be produced.</summary>
    Task<byte[]?> RenderAsync(string text, int maxDimension, CancellationToken cancellationToken);
}
