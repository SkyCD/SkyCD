using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.YamlToml.Models;

namespace SkyCD.Plugin.Metadata.YamlToml.Export;

/// <summary>Renders the bounded leading structure of a YAML or TOML document to an encoded PNG thumbnail.</summary>
public interface IYamlTomlThumbnailRenderer
{
    /// <summary>False when the host has no usable rasterizer, so no page image can be produced.</summary>
    bool IsAvailable { get; }

    /// <summary>Returns encoded PNG bytes, or null when no image can be produced.</summary>
    Task<byte[]?> RenderAsync(
        IReadOnlyList<YamlTomlPreviewLine> lines,
        int maxDimension,
        CancellationToken cancellationToken);
}
