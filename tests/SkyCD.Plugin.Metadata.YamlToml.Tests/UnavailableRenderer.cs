using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.YamlToml.Export;
using SkyCD.Plugin.Metadata.YamlToml.Models;

namespace SkyCD.Plugin.Metadata.YamlToml.Tests;

internal sealed class UnavailableRenderer : IYamlTomlThumbnailRenderer
{
    public bool IsAvailable => false;

    public Task<byte[]?> RenderAsync(
        IReadOnlyList<YamlTomlPreviewLine> lines,
        int maxDimension,
        CancellationToken cancellationToken) =>
        Task.FromResult<byte[]?>(null);
}
