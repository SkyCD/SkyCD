using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Iso9660;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Daa.Exceptions;
using SkyCD.Plugin.Daa.Models;
using SkyCD.Plugin.Daa.Parsing;

namespace SkyCD.Plugin.Daa;

/// <summary>
/// Read-only DAA disk-image plugin. It exposes the ISO filesystem stored in a Direct Access Archive as a
/// catalog hierarchy and, when metadata providers are enabled, streams single contained files to them
/// without extracting the whole image.
/// </summary>
public sealed class DaaImageIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private const string DaaFormatId = "skycd-daa";
    private readonly IDaaEntryReader entryReader;

    public DaaImageIndexPlugin() : this(new DiscUtilsDaaEntryReader())
    {
    }

    public DaaImageIndexPlugin(IDaaEntryReader entryReader)
    {
        ArgumentNullException.ThrowIfNull(entryReader);
        this.entryReader = entryReader;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: DaaFormatId,
            DisplayName: "DAA Disk Image Index",
            Extensions: [".daa"],
            MimeTypes: ["application/x-daa"],
            CanRead: true,
            CanWrite: false);

    public async Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(Path.GetExtension(request.FileName), ".daa", StringComparison.OrdinalIgnoreCase))
        {
            return IndexWorkItemSet.Empty;
        }

        DaaImageStream? image = null;
        try
        {
            image = await OpenForIndexingAsync(request, cancellationToken).ConfigureAwait(false);
            var entries = entryReader.ReadEntries(image);
            var items = new List<IndexWorkItem>();
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsDirectory || !DaaEntryPath.TryNormalize(entry.Path, out var normalized))
                {
                    continue;
                }

                var lookupPath = entry.LookupPath ?? entry.Path;
                items.Add(new IndexWorkItem(
                    normalized,
                    entry.SizeBytes,
                    token => OpenEntryAsync(request, lookupPath, token),
                    ToOffset(entry.ModifiedUtc)));
            }

            return new IndexWorkItemSet(items);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return IndexWorkItemSet.Empty;
        }
        finally
        {
            image?.Dispose();
        }
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "DAA disk image index plugin is read-only."
        });
    }

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            DaaImageStream? image = null;
            try
            {
                image = await OpenForReadAsync(request, cancellationToken).ConfigureAwait(false);
                var entries = entryReader.ReadEntries(image);
                request.Progress?.Report(100);
                return new FileFormatReadResult
                {
                    Success = true,
                    Payload = BuildRows(entries)
                };
            }
            finally
            {
                image?.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new FileFormatReadResult
            {
                Success = false,
                Error = exception.Message
            };
        }
    }

    private static async Task<DaaImageStream> OpenForReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SourcePath) && File.Exists(request.SourcePath))
        {
            return await DaaImageStream.OpenAsync(null, request.SourcePath, ownsSource: false, cancellationToken)
                .ConfigureAwait(false);
        }

        if (request.Source is null || ReferenceEquals(request.Source, Stream.Null))
        {
            throw new DaaDamagedImageException("no source stream is available");
        }

        return await DaaImageStream.OpenAsync(request.Source, null, ownsSource: false, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<DaaImageStream> OpenForIndexingAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SourcePath) && File.Exists(request.SourcePath))
        {
            return await DaaImageStream.OpenAsync(null, request.SourcePath, ownsSource: false, cancellationToken)
                .ConfigureAwait(false);
        }

        var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
        return await DaaImageStream.OpenAsync(source, null, ownsSource: true, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Stream> OpenEntryAsync(
        IndexWorkItemRequest request,
        string lookupPath,
        CancellationToken cancellationToken)
    {
        var image = await OpenForIndexingAsync(request, cancellationToken).ConfigureAwait(false);
        CDReader? reader = null;
        Stream? content = null;
        try
        {
            reader = new CDReader(image, joliet: true, hideVersions: true);
            content = reader.OpenFile(lookupPath, FileMode.Open, FileAccess.Read);
            return new OwnedEntryStream(content, reader, image);
        }
        catch (Exception)
        {
            content?.Dispose();
            reader?.Dispose();
            image.Dispose();
            throw;
        }
    }

    private static List<Dictionary<string, object?>> BuildRows(IReadOnlyCollection<DaaEntryInfo> entries)
    {
        var rows = new List<Dictionary<string, object?>>();
        var seenFolders = new HashSet<string>(StringComparer.Ordinal);
        var seenFiles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (!DaaEntryPath.TryNormalize(entry.Path, out var normalized))
            {
                continue;
            }

            var parts = normalized.Split('/');
            var folderCount = entry.IsDirectory ? parts.Length : parts.Length - 1;
            for (var index = 1; index <= folderCount; index++)
            {
                var folderPath = string.Join('/', parts.Take(index));
                if (!seenFolders.Add(folderPath))
                {
                    continue;
                }

                var modified = entry.IsDirectory && index == parts.Length ? entry.ModifiedUtc : null;
                rows.Add(Row("folder", folderPath, parts[index - 1], 0, modified));
            }

            if (entry.IsDirectory || !seenFiles.Add(normalized))
            {
                continue;
            }

            rows.Add(Row("file", normalized, parts[^1], entry.SizeBytes, entry.ModifiedUtc));
        }

        return rows
            .OrderBy(row => row["fullPath"]?.ToString(), StringComparer.Ordinal)
            .ThenBy(row => row["kind"]?.ToString(), StringComparer.Ordinal)
            .ToList();
    }

    private static Dictionary<string, object?> Row(
        string kind,
        string fullPath,
        string name,
        long sizeBytes,
        DateTime? modifiedUtc) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = kind,
            ["fullPath"] = fullPath,
            ["name"] = name,
            ["sizeBytes"] = sizeBytes.ToString(CultureInfo.InvariantCulture),
            ["modifiedUtc"] = modifiedUtc?.ToString("O", CultureInfo.InvariantCulture)
        };

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value is { } date ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)) : null;

    private sealed class OwnedEntryStream(Stream inner, CDReader reader, DaaImageStream image) : Stream
    {
        private bool disposed;

        public override bool CanRead => !disposed && inner.CanRead;
        public override bool CanSeek => !disposed && inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                inner.Dispose();
                reader.Dispose();
                image.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
