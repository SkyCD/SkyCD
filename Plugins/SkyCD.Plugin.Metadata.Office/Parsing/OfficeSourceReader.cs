using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Office.Exceptions;
using SkyCD.Plugin.Metadata.Office.Models;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Reads a bounded source into memory and resolves the metadata it carries.
/// </summary>
/// <remarks>
/// <para>
/// Reading in one pass keeps the host's budget accounting simple: the charge equals the source size, once. The
/// plugin only ever holds bytes and never a path, so a document that references another file has no way to make
/// it open that file.
/// </para>
/// <para>
/// A package is resolved by the application directory its document part lives in and a compound document by the
/// content stream it writes, so the published document type comes from the bytes rather than from the file name.
/// Three families need the extension as well: a OneNote store, a Jet database and a Project plan each share a
/// container with other Office formats and none of them writes a stream this plugin models. A damaged optional
/// metadata part is skipped, while a source that is not an Office document at all raises
/// <see cref="OfficeDecodeException"/>.
/// </para>
/// </remarks>
internal static class OfficeSourceReader
{
    private const int ChunkBytes = 256 * 1024;

    public const string SummaryStream = "\u0005SummaryInformation";
    public const string DocumentSummaryStream = "\u0005DocumentSummaryInformation";
    public const string EncryptionInfoStream = "EncryptionInfo";
    public const string EncryptedPackageStream = "EncryptedPackage";

    public static async Task<OfficeMetadata> ReadAllAsync(
        MediaItem item,
        IMediaSource source,
        OfficePluginOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var data = await ReadSourceAsync(item, source, options, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return ReadAll(item, data, options, cancellationToken);
    }

    public static OfficeMetadata ReadAll(
        MediaItem item,
        byte[] data,
        OfficePluginOptions options,
        CancellationToken cancellationToken)
    {
        var extension = OfficeFamilyResolver.Normalize(item.Extension);
        var hint = OfficeFamilyResolver.FromExtension(extension);

        // These three are decided by the matched extension before the container is inspected, because their
        // container is one another Office format also uses.
        switch (hint)
        {
            case OfficeFamily.Note:
                return OfficeOneNoteReader.Read(extension, data, options, cancellationToken);
            case OfficeFamily.Database when OfficeContainers.IsJet(data):
                return OfficeAccessReader.Read(data, options, cancellationToken);
            case OfficeFamily.Plan:
                // The same family stores a plan as a compound document and as the older delimited interchange text,
                // and the extension does not say which, so the leading bytes decide.
                return OfficeProjectReader.Read(
                    CompoundFile.HasSignature(data) ? OfficeContainers.Ole : OfficeContainers.Text,
                    data,
                    options,
                    extension,
                    cancellationToken);
        }

        if (OfficeContainers.IsPackage(data))
        {
            return ReadPackage(data, options, extension, cancellationToken);
        }

        if (CompoundFile.HasSignature(data))
        {
            return ReadCompound(data, options, hint, extension);
        }

        throw new OfficeDecodeException(
            "the source is neither an OOXML package, an Office compound document nor a OneNote, Access or Project file");
    }

    /// <summary>True when the compound file wraps an encrypted OOXML package rather than a legacy document.</summary>
    public static bool IsEncryptedWrapper(CompoundFile compound) =>
        compound.ContainsStream(EncryptionInfoStream) && compound.ContainsStream(EncryptedPackageStream);

    private static OfficeMetadata ReadPackage(
        byte[] data,
        OfficePluginOptions options,
        string? extension,
        CancellationToken cancellationToken)
    {
        var family = SniffPackageFamily(data)
            ?? throw new OfficeDecodeException("the package holds no Word, Excel, PowerPoint or Visio document part");

        return family switch
        {
            OfficeFamily.Word => OfficeWordReader.Read(data, options, extension, cancellationToken),
            OfficeFamily.Workbook => OfficeWorkbookReader.Read(data, options, extension, cancellationToken),
            OfficeFamily.Presentation => OfficePowerPointReader.Read(data, options, extension, cancellationToken),
            _ => OfficeVisioReader.Read(data, options, extension, cancellationToken)
        };
    }

    private static OfficeMetadata ReadCompound(
        byte[] data,
        OfficePluginOptions options,
        OfficeFamily? hint,
        string? extension)
    {
        var compound = CompoundFile.Open(data);

        // Word, Excel and PowerPoint identify themselves by the stream they write, so a matching extension is
        // never allowed to stand in for a document stream that is not there.
        var legacyHint = hint is OfficeFamily.Drawing ? hint : null;
        return OfficeProtection.Read(OfficeContainers.Ole,
            () => OfficeLegacyReader.Read(compound, options, legacyHint, extension));
    }

    /// <summary>The application directory the package stores its document part in, which names the application.</summary>
    private static OfficeFamily? SniffPackageFamily(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in archive.Entries)
            {
                if (OfficeFamilyResolver.FromPackagePart(entry.FullName) is { } family)
                {
                    return family;
                }
            }

            return null;
        }
        catch (InvalidDataException exception)
        {
            throw new OfficeDecodeException("the package structure could not be read", exception);
        }
    }

    /// <summary>Reads the whole source into memory, charging the host budget exactly once.</summary>
    public static async Task<byte[]> ReadSourceAsync(
        MediaItem item,
        IMediaSource source,
        OfficePluginOptions options,
        CancellationToken cancellationToken)
    {
        var length = source.Length > 0 ? source.Length : item.Length;
        if (length <= 0)
        {
            throw new OfficeDecodeException("the source is empty");
        }

        if (length > options.MaxSourceBytes || length > int.MaxValue)
        {
            throw new OfficeSourceTooLargeException(length, options.MaxSourceBytes);
        }

        var buffer = new byte[length];
        var offset = 0L;
        while (offset < length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = (int)Math.Min(ChunkBytes, length - offset);
            var chunk = await source.ReadAsync(offset, count, cancellationToken).ConfigureAwait(false);
            if (chunk.Length != count)
            {
                throw new OfficeDecodeException($"the source returned {chunk.Length} of {count} requested bytes");
            }

            chunk.Span.CopyTo(buffer.AsSpan((int)offset, count));
            offset += count;
        }

        return buffer;
    }
}
