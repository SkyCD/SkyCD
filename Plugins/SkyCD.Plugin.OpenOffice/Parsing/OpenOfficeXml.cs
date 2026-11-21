using System.Xml;

namespace SkyCD.Plugin.OpenOffice.Parsing;

/// <summary>
/// Reader settings shared by the metadata and manifest parsers.
/// </summary>
/// <remarks>
/// External resources are never resolved: DTD processing is prohibited and the resolver is disabled, so a
/// document cannot pull in a file or a URL. The character bound stops a small, highly compressed entry from
/// expanding without limit while it is parsed.
/// </remarks>
internal static class OpenOfficeXml
{
    public static XmlReaderSettings CreateSettings(int maxCharacters) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        MaxCharactersInDocument = maxCharacters,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true
    };
}
