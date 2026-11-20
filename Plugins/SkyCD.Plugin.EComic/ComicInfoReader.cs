using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace SkyCD.Plugin.EComic;

/// <summary>
/// Reads the few ComicInfo elements the plugin publishes. Parsing is strict about the document shape — DTDs
/// are prohibited, no resolver is installed and the character count is bounded — and tolerant about content:
/// malformed XML, unexpected elements or unparsable numbers degrade to "no value" instead of failing the
/// extraction.
/// </summary>
internal static class ComicInfoReader
{
    private const long MaxCharactersInDocument = 8_000_000;

    public static ComicInfo Parse(byte[] document, int maxFieldCharacters)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFieldCharacters);

        try
        {
            using var stream = new MemoryStream(document, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
                MaxCharactersInDocument = MaxCharactersInDocument,
                CloseInput = false
            });

            var root = XDocument.Load(reader).Root;
            if (root is null)
            {
                return ComicInfo.Empty;
            }

            string? Value(string element) => Normalize(
                root.Descendants()
                    .FirstOrDefault(candidate => candidate.Name.LocalName.Equals(element, StringComparison.OrdinalIgnoreCase))
                    ?.Value,
                maxFieldCharacters);

            return new ComicInfo(
                Series: Value("Series"),
                Title: Value("Title"),
                Number: Value("Number"),
                Volume: Value("Volume"),
                Writer: Value("Writer"),
                Publisher: Value("Publisher"),
                Genre: Value("Genre"),
                Year: ParsePositiveInteger(Value("Year")),
                PageCount: ParsePositiveInteger(Value("PageCount")),
                LanguageIso: Value("LanguageISO"),
                Summary: Value("Summary"));
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException
                                              or System.IO.IOException)
        {
            return ComicInfo.Empty;
        }
    }

    private static string? Normalize(string? value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxCharacters ? trimmed : trimmed[..maxCharacters].TrimEnd();
    }

    private static int? ParsePositiveInteger(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;
}
