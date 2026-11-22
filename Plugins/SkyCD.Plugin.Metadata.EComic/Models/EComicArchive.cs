using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.EComic.Exceptions;
using SkyCD.Plugin.Metadata.EComic.Parsing;

namespace SkyCD.Plugin.Metadata.EComic.Models;

/// <summary>
/// One opened comic container: the archive flavor, the ComicInfo document and the first image page.
/// </summary>
/// <remarks>
/// The archive is read through <see cref="MediaSourceStream"/>, so a large comic is never buffered whole and
/// every read is charged to the host's budget. Only entry headers and the two small payloads the plugin needs
/// are decompressed. Entry size and visit count are bounded before decompression, and nothing is written to
/// disk, so archive paths cannot escape anywhere.
/// </remarks>
internal sealed class EComicArchive : IDisposable
{
    private const string ComicInfoFileName = "ComicInfo.xml";

    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".avif", ".tif", ".tiff"];

    private readonly IArchive archive;
    private readonly MediaSourceStream stream;
    private readonly List<UsableEntry> entries;

    private EComicArchive(IArchive archive, MediaSourceStream stream, string container, List<UsableEntry> entries)
    {
        this.archive = archive;
        this.stream = stream;
        Container = container;
        this.entries = entries;
    }

    /// <summary>Detected flavor of the opened container: <c>zip</c>, <c>rar</c>, <c>7z</c> and so on.</summary>
    public string Container { get; }

    public static EComicArchive Open(IMediaSource source, EComicPluginOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (source.Length <= 0)
        {
            throw new EComicArchiveException("the source is empty");
        }

        var stream = new MediaSourceStream(source, cancellationToken);
        IArchive? archive = null;
        try
        {
            archive = ArchiveFactory.OpenArchive(stream, new ReaderOptions { LeaveStreamOpen = true });
            var entries = ListEntries(archive, options, cancellationToken);
            return new EComicArchive(archive, stream, ContainerName(archive.Type), entries);
        }
        catch (Exception exception) when (IsFormatFailure(exception))
        {
            archive?.Dispose();
            stream.Dispose();
            throw new EComicArchiveException("the container is not a readable comic archive", exception);
        }
    }

    /// <summary>Reads <c>ComicInfo.xml</c>; a missing, oversized or malformed document reads as empty metadata.</summary>
    public async Task<ComicInfo> ReadComicInfoAsync(EComicPluginOptions options, CancellationToken cancellationToken)
    {
        var candidate = entries
            .Where(entry => string.Equals(FileName(entry.Path), ComicInfoFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Path.Count(character => character == '/'))
            .ThenBy(entry => entry.Path, StringComparer.Ordinal)
            .FirstOrDefault();
        if (candidate is null)
        {
            return ComicInfo.Empty;
        }

        var document = await ReadEntryAsync(candidate.Entry, options.MaxComicInfoBytes, cancellationToken)
            .ConfigureAwait(false);
        return document.Length == 0 ? ComicInfo.Empty : ComicInfoReader.Parse(document, options.MaxFieldCharacters);
    }

    /// <summary>
    /// Reads the first page in natural page order. Returns the encoded image bytes, or <c>null</c> when the
    /// archive holds no readable page within the size bound.
    /// </summary>
    public async Task<byte[]?> ReadFirstPageAsync(EComicPluginOptions options, CancellationToken cancellationToken)
    {
        var pages = entries
            .Where(entry => ImageExtensions.Contains(Path.GetExtension(entry.Path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Path, NaturalPathComparer.Instance);
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = await ReadEntryAsync(page.Entry, options.MaxPageBytes, cancellationToken).ConfigureAwait(false);
            if (data.Length > 0)
            {
                return data;
            }
        }

        return null;
    }

    public void Dispose()
    {
        archive.Dispose();
        stream.Dispose();
    }

    private static List<UsableEntry> ListEntries(IArchive archive, EComicPluginOptions options, CancellationToken cancellationToken)
    {
        var usable = new List<UsableEntry>();
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (usable.Count >= options.MaxArchiveEntries)
            {
                break;
            }

            if (entry.IsDirectory || entry.IsEncrypted)
            {
                continue;
            }

            var path = entry.Key?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(path) ||
                path.StartsWith("__MACOSX/", StringComparison.Ordinal) ||
                FileName(path).StartsWith("._", StringComparison.Ordinal))
            {
                continue;
            }

            usable.Add(new UsableEntry(entry, path));
        }

        return usable;
    }

    private static async Task<byte[]> ReadEntryAsync(
        IArchiveEntry entry,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (entry.Size == 0 || entry.Size > maxBytes)
        {
            return [];
        }

        try
        {
            await using var entryStream = await entry.OpenEntryStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream((int)Math.Min(entry.Size, maxBytes));
            var chunk = new byte[81920];
            long total = 0;
            int read;
            while ((read = await entryStream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    return [];
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (Exception exception) when (IsFormatFailure(exception))
        {
            // A damaged entry only loses its own value; the archive still reports what did decode.
            return [];
        }
    }

    private static string FileName(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? path : path[(separator + 1)..];
    }

    private static string ContainerName(ArchiveType type) => type switch
    {
        ArchiveType.SevenZip => "7z",
        ArchiveType.Rar => "rar",
        ArchiveType.Zip => "zip",
        _ => type.ToString().ToLowerInvariant()
    };

    private static bool IsFormatFailure(Exception exception) =>
        exception is SharpCompressException or InvalidFormatException or EndOfStreamException &&
        !IsCancellation(exception);

    private static bool IsCancellation(Exception exception) =>
        exception is OperationCanceledException ||
        exception.InnerException is not null && IsCancellation(exception.InnerException);

    private sealed record UsableEntry(IArchiveEntry Entry, string Path);

    /// <summary>
    /// Orders page names the way a reading application lists pages: digit runs compare by value, so page 2
    /// sorts before page 10, and case differences never reorder otherwise equal names.
    /// </summary>
    private sealed class NaturalPathComparer : IComparer<string>
    {
        public static NaturalPathComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            int left = 0, right = 0;
            while (left < x.Length && right < y.Length)
            {
                var leftDigit = char.IsAsciiDigit(x[left]);
                var rightDigit = char.IsAsciiDigit(y[right]);
                if (leftDigit && rightDigit)
                {
                    var leftStart = left;
                    var rightStart = right;
                    while (left < x.Length && char.IsAsciiDigit(x[left])) left++;
                    while (right < y.Length && char.IsAsciiDigit(y[right])) right++;
                    var leftNumber = x.AsSpan(leftStart, left - leftStart).TrimStart('0');
                    var rightNumber = y.AsSpan(rightStart, right - rightStart).TrimStart('0');
                    if (leftNumber.Length != rightNumber.Length)
                    {
                        return leftNumber.Length.CompareTo(rightNumber.Length);
                    }

                    var comparison = leftNumber.SequenceCompareTo(rightNumber);
                    if (comparison != 0)
                    {
                        return comparison;
                    }

                    continue;
                }

                var leftChar = char.ToLowerInvariant(x[left]);
                var rightChar = char.ToLowerInvariant(y[right]);
                if (leftChar != rightChar)
                {
                    return leftChar.CompareTo(rightChar);
                }

                left++;
                right++;
            }

            return (x.Length - left).CompareTo(y.Length - right);
        }
    }
}
