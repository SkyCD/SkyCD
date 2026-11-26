using System;
using System.IO;
using System.Threading;
using OfficeIMO;
using OfficeIMO.PowerPoint;
using SkyCD.Plugin.Metadata.Office.Models;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Reads a presentation, slideshow or show package through the reading library.
/// </summary>
internal static class OfficePowerPointReader
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
        using var document = PowerPointDocument.Load(stream, OfficeLoadBounds.Presentation(options));

        var metadata = new OfficeMetadata
        {
            Container = OfficeContainers.Ooxml,
            DocumentType = OfficeFamilyResolver.DocumentType(OfficeFamily.Presentation),
            IsTemplate = OfficeFamilyResolver.IsTemplate(extension) || IsTemplateKind(document),
            FormatProfile = document.SourceFormat.ToString().ToLowerInvariant()
        };

        OfficePackageCore.Apply(metadata, () => document.OpenXmlDocument?.CoreFilePropertiesPart);
        OfficePackageExtended.Apply(metadata, () => document.OpenXmlDocument?.ExtendedFilePropertiesPart);

        // The producer records the slide count, and a package that never recorded one still shows how many
        // slides it holds, which the library has already enumerated to open the presentation.
        metadata.SlideCount ??= OfficeGuard.TryCount(() => document.Slides.Count);
        return metadata;
    }

    private static bool IsTemplateKind(PowerPointDocument document) =>
        OfficeGuard.Try(() => document.SourceFormatDescriptor) is { DocumentKind: OfficeDocumentKind.Template };
}
