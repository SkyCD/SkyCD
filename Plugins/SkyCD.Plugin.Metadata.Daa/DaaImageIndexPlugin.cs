using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Iso9660;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Daa.Models;
using SkyCD.Plugin.Metadata.Daa.Parsing;

namespace SkyCD.Plugin.Metadata.Daa;

/// <summary>
/// DAA disk-image metadata provider. It exposes the ISO filesystem stored in a Direct Access Archive
/// and streams single contained files to metadata providers without extracting the whole image.
/// </summary>
public sealed class DaaImageIndexPlugin : IIndexWorkItemProvider
{
    private readonly IDaaEntryReader entryReader;

    public DaaImageIndexPlugin() : this(new DiscUtilsDaaEntryReader())
    {
    }

    public DaaImageIndexPlugin(IDaaEntryReader entryReader)
    {
        ArgumentNullException.ThrowIfNull(entryReader);
        this.entryReader = entryReader;
    }

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
