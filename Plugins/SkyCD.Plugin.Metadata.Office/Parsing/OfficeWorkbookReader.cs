using System;
using System.IO;
using System.Threading;
using OfficeIMO;
using OfficeIMO.Excel;
using SkyCD.Plugin.Metadata.Office.Models;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Reads a workbook package, including the binary <c>.xlsb</c> package, through the reading library.
/// </summary>
internal static class OfficeWorkbookReader
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
        using var document = ExcelDocument.Load(stream, OfficeLoadBounds.Workbook(options));

        var metadata = new OfficeMetadata
        {
            Container = OfficeContainers.Ooxml,
            DocumentType = OfficeFamilyResolver.DocumentType(OfficeFamily.Workbook),
            IsTemplate = OfficeFamilyResolver.IsTemplate(extension) || IsTemplateKind(document),
            FormatProfile = document.SourceFormat.ToString().ToLowerInvariant(),
            SheetCount = OfficeGuard.TryCount(() => document.Sheets.Count)
        };

        OfficePackageCore.Apply(metadata, () => document.OpenXmlDocument?.CoreFilePropertiesPart);
        OfficePackageExtended.Apply(metadata, () => document.OpenXmlDocument?.ExtendedFilePropertiesPart);
        return metadata;
    }

    private static bool IsTemplateKind(ExcelDocument document) =>
        OfficeGuard.Try(() => document.SourceFormatDescriptor) is { DocumentKind: OfficeDocumentKind.Template };
}
