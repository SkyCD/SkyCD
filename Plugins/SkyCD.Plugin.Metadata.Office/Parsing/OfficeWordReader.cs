using System;
using System.IO;
using System.Threading;
using OfficeIMO;
using OfficeIMO.Word;
using SkyCD.Plugin.Metadata.Office.Models;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Reads a Word package through the reading library, which locates the metadata parts by their relationships.
/// </summary>
internal static class OfficeWordReader
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
        using var document = WordDocument.Load(stream, OfficeLoadBounds.Word(options));

        var metadata = new OfficeMetadata
        {
            Container = OfficeContainers.Ooxml,
            DocumentType = OfficeFamilyResolver.DocumentType(OfficeFamily.Word),
            IsTemplate = OfficeFamilyResolver.IsTemplate(extension) || IsTemplateKind(document),
            FormatProfile = document.SourceFormat.ToString().ToLowerInvariant()
        };

        OfficePackageCore.Apply(metadata, () => document.OpenXmlDocument?.CoreFilePropertiesPart);
        OfficePackageExtended.Apply(metadata, () => document.OpenXmlDocument?.ExtendedFilePropertiesPart);
        return metadata;
    }

    private static bool IsTemplateKind(WordDocument document) =>
        OfficeGuard.Try(() => document.SourceFormatDescriptor) is { DocumentKind: OfficeDocumentKind.Template };
}
