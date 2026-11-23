using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.EncodingProfiles;
using SkyCD.Plugin.Metadata.EBook.Exceptions;
using SkyCD.Plugin.Metadata.EBook.Models;

namespace SkyCD.Plugin.Metadata.EBook.Parsing;

/// <summary>
/// Reads metadata from a Mobipocket/Amazon book — <c>.mobi</c>, <c>.prc</c>, <c>.azw</c>, <c>.azw3</c> — or a
/// plain PalmDOC (Palm database) book.
/// </summary>
/// <remarks>
/// The book text is never decompressed, because everything the plugin publishes lives in record 0 (the book
/// header with its EXTH records) and in the raw image record the cover points at. Only those records are
/// read, each with a size bound, and the record table is validated before any record is touched. An encrypted
/// book still publishes whatever metadata is readable and reports itself as protected instead of failing.
/// </remarks>
internal static class MobipocketReader
{
    private const int PdbHeaderBytes = 78;
    private const int PalmDocHeaderBytes = 16;
    private const int MobiHeaderPosition = PalmDocHeaderBytes;
    private const uint ExthFlag = 0x40;
    private const int MaxExthRecords = 1024;

    private const uint ExthAuthor = 100;
    private const uint ExthPublisher = 101;
    private const uint ExthDescription = 103;
    private const uint ExthIsbn = 104;
    private const uint ExthSubject = 105;
    private const uint ExthPublishedDate = 106;
    private const uint ExthAsin = 113;
    private const uint ExthKf8Boundary = 121;
    private const uint ExthCoverOffset = 201;
    private const uint ExthThumbnailOffset = 202;
    private const uint ExthFakeCover = 203;
    private const uint ExthUpdatedTitle = 503;
    private const uint ExthLanguage = 524;

    public static async Task<EBookDocument> ReadAsync(
        IMediaSource source,
        EBookPluginOptions options,
        bool readCover,
        CancellationToken cancellationToken)
    {
        if (source.Length < PdbHeaderBytes + 8)
        {
            throw new EBookDecodeException("the Palm database is truncated");
        }

        var header = await ReadRangeAsync(source, 0, PdbHeaderBytes, cancellationToken).ConfigureAwait(false);
        var databaseName = EBookText.Normalize(Encoding.Latin1.GetString(header, 0, 32)) ?? string.Empty;
        var type = Encoding.ASCII.GetString(header, 60, 4);
        var creator = Encoding.ASCII.GetString(header, 64, 4);
        var isMobi = string.Equals(type, "BOOK", StringComparison.Ordinal)
                     && string.Equals(creator, "MOBI", StringComparison.Ordinal);
        var isPalmDoc = string.Equals(type, "TEXt", StringComparison.Ordinal)
                        && string.Equals(creator, "REAd", StringComparison.Ordinal);
        if (!isMobi && !isPalmDoc)
        {
            throw new EBookDecodeException("the source is not a Mobipocket or PalmDOC book");
        }

        var recordCount = ReadU16(header, 76);
        if (recordCount == 0 || PdbHeaderBytes + ((long)recordCount * 8) + 2 > source.Length)
        {
            throw new EBookDecodeException("the Palm database record table is truncated");
        }

        var table = await ReadRangeAsync(source, PdbHeaderBytes, recordCount * 8, cancellationToken)
            .ConfigureAwait(false);
        var starts = new long[recordCount];
        for (var index = 0; index < recordCount; index++)
        {
            starts[index] = ReadU32(table, index * 8);
            if (starts[index] >= source.Length || (index > 0 && starts[index] <= starts[index - 1]))
            {
                throw new EBookDecodeException("the Palm database record table is damaged");
            }
        }

        var recordZeroEnd = recordCount > 1 ? starts[1] : source.Length;
        var recordZero = await ReadRecordAsync(
                source, starts[0], recordZeroEnd, options.MaxMetadataPartBytes, cancellationToken)
            .ConfigureAwait(false);
        if (recordZero.Length < PalmDocHeaderBytes)
        {
            throw new EBookDecodeException("the book header record is truncated");
        }

        var encryptionType = ReadU16(recordZero, 12);
        var textRecordCount = ReadU16(recordZero, 8);
        if (!isMobi || !TryParseMobiHeader(recordZero, out var mobi))
        {
            // A PalmDOC book keeps its title in the database name and carries no other metadata.
            return new EBookDocument
            {
                Container = "palmdb",
                Format = "palmdoc",
                Title = databaseName,
                IsProtected = encryptionType != 0
            };
        }

        var metadata = ParseMetadata(mobi, options.MaxFieldCharacters);
        var cover = readCover && !metadata.HasFakeCover
            ? await ReadCoverAsync(source, starts, recordCount, mobi, metadata, textRecordCount, options, cancellationToken)
                .ConfigureAwait(false)
            : null;

        return new EBookDocument
        {
            Container = "palmdb",
            Format = metadata.IsKf8 || mobi.FileVersion >= 8 ? "kf8" : "mobi",
            Title = metadata.Title ?? mobi.FullName ?? databaseName,
            Authors = metadata.Authors,
            Publisher = metadata.Publisher,
            Language = metadata.Language,
            Subject = metadata.Subject,
            Description = metadata.Description,
            Identifier = metadata.Identifier,
            PublishedAt = metadata.PublishedAt,
            PublishedYear = metadata.PublishedAt?.Year,
            IsProtected = encryptionType != 0,
            Cover = cover
        };
    }

