using System;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Maps a source onto the Office application family it belongs to, from the extension the host matched and
/// from the parts a package stores.
/// </summary>
/// <remarks>
/// The extension is a hint, never an assumption: a package is confirmed by the application directory its
/// document part lives in, so a workbook saved with a Word extension is still published as <c>excel</c>.
/// </remarks>
internal static class OfficeFamilyResolver
{
    public static OfficeFamily? FromExtension(string? extension) => Normalize(extension) switch
    {
        ".docx" or ".docm" or ".dotx" or ".dotm" or ".doc" or ".dot" => OfficeFamily.Word,
        ".xlsx" or ".xlsm" or ".xltx" or ".xltm" or ".xlsb" or ".xls" or ".xlt" => OfficeFamily.Workbook,
        ".pptx" or ".pptm" or ".potx" or ".potm" or ".ppsx" or ".ppsm" or ".ppt" or ".pot" or ".pps"
            => OfficeFamily.Presentation,
        ".vsdx" or ".vsdm" or ".vstx" or ".vstm" or ".vssx" or ".vssm" or ".vsd" or ".vss" or ".vst"
            => OfficeFamily.Drawing,
        ".one" or ".onetoc2" or ".onepkg" => OfficeFamily.Note,
        ".mdb" or ".accdb" => OfficeFamily.Database,
        ".mpp" or ".mpt" or ".mpx" => OfficeFamily.Plan,
        _ => null
    };

    public static string DocumentType(OfficeFamily family) => family switch
    {
        OfficeFamily.Word => "word",
        OfficeFamily.Workbook => "excel",
        OfficeFamily.Presentation => "powerpoint",
        OfficeFamily.Drawing => "visio",
        OfficeFamily.Note => "onenote",
        OfficeFamily.Database => "access",
        _ => "project"
    };

    /// <summary>True for the template, stencil and add-in variants, which carry no content of their own.</summary>
    public static bool IsTemplate(string? extension) => Normalize(extension) switch
    {
        ".dot" or ".dotx" or ".dotm" or ".xlt" or ".xltx" or ".xltm" or ".pot" or ".potx" or ".potm"
            or ".vst" or ".vstx" or ".vstm" or ".vss" or ".vssx" or ".vssm" or ".mpt" => true,
        _ => false
    };

    /// <summary>
    /// The family a package part belongs to, taken from the application directory that owns the document part.
    /// </summary>
    public static OfficeFamily? FromPackagePart(string? entryName)
    {
        if (entryName is null)
        {
            return null;
        }

        var separator = entryName.IndexOf('/');
        if (separator <= 0)
        {
            return null;
        }

        return entryName[..separator].ToLowerInvariant() switch
        {
            "word" => OfficeFamily.Word,
            "xl" => OfficeFamily.Workbook,
            "ppt" => OfficeFamily.Presentation,
            "visio" => OfficeFamily.Drawing,
            _ => null
        };
    }

    /// <summary>The extension in the lower-cased, dotted form the tables above are keyed by.</summary>
    public static string? Normalize(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var value = extension.Trim().ToLowerInvariant();
        return value[0] == '.' ? value : "." + value;
    }
}
