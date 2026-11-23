using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Markdown.Models;

namespace SkyCD.Plugin.Metadata.Markdown.Export;

/// <summary>Renders the bounded leading content of a Markdown document to an encoded PNG thumbnail.</summary>
public interface IMarkdownThumbnailRenderer
{
    /// <summary>False when the host has no usable rasterizer, so no page image can be produced.</summary>
    bool IsAvailable { get; }

    /// <summary>Returns encoded PNG bytes, or null when no image can be produced.</summary>
    Task<byte[]?> RenderAsync(
        IReadOnlyList<MarkdownPreviewLine> lines,
        int maxDimension,
        CancellationToken cancellationToken);
}
