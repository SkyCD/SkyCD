using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using SkyCD.Plugin.Metadata.VideoProjects.Exceptions;
using SkyCD.Plugin.Metadata.VideoProjects.Models;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

/// <summary>
/// Picks the reader from the bytes themselves — gzip Premiere XML, plain XML (Premiere or After Effects),
/// RIFX <c>Egg!</c>, the Sonic Foundry RIFF header of VEGAS Pro, or a ZIP that holds a Resolve project — so a
/// renamed or unrelated file yields no facts instead of a guess based on its extension.
/// </summary>
internal static class VideoProjectReader
{
    private const int PrefixLength = 40;

    public static async Task<VideoProjectFacts?> ReadAsync(
        MediaItem item,
        IMediaSource source,
        VideoProjectPluginOptions options,
        CancellationToken cancellationToken)
    {
        var prefix = await ReadPrefixAsync(source, cancellationToken).ConfigureAwait(false);
        if (prefix.Length < 4)
        {
            return null;
        }

        try
        {
            if (IsRifxEgg(prefix))
            {
                using var stream = new MediaSourceStream(source, cancellationToken);
                return AfterEffectsBinaryReader.Read(stream, options, cancellationToken);
            }

            if (VegasProjectReader.MatchesSignature(prefix))
            {
                using var stream = new MediaSourceStream(source, cancellationToken);
                return VegasProjectReader.Read(stream);
            }

            if (IsZip(prefix))
            {
                return ResolveProjectReader.Read(source, item.Extension, options, cancellationToken);
            }

            if (IsGzip(prefix))
            {
                using var stream = new MediaSourceStream(source, cancellationToken);
                using var gzip = new GZipStream(stream, CompressionMode.Decompress);
                using var bounded = new BoundedReadStream(
                    gzip, options.MaxProjectXmlBytes, "decompressed project size");
                return ReadXml(bounded, options, cancellationToken);
            }

            if (IsXml(prefix))
            {
                using var stream = new MediaSourceStream(source, cancellationToken);
                using var bounded = new BoundedReadStream(
                    stream, options.MaxProjectXmlBytes, "project document size");
                return ReadXml(bounded, options, cancellationToken);
            }
        }
        catch (InvalidDataException exception)
        {
            throw new VideoProjectDecodeException("the compressed project data is damaged", exception);
        }
        catch (XmlException exception)
        {
            throw new VideoProjectDecodeException("the project document is not well-formed XML", exception);
        }

        return null;
    }

    private static VideoProjectFacts? ReadXml(
        Stream xml, VideoProjectPluginOptions options, CancellationToken cancellationToken)
    {
        using var reader = ProjectXml.Create(xml, options.MaxProjectXmlBytes);
        if (reader.MoveToContent() != XmlNodeType.Element)
        {
            return null;
        }

        return reader.LocalName switch
        {
            "PremiereData" => PremiereProjectReader.Read(reader, options, cancellationToken),
            "AfterEffectsProject" => AfterEffectsXmlReader.Read(reader, options, cancellationToken),
            _ => null
        };
    }

    private static async Task<byte[]> ReadPrefixAsync(IMediaSource source, CancellationToken cancellationToken)
    {
        var length = source.Length;
        if (length <= 0)
        {
            return [];
        }

        var count = (int)Math.Min(PrefixLength, length);
        var memory = await source.ReadAsync(0, count, cancellationToken).ConfigureAwait(false);
        return memory.Length >= count ? memory.Span[..count].ToArray() : memory.ToArray();
    }

    private static bool IsRifxEgg(ReadOnlySpan<byte> prefix) =>
        prefix.Length >= 12 &&
        prefix[..4].SequenceEqual("RIFX"u8) &&
        prefix.Slice(8, 4).SequenceEqual("Egg!"u8);

    private static bool IsZip(ReadOnlySpan<byte> prefix) =>
        prefix.Length >= 4 && prefix[0] == (byte)'P' && prefix[1] == (byte)'K' && prefix[2] == 3 && prefix[3] == 4;

    private static bool IsGzip(ReadOnlySpan<byte> prefix) =>
        prefix.Length >= 2 && prefix[0] == 0x1F && prefix[1] == 0x8B;

    private static bool IsXml(ReadOnlySpan<byte> prefix)
    {
        var offset = 0;
        if (prefix.Length >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF)
        {
            offset = 3;
        }

        for (; offset < prefix.Length; offset++)
        {
            switch (prefix[offset])
            {
                case (byte)' ':
                case (byte)'\t':
                case (byte)'\r':
                case (byte)'\n':
                    continue;

                case (byte)'<':
                    return true;

                default:
                    return false;
            }
        }

        return false;
    }
}