    private static bool TryParseMobiHeader(byte[] record, out MobiHeader header)
    {
        header = default;
        if (record.Length < MobiHeaderPosition + 0x18
            || !string.Equals(Encoding.ASCII.GetString(record, MobiHeaderPosition, 4), "MOBI",
                StringComparison.Ordinal))
        {
            return false;
        }

        var headerLength = ReadU32(record, MobiHeaderPosition + 4);
        if (headerLength < 0x18)
        {
            return false;
        }

        var encoding = ResolveEncoding(ReadU32(record, MobiHeaderPosition + 12));
        var fileVersion = ReadU32(record, MobiHeaderPosition + 20);

        string? fullName = null;
        if (headerLength >= 0x4C)
        {
            var nameOffset = ReadU32(record, MobiHeaderPosition + 0x44);
            var nameLength = ReadU32(record, MobiHeaderPosition + 0x48);
            if (nameOffset < record.Length && nameLength <= (uint)(record.Length - nameOffset) && nameLength < 4096)
            {
                fullName = EBookText.Normalize(encoding.GetString(record, (int)nameOffset, (int)nameLength));
            }
        }

        int? firstImageIndex = null;
        if (headerLength >= 0x60)
        {
            var value = ReadU32(record, MobiHeaderPosition + 0x5C);
            if (value < int.MaxValue)
            {
                firstImageIndex = (int)value;
            }
        }

        var exthFlags = headerLength >= 0x74 ? ReadU32(record, MobiHeaderPosition + 0x70) : 0;
        header = new MobiHeader(
            encoding,
            fileVersion,
            fullName,
            firstImageIndex,
            ParseExth(record, MobiHeaderPosition + (int)headerLength, exthFlags));
        return true;
    }

    /// <summary>Parses the EXTH block when the header declares one and the block lies inside record 0.</summary>
    private static Dictionary<uint, List<byte[]>> ParseExth(byte[] record, int position, uint exthFlags)
    {
        var values = new Dictionary<uint, List<byte[]>>();
        if ((exthFlags & ExthFlag) == 0 || position < 0 || position + 12 > record.Length)
        {
            return values;
        }

        if (!string.Equals(Encoding.ASCII.GetString(record, position, 4), "EXTH", StringComparison.Ordinal))
        {
            return values;
        }

        var count = ReadU32(record, position + 8);
        if (count > MaxExthRecords)
        {
            return values;
        }

        var offset = position + 12;
        for (var index = 0u; index < count; index++)
        {
            if (offset + 8 > record.Length)
            {
                break;
            }

            var type = ReadU32(record, offset);
            var length = ReadU32(record, offset + 4);
            if (length < 8 || length > (uint)(record.Length - offset))
            {
                break;
            }

            if (!values.TryGetValue(type, out var list))
            {
                list = [];
                values[type] = list;
            }

            var data = record.AsSpan(offset + 8, (int)length - 8);
            list.Add(data.ToArray());
            offset += (int)length;
        }

        return values;
    }

    private static Metadata ParseMetadata(MobiHeader header, int maxFieldCharacters)
    {
        string? ReadText(uint type) =>
            header.Exth.TryGetValue(type, out var list)
                ? EBookText.Normalize(header.Encoding.GetString(list[0]), maxFieldCharacters)
                : null;

        var authors = new List<string>();
        if (header.Exth.TryGetValue(ExthAuthor, out var authorValues))
        {
            foreach (var value in authorValues)
            {
                var name = EBookText.Normalize(header.Encoding.GetString(value), maxFieldCharacters);
                if (name is not null)
                {
                    authors.Add(name);
                }
            }
        }

        var subjects = new List<string>();
        if (header.Exth.TryGetValue(ExthSubject, out var subjectValues))
        {
            foreach (var value in subjectValues)
            {
                var subject = EBookText.Normalize(header.Encoding.GetString(value), maxFieldCharacters);
                if (subject is not null)
                {
                    subjects.Add(subject);
                }
            }
        }

        return new Metadata
        {
            Title = ReadText(ExthUpdatedTitle),
            Authors = authors,
            Publisher = ReadText(ExthPublisher),
            Language = ReadText(ExthLanguage),
            Subject = EBookText.JoinValues(subjects),
            Description = ReadText(ExthDescription),
            Identifier = ReadText(ExthAsin) ?? ReadText(ExthIsbn),
            PublishedAt = EBookText.ParseTimestamp(ReadText(ExthPublishedDate)),
            IsKf8 = header.Exth.ContainsKey(ExthKf8Boundary),
            HasFakeCover = ReadNumber(header, ExthFakeCover) is not (null or 0),
            CoverOffset = ReadNumber(header, ExthCoverOffset),
            ThumbnailOffset = ReadNumber(header, ExthThumbnailOffset)
        };
    }

