using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.OpenOffice.Models;

/// <summary>
/// Maps a package's declared MIME type, or its filename when no type is declared, to the container family and
/// document type published through the schema.
/// </summary>
internal static class OpenOfficeDocumentTypes
{
    public const string OdfContainer = "odf";
    public const string LegacyContainer = "openoffice";

    private const string OdfContentTypePrefix = "application/vnd.oasis.opendocument.";
    private const string LegacyContentTypePrefix = "application/vnd.sun.xml.";

    private static readonly IReadOnlyDictionary<string, string> DocumentTypesByContentType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/vnd.oasis.opendocument.text"] = "text",
            ["application/vnd.oasis.opendocument.text-template"] = "text-template",
            ["application/vnd.oasis.opendocument.text-master"] = "text-master",
            ["application/vnd.oasis.opendocument.text-master-template"] = "text-master-template",
            ["application/vnd.oasis.opendocument.text-web"] = "text-web",
            ["application/vnd.oasis.opendocument.spreadsheet"] = "spreadsheet",
            ["application/vnd.oasis.opendocument.spreadsheet-template"] = "spreadsheet-template",
            ["application/vnd.oasis.opendocument.presentation"] = "presentation",
            ["application/vnd.oasis.opendocument.presentation-template"] = "presentation-template",
            ["application/vnd.oasis.opendocument.graphics"] = "graphics",
            ["application/vnd.oasis.opendocument.graphics-template"] = "graphics-template",
            ["application/vnd.oasis.opendocument.formula"] = "formula",
            ["application/vnd.oasis.opendocument.formula-template"] = "formula-template",
            ["application/vnd.oasis.opendocument.chart"] = "chart",
            ["application/vnd.oasis.opendocument.chart-template"] = "chart-template",
            ["application/vnd.oasis.opendocument.image"] = "image",
            ["application/vnd.oasis.opendocument.image-template"] = "image-template",
            ["application/vnd.oasis.opendocument.database"] = "database",
            ["application/vnd.sun.xml.writer"] = "text",
            ["application/vnd.sun.xml.writer.template"] = "text-template",
            ["application/vnd.sun.xml.writer.global"] = "text-master",
            ["application/vnd.sun.xml.calc"] = "spreadsheet",
            ["application/vnd.sun.xml.calc.template"] = "spreadsheet-template",
            ["application/vnd.sun.xml.impress"] = "presentation",
            ["application/vnd.sun.xml.impress.template"] = "presentation-template",
            ["application/vnd.sun.xml.draw"] = "graphics",
            ["application/vnd.sun.xml.draw.template"] = "graphics-template",
            ["application/vnd.sun.xml.math"] = "formula"
        };

    private static readonly IReadOnlyDictionary<string, string> DocumentTypesByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".odt"] = "text",
            [".ott"] = "text-template",
            [".odm"] = "text-master",
            [".otm"] = "text-master-template",
            [".oth"] = "text-web",
            [".ods"] = "spreadsheet",
            [".ots"] = "spreadsheet-template",
            [".odp"] = "presentation",
            [".otp"] = "presentation-template",
            [".odg"] = "graphics",
            [".otg"] = "graphics-template",
            [".odf"] = "formula",
            [".odc"] = "chart",
            [".odi"] = "image",
            [".oti"] = "image-template",
            [".odb"] = "database",
            [".sxw"] = "text",
            [".stw"] = "text-template",
            [".sxg"] = "text-master",
            [".sxc"] = "spreadsheet",
            [".stc"] = "spreadsheet-template",
            [".sxi"] = "presentation",
            [".sti"] = "presentation-template",
            [".sxd"] = "graphics",
            [".std"] = "graphics-template",
            [".sxm"] = "formula"
        };

    private static readonly IReadOnlySet<string> OdfExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".odt", ".ott", ".odm", ".otm", ".oth", ".ods", ".ots", ".odp", ".otp", ".odg", ".otg",
            ".odf", ".odc", ".odi", ".oti", ".odb"
        };

    private static readonly IReadOnlySet<string> LegacyExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".sxw", ".stw", ".sxg", ".sxc", ".stc", ".sxi", ".sti", ".sxd", ".std", ".sxm"
        };

    public static string? ResolveContainer(string? contentType, string? extension)
    {
        var type = NormalizeContentType(contentType);
        if (type is not null)
        {
            if (type.StartsWith(OdfContentTypePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return OdfContainer;
            }

            if (type.StartsWith(LegacyContentTypePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return LegacyContainer;
            }
        }

        var suffix = NormalizeExtension(extension);
        if (suffix is null)
        {
            return null;
        }

        if (OdfExtensions.Contains(suffix))
        {
            return OdfContainer;
        }

        return LegacyExtensions.Contains(suffix) ? LegacyContainer : null;
    }

    public static string? ResolveDocumentType(string? contentType, string? extension)
    {
        var type = NormalizeContentType(contentType);
        if (type is not null && DocumentTypesByContentType.TryGetValue(type, out var byType))
        {
            return byType;
        }

        var suffix = NormalizeExtension(extension);
        return suffix is not null && DocumentTypesByExtension.TryGetValue(suffix, out var byExtension)
            ? byExtension
            : null;
    }

    private static string? NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var value = contentType.Trim();
        var parameters = value.IndexOf(';', StringComparison.Ordinal);
        return parameters >= 0 ? value[..parameters].TrimEnd() : value;
    }

    private static string? NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var value = extension.Trim();
        return value.StartsWith('.') ? value : "." + value;
    }
}
