using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using SkyCD.Plugin.Metadata.Sequencer.Exceptions;
using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class ProjectXml
{
    private const int MiB = 1024 * 1024;

    internal static XElement Read(byte[] bytes, CancellationToken token, bool allowGzip = false,
        bool allowQtZlib = false, int limitMiB = 32)
    {
        using var input = new MemoryStream(bytes);
        var limit = limitMiB * MiB;
        using var compressed = OpenCompressedProject(input, bytes, allowGzip, allowQtZlib, limit);
        return Read(compressed ?? input, limit, token);
    }

    internal static SequencerFacts? CreateFacts(XElement root, string signature, string format, string application)
    {
        if (root.Name.LocalName != signature)
            return null;
        var facts = new SequencerFacts("Project", format);
        facts.Text("Application", application);
        facts.Text("ApplicationVersion", Attribute(root, "Creator", "creatorversion", "applicationVersion", "appVersion", "version")
            ?? (string?)root.Element("version"));
        return facts;
    }

    private static Stream? OpenCompressedProject(MemoryStream input, ReadOnlySpan<byte> bytes, bool allowGzip, bool allowQtZlib, int limit)
    {
        if (allowGzip && bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
            return new GZipStream(input, CompressionMode.Decompress);

        // Qt qCompress prefixes the zlib stream with a big-endian uncompressed length.
        if (allowQtZlib && bytes.Length >= 6 && (bytes[4] & 0x0f) == 8)
        {
            if (BinaryPrimitives.ReadUInt32BigEndian(bytes) > limit)
                throw new SequencerDocumentException();
            input.Position = 4;
            return new ZLibStream(input, CompressionMode.Decompress);
        }

        return null;
    }

    internal static XElement Read(Stream source, int limit, CancellationToken token)
    {
        using var bounded = new MemoryStream();
        var buffer = new byte[32768];
        int count;
        while ((count = source.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested();
            if (bounded.Length + count > limit)
                throw new SequencerDocumentException();
            bounded.Write(buffer, 0, count);
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = limit
        };
        bounded.Position = 0;
        using (var check = XmlReader.Create(bounded, settings))
        {
            var nodes = 0;
            while (check.Read())
            {
                token.ThrowIfCancellationRequested();
                if (check.Depth > 64 || ++nodes > 500_000)
                    throw new SequencerDocumentException();
            }
        }

        bounded.Position = 0;
        using var reader = XmlReader.Create(bounded, settings);
        return XElement.Load(reader);
    }

    internal static void SetTempo(SequencerFacts facts, string? text, double scale = 1)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var tempo) && tempo > 0)
            facts.Number("Tempo", tempo * scale);
    }

    internal static string? Attribute(XElement? element, params string[] keys) =>
        keys.Select(key => (string?)element?.Attribute(key)).FirstOrDefault(value => value is not null);
}
