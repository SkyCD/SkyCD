using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace SkyCD.Plugin.Metadata.EBook.Parsing;

/// <summary>
/// Loads an e-book XML part without resolving entities or external resources. E-books are untrusted input,
/// and an EPUB package or FB2 document that references a DTD must be rejected rather than expanded, so the
/// reader setting is part of the format contract rather than a per-call choice.
/// </summary>
internal static class EBookXml
{
    public static XDocument? TryLoad(byte[] data, long maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            return null;
        }

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
                CloseInput = true,
                MaxCharactersInDocument = maxCharacters
            };

            using var stream = new MemoryStream(data, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
