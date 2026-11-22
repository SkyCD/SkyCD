using System.Threading;
using System.Threading.Tasks;
using SkyCD.Formatting.Rtf;

namespace SkyCD.Plugin.Metadata.Rtf.Export;

/// <summary>Renders a parsed RTF document to an encoded PNG thumbnail.</summary>
public interface IRtfThumbnailRenderer
{
    /// <summary>False when the host has no usable rasterizer, so no source bytes need to be read.</summary>
    bool IsAvailable { get; }

    /// <summary>Returns encoded PNG bytes, or null when no image can be produced.</summary>
    Task<byte[]?> RenderAsync(
        RichTextDocument document,
        int maxDimension,
        int maxCharacters,
        CancellationToken cancellationToken);
}
