using System;
using System.IO;
using System.Threading;
using OfficeIMO.Visio;
using SkyCD.Plugin.Metadata.Office.Models;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Reads a Visio drawing, template or stencil package: the document properties and the page list, nothing else.
/// </summary>
/// <remarks>
/// Shapes, connectors and masters are the drawing itself, not a fact about it, so the reader stops at the page
/// count. The preview keeps coming from the embedded <c>docProps/thumbnail.jpeg</c> part rather than from
/// rendering the drawing, because a scan must not pay for a rasterizer.
/// </remarks>
internal static class OfficeVisioReader
{
    public static OfficeMetadata Read(
        byte[] data,
        OfficePluginOptions options,
        string? extension,
        CancellationToken cancellationToken) =>
        OfficeProtection.Read(OfficeContainers.Ooxml,
            () => ReadOpened(data, options, extension, cancellationToken));

    private static OfficeMetadata ReadOpened(
        byte[] data,
        OfficePluginOptions options,
        string? extension,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = new MemoryStream(data, writable: false);
        using var document = VisioDocument.Load(stream, OfficeLoadBounds.Drawing(options));

        return new OfficeMetadata
        {
            Container = OfficeContainers.Ooxml,
            DocumentType = OfficeFamilyResolver.DocumentType(OfficeFamily.Drawing),
            IsTemplate = OfficeFamilyResolver.IsTemplate(extension) || IsAuthoringShape(document),
            Title = OfficeGuard.Try(() => OfficeText.Normalize(document.Properties?.Title)),
            Author = OfficeGuard.Try(() => OfficeText.Normalize(document.Properties?.Author)),
            PageCount = OfficeGuard.TryCount(() => document.Pages.Count),
            FormatProfile = OfficeGuard.Try(() => document.PackageType.ToString()?.ToLowerInvariant())
        };
    }

    private static bool IsAuthoringShape(VisioDocument document) =>
        OfficeGuard.TryBool(() => document.IsTemplate) || OfficeGuard.TryBool(() => document.IsStencil);
}