    /// <summary>
    /// Reads an EXTH number. Kindle stores these as a big-endian 32-bit integer, while a few producers write
    /// the decimal text instead, so both forms are accepted.
    /// </summary>
    private static int? ReadNumber(MobiHeader header, uint type)
    {
        if (!header.Exth.TryGetValue(type, out var list))
        {
            return null;
        }

        var value = list[0];
        if (value.Length == 4)
        {
            var number = (value[0] << 24) | (value[1] << 16) | (value[2] << 8) | value[3];
            return number >= 0 ? number : null;
        }

        return int.TryParse(EBookText.Normalize(header.Encoding.GetString(value)), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;
    }

    /// <summary>
    /// Reads the image record the EXTH cover offset names. Offsets are relative to the first image record; the
    /// header value and the two interpretations of the PalmDOC record count are all tried, and only a record
    /// that actually contains an image signature is accepted, so a wrong base costs nothing.
    /// </summary>
    private static async Task<byte[]?> ReadCoverAsync(
        IMediaSource source,
        long[] starts,
        int recordCount,
        MobiHeader header,
        Metadata metadata,
        int textRecordCount,
        EBookPluginOptions options,
        CancellationToken cancellationToken)
    {
        foreach (var offset in new[] { metadata.CoverOffset, metadata.ThumbnailOffset })
        {
            if (offset is null)
            {
                continue;
            }

            var tried = new HashSet<int>();
            foreach (var candidate in CandidateRecords(header.FirstImageIndex, textRecordCount, offset.Value, recordCount))
            {
                if (!tried.Add(candidate))
                {
                    continue;
                }

                var end = candidate + 1 < recordCount ? starts[candidate + 1] : source.Length;
                var data = await ReadRecordAsync(source, starts[candidate], end, options.MaxCoverBytes, cancellationToken)
                    .ConfigureAwait(false);
                var imageStart = EBookSignature.FindImageStart(data);
                if (imageStart >= 0)
                {
                    return data[imageStart..];
                }
            }
        }

        return null;
    }

    private static IEnumerable<int> CandidateRecords(
        int? firstImageIndex,
        int textRecordCount,
        int offset,
        int recordCount)
    {
        var bases = new List<int>(3);
        if (firstImageIndex is > 0 && firstImageIndex < recordCount)
        {
            bases.Add(firstImageIndex.Value);
        }

        if (textRecordCount > 0)
        {
            bases.Add(textRecordCount + 1);
            bases.Add(textRecordCount);
        }

        foreach (var item in bases)
        {
            var candidate = item + offset;
            if (candidate >= 0 && candidate < recordCount)
            {
                yield return candidate;
            }
        }
    }

    private static Encoding ResolveEncoding(uint codePage)
    {
        if (codePage == 65001)
        {
            return Encoding.UTF8;
        }

        try
        {
            return LegacyEncoding.Get((int)codePage);
        }
        catch (ArgumentException)
        {
            return LegacyEncoding.Windows1252;
        }
        catch (NotSupportedException)
        {
            return LegacyEncoding.Windows1252;
        }
    }

    private static async Task<byte[]> ReadRecordAsync(
        IMediaSource source,
        long start,
        long end,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var size = end - start;
        if (size <= 0 || size > maxBytes)
        {
            return [];
        }

        return await ReadRangeAsync(source, start, (int)size, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadRangeAsync(
        IMediaSource source,
        long offset,
        int count,
        CancellationToken cancellationToken)
    {
        if (count <= 0 || offset < 0 || offset + count > source.Length)
        {
            throw new EBookDecodeException("the Palm database is truncated");
        }

        var memory = await source.ReadAsync(offset, count, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    private static ushort ReadU16(byte[] data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadU32(byte[] data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private readonly record struct MobiHeader(
        Encoding Encoding,
        uint FileVersion,
        string? FullName,
        int? FirstImageIndex,
        Dictionary<uint, List<byte[]>> Exth);

    private sealed class Metadata
    {
        public string? Title { get; init; }

        public IReadOnlyList<string> Authors { get; init; } = [];

        public string? Publisher { get; init; }

        public string? Language { get; init; }

        public string? Subject { get; init; }

        public string? Description { get; init; }

        public string? Identifier { get; init; }

        public DateTimeOffset? PublishedAt { get; init; }

        public bool IsKf8 { get; init; }

        public bool HasFakeCover { get; init; }

        public int? CoverOffset { get; init; }

        public int? ThumbnailOffset { get; init; }
    }
}
