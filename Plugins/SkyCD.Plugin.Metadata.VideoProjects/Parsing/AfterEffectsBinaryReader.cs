using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using SkyCD.Plugin.Metadata.VideoProjects.Exceptions;
using SkyCD.Plugin.Metadata.VideoProjects.Models;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

/// <summary>
/// Walks the big-endian RIFX <c>Egg!</c> chunk tree of a binary After Effects project. Only chunk headers and
/// the few bodies that carry facts are read; every declared size is validated against its parent, and an
/// overrun stops the walk with the facts recovered so far and records that the project was truncated.
/// </summary>
internal static class AfterEffectsBinaryReader
{
    private const int MaxNestingDepth = 16;

    public static VideoProjectFacts? Read(
        Stream stream, VideoProjectPluginOptions options, CancellationToken cancellationToken)
    {
        var length = stream.Length;
        if (length < 16)
        {
            return null;
        }

        var header = new byte[12];
        stream.Position = 0;
        if (!ReadFully(stream, header))
        {
            return null;
        }

        if (!header.AsSpan(0, 4).SequenceEqual("RIFX"u8) ||
            !header.AsSpan(8, 4).SequenceEqual("Egg!"u8))
        {
            return null;
        }

        var declared = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4, 4));
        if (declared < 4)
        {
            return null;
        }

        var truncated = false;
        var containerEnd = 12 + ((long)declared - 4);
        if (containerEnd > length)
        {
            containerEnd = length;
            truncated = true;
        }

        var walker = new Walker(stream, options, cancellationToken) { Truncated = truncated };
        return walker.Walk(containerEnd);
    }

    private sealed class Walker(Stream stream, VideoProjectPluginOptions options, CancellationToken cancellationToken)
    {
        private readonly Stream stream = stream;
        private readonly VideoProjectPluginOptions options = options;
        private readonly CancellationToken cancellationToken = cancellationToken;

        private int chunks;
        private string? applicationVersion;
        private long compositionCount;
        private long footageCount;
        private long folderCount;
        private string? firstCompositionName;
        private byte[]? firstCompositionData;

        public bool Truncated { get; set; }

        public VideoProjectFacts Walk(long containerEnd)
        {
            WalkRange(12, containerEnd);

            string? resolution = null;
            double? frameRate = null;
            double? durationSeconds = null;
            if (firstCompositionData is not null)
            {
                CompositionFacts.Read(firstCompositionData, out resolution, out frameRate, out durationSeconds);
            }

            return new VideoProjectFacts(
                VideoProjectIdentity.KindAfterEffects,
                VideoProjectIdentity.FormatAep,
                VideoProjectIdentity.ApplicationAfterEffects)
            {
                ApplicationVersion = applicationVersion,
                ProjectTitle = firstCompositionName,
                TimelineCount = compositionCount,
                ItemCount = compositionCount + footageCount + folderCount,
                MediaFileCount = footageCount,
                FrameRate = frameRate,
                DurationSeconds = durationSeconds,
                Resolution = resolution,
                Truncated = Truncated
            };
        }

        private void WalkRange(long start, long end)
        {
            var position = start;
            while (position < end)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryReadChunkHeader(position, end, out var id, out var bodyStart, out var bodyEnd))
                {
                    return;
                }

                if (id == "LIST")
                {
                    var listType = ReadPrefix(bodyStart, 4, bodyEnd);
                    if (listType.Length == 4)
                    {
                        var listName = Encoding.ASCII.GetString(listType);
                        if (listName == "Fold")
                        {
                            ScanFolder(bodyStart + 4, bodyEnd, 1);
                        }
                    }
                }
                else if (id == "head")
                {
                    applicationVersion ??= AeVersion.DecodeHead(ReadPrefix(bodyStart, 32, bodyEnd));
                }
                else if (id == "svap")
                {
                    applicationVersion ??= AeVersion.DecodeWord(ReadPrefix(bodyStart, 4, bodyEnd));
                }

                position = Advance(bodyEnd, bodyEnd - bodyStart, end);
            }
        }

        private void ScanFolder(long start, long end, int depth)
        {
            if (depth > MaxNestingDepth)
            {
                throw new VideoProjectLimitExceededException("project nesting depth", MaxNestingDepth);
            }

            var position = start;
            while (position < end)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryReadChunkHeader(position, end, out var id, out var bodyStart, out var bodyEnd))
                {
                    return;
                }

                if (id == "LIST")
                {
                    var listType = ReadPrefix(bodyStart, 4, bodyEnd);
                    if (listType.Length == 4)
                    {
                        switch (Encoding.ASCII.GetString(listType))
                        {
                            case "Item":
                                ScanItem(bodyStart + 4, bodyEnd, depth + 1);
                                break;
                            case "Sfdr":
                                ScanFolder(bodyStart + 4, bodyEnd, depth + 1);
                                break;
                        }
                    }
                }

                position = Advance(bodyEnd, bodyEnd - bodyStart, end);
            }
        }

        private void ScanItem(long start, long end, int depth)
        {
            if (depth > MaxNestingDepth)
            {
                throw new VideoProjectLimitExceededException("project nesting depth", MaxNestingDepth);
            }

            var itemType = 0;
            string? name = null;
            byte[]? compositionData = null;
            var position = start;
            while (position < end)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryReadChunkHeader(position, end, out var id, out var bodyStart, out var bodyEnd))
                {
                    return;
                }

                switch (id)
                {
                    case "idta":
                        var identity = ReadPrefix(bodyStart, 64, bodyEnd);
                        if (identity.Length >= 2)
                        {
                            itemType = BinaryPrimitives.ReadUInt16BigEndian(identity);
                        }
                        else if (identity.Length >= 1)
                        {
                            itemType = identity[0];
                        }

                        break;

                    case "cdta":
                        compositionData ??= ReadPrefix(bodyStart, 204, bodyEnd);
                        break;

                    case "Utf8":
                        name ??= Encoding.UTF8.GetString(ReadPrefix(bodyStart, options.MaxNameCharacters * 4, bodyEnd));
                        break;

                    case "LIST":
                        var listType = ReadPrefix(bodyStart, 4, bodyEnd);
                        if (listType.Length == 4 && Encoding.ASCII.GetString(listType) == "Sfdr")
                        {
                            ScanFolder(bodyStart + 4, bodyEnd, depth + 1);
                        }

                        break;
                }

                position = Advance(bodyEnd, bodyEnd - bodyStart, end);
            }

            switch (itemType)
            {
                case 1:
                    folderCount++;
                    break;

                case 4:
                    compositionCount++;
                    if (firstCompositionData is null)
                    {
                        firstCompositionName = ProjectText.Clean(name, options.MaxNameCharacters);
                        firstCompositionData = compositionData;
                    }

                    break;

                case 7:
                    footageCount++;
                    break;
            }
        }

        private bool TryReadChunkHeader(
            long position, long end, out string id, out long bodyStart, out long bodyEnd)
        {
            id = string.Empty;
            bodyStart = 0;
            bodyEnd = 0;
            if (end - position < 8)
            {
                return false;
            }

            if (NextChunk() > options.MaxChunks)
            {
                throw new VideoProjectLimitExceededException("chunk count", options.MaxChunks);
            }

            var header = new byte[8];
            stream.Position = position;
            if (!ReadFully(stream, header))
            {
                Truncated = true;
                return false;
            }

            id = Encoding.ASCII.GetString(header, 0, 4);
            var size = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4, 4));
            bodyStart = position + 8;
            bodyEnd = bodyStart + size;
            if (bodyEnd < bodyStart || bodyEnd > end)
            {
                Truncated = true;
                return false;
            }

            return true;
        }

        private byte[] ReadPrefix(long start, int count, long end)
        {
            count = (int)Math.Min(count, end - start);
            if (count <= 0)
            {
                return [];
            }

            var buffer = new byte[count];
            stream.Position = start;
            var read = 0;
            while (read < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = stream.Read(buffer, read, count - read);
                if (current == 0)
                {
                    Truncated = true;
                    break;
                }

                read += current;
            }

            return read == count ? buffer : buffer.AsSpan(0, read).ToArray();
        }

        private int NextChunk() => ++chunks;

        private static long Advance(long bodyEnd, long size, long end)
        {
            var next = bodyEnd + (size & 1);
            return next > end ? end : next;
        }
    }

    private static bool ReadFully(Stream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var current = stream.Read(buffer[read..]);
            if (current == 0)
            {
                return false;
            }

            read += current;
        }

        return true;
    }
}
