using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Archives;
using SkyCD.Plugin.Metadata.Archives.Arc;
using SkyCD.Plugin.Metadata.Archives.Arc.Tests;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;
using SkyCD.Plugin.Metadata.Archives.Arc.Models;
using SkyCD.Plugin.Metadata.Archives.Models;

namespace SkyCD.Plugin.Host.Tests;

public sealed class ArcArchiveIndexTests
{
    [Fact]
    public async Task GetWorkItemsAsync_ExtractsSafeStoredEntries_AndSkipsUnsafePartially()
    {
        using var archive = ArcTestArchiveBuilder.Create()
            .AddStored("safe.txt", "ok")
            .AddStored("../escape.txt", "evil")
            .AddStored("/absolute.txt", "evil")
            .AddStored("C:/drive.txt", "evil")
            .AddStored("folder/../../escape2.txt", "evil")
            .AddStored("folder/nested.txt", "nested")
            .Build();

        var set = await new ArchivesIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.arc"));

        Assert.Equal(["folder/nested.txt", "safe.txt"], set.Items.Select(item => item.Name).Order(StringComparer.Ordinal));
        var safe = set.Items.Single(item => item.Name == "safe.txt");
        Assert.Equal(2, safe.SizeBytes);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 6, TimeSpan.Zero), safe.ModifiedUtc);

        var extracted = Assert.IsType<FileStream>(await safe.OpenSource(CancellationToken.None));
        var fullPath = extracted.Name;
        var tempRoot = Path.GetDirectoryName(fullPath)!;
        await using (extracted)
        {
            Assert.Equal("ok", await File.ReadAllTextAsync(fullPath));
            Assert.StartsWith(
                Path.Combine(Path.GetTempPath(), "skycd-arc-index-"),
                tempRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        await DisposeAndAssertCleanupAsync(set, tempRoot, fullPath);
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "escape.txt")));
    }

    [Fact]
    public async Task GetWorkItemsAsync_DuplicateNames_KeepFirstEntry()
    {
        using var archive = ArcTestArchiveBuilder.Create()
            .AddStored("dup.txt", "first")
            .AddStored("dup.txt", "second")
            .Build();

        var set = await new ArchivesIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.arc"));

        var item = Assert.Single(set.Items);
        string tempRoot;
        await using (var source = await item.OpenSource(CancellationToken.None))
        {
            using var reader = new StreamReader(source);
            tempRoot = Path.GetDirectoryName(Assert.IsType<FileStream>(source).Name)!;
            Assert.Equal("first", await reader.ReadToEndAsync());
        }

        await DisposeAndAssertCleanupAsync(set, tempRoot, null);
    }

    [Fact]
    public async Task GetWorkItemsAsync_UnsupportedMethod_IsSkippedWithoutFailingArchive()
    {
        using var archive = ArcTestArchiveBuilder.Create()
            .AddWithMethod(1, "packed.txt", "compressed")
            .AddWithMethod(6, "crunched.txt", "compressed")
            .AddStored("stored.txt", "ok")
            .Build();

        var set = await new ArchivesIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.arc"));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("stored.txt", item.Name);
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryAndTotalLimits_AreApplied()
    {
        using var archive = ArcTestArchiveBuilder.Create()
            .AddStored("small.txt", "ok")
            .AddStored("large.txt", new string('x', 64))
            .AddStored("b.txt", "bb")
            .Build();

        var entryLimited = await new ArchivesIndexPlugin(new ArchiveIndexLimits(MaxEntries: 100, MaxEntryBytes: 8,
            MaxTotalBytes: 1024)).GetWorkItemsAsync(RequestFor(archive, "sample.arc"));
        var entryLimitedRoot = TempRootOf(entryLimited);
        Assert.Equal(["small.txt", "b.txt"], entryLimited.Items.Select(item => item.Name));
        await DisposeAndAssertCleanupAsync(entryLimited, entryLimitedRoot, null);

        var totalLimited = await new ArchivesIndexPlugin(new ArchiveIndexLimits(MaxEntries: 100, MaxEntryBytes: 1024,
            MaxTotalBytes: 10)).GetWorkItemsAsync(RequestFor(archive, "sample.arc"));
        var totalLimitedRoot = TempRootOf(totalLimited);
        Assert.Equal(["small.txt", "b.txt"], totalLimited.Items.Select(item => item.Name));
        await DisposeAndAssertCleanupAsync(totalLimited, totalLimitedRoot, null);

        var countLimited = await new ArchivesIndexPlugin(new ArchiveIndexLimits(MaxEntries: 1, MaxEntryBytes: 1024,
            MaxTotalBytes: 1024)).GetWorkItemsAsync(RequestFor(archive, "sample.arc"));
        Assert.Empty(countLimited.Items);
        await countLimited.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_PropagatesAndRemovesTempDirectory()
    {
        using var archive = ArcTestArchiveBuilder.Create()
            .AddStored("a.txt", "a")
            .AddStored("b.txt", "b")
            .Build();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ArchivesIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.arc"), cancellation.Token));

        Assert.DoesNotContain(FindTempDirectories("skycd-arc-index-"), ContainsEntryNamed("a.txt"));
    }

    [Fact]
    public async Task GetWorkItemsAsync_CancelledMidway_RemovesExtractedFiles()
    {
        // A source that cancels its own token while the entry payload is copied proves mid-copy cleanup:
        // the cancellation surfaces as an OperationCanceledException and the temp directory disappears.
        var large = new string('z', 900_000);
        using var archive = ArcTestArchiveBuilder.Create()
            .AddStored("first.txt", large)
            .AddStored("second.txt", "small")
            .Build();
        using var cancellation = new CancellationTokenSource();
        var before = FindTempDirectories("skycd-arc-index-").Count;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ArchivesIndexPlugin().GetWorkItemsAsync(new IndexWorkItemRequest
            {
                FileName = "sample.arc",
                SizeBytes = archive.Length,
                OpenSource = token => Task.FromResult<Stream>(new NonDisposingStream(
                    new CancellingReadStream(archive, cancellation, token, bytesBeforeCancel: 200_000)))
            }, cancellation.Token));

        var leftovers = FindTempDirectories("skycd-arc-index-");
        Assert.Equal(before, leftovers.Count);
        Assert.DoesNotContain(leftovers, ContainsEntryNamed("first.txt"));
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonSeekableSource_IsBufferedAndCleanedUp()
    {
        using var archive = ArcTestArchiveBuilder.Create().AddStored("dir/file.txt", "data").Build();

        var set = await new ArchivesIndexPlugin().GetWorkItemsAsync(
            new IndexWorkItemRequest
            {
                FileName = "sample.arc",
                SizeBytes = archive.Length,
                OpenSource = _ => Task.FromResult<Stream>(new ForwardOnlyStream(archive))
            });

        var item = Assert.Single(set.Items);
        Assert.Equal("dir/file.txt", item.Name);
        var tempRoot = TempRootOf(set);
        Assert.False(File.Exists(Path.Combine(tempRoot, ".skycd-arc-source")));
        await DisposeAndAssertCleanupAsync(set, tempRoot, null);
    }

    [Fact]
    public async Task GetWorkItemsAsync_NotArcSource_ReturnsEmptySetAndLeavesNoTempDirectory()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));
        var before = FindTempDirectories("skycd-arc-index-").Count;

        var set = await new ArchivesIndexPlugin().GetWorkItemsAsync(RequestFor(stream, "notes.arc"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
        Assert.Equal(before, FindTempDirectories("skycd-arc-index-").Count);
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonArcFileName_ReturnsEmptySet()
    {
        using var archive = ArcTestArchiveBuilder.Create().AddStored("a.txt", "a").Build();

        var set = await new ArchivesIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "notes.txt"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContributedItems()
    {
        using var archive = ArcTestArchiveBuilder.Create().AddStored("images/photo.txt", "metadata-source").Build();
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new ArchivesIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(archive, "sample.arc"));

        var entry = Assert.Single(indexed);
        Assert.Equal("images/photo.txt", entry.Name);
        var providerResult = Assert.Single(entry.Results);
        Assert.False(providerResult.Failed);
        Assert.Contains(providerResult.Metadata!.Values, value =>
            value.Key == "indexed" && value.Text == "yes");
    }

    [Fact]
    public async Task Sequence_WithoutMetadataProviders_ContributesNothing()
    {
        using var archive = ArcTestArchiveBuilder.Create().AddStored("a.txt", "a").Build();
        var sequence = new IndexWorkItemSequence(new MediaExtensionManager([]), [new ArchivesIndexPlugin()]);

        Assert.False(sequence.HasMetadataProviders);
        Assert.Empty(await sequence.ProcessAsync(RequestFor(archive, "sample.arc")));
    }

    private static IndexWorkItemRequest RequestFor(Stream stream, string fileName) => new()
    {
        FileName = fileName,
        SizeBytes = stream.Length,
        OpenSource = _ => Task.FromResult<Stream>(new NonDisposingStream(stream))
    };

    private static string TempRootOf(IndexWorkItemSet set) => set.Items.Count == 0
        ? string.Empty
        : FindRootOfItem(set.Items[0]);

    private static string FindRootOfItem(IndexWorkItem item)
    {
        var stream = item.OpenSource(CancellationToken.None).GetAwaiter().GetResult();
        try
        {
            return Path.GetDirectoryName(Assert.IsType<FileStream>(stream).Name)!;
        }
        finally
        {
            stream.Dispose();
        }
    }

    private static List<string> FindTempDirectories(string prefix) =>
        Directory.EnumerateDirectories(Path.GetTempPath(), prefix + "*").ToList();

    private static Predicate<string> ContainsEntryNamed(string name) => directory =>
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Any(file => string.Equals(Path.GetFileName(file), name, StringComparison.Ordinal));
        }
        catch (IOException)
        {
            return false;
        }
    };

    private static async Task DisposeAndAssertCleanupAsync(IndexWorkItemSet set, string tempRoot, string? file)
    {
        await set.DisposeAsync();
        if (file is not null)
        {
            Assert.False(File.Exists(file));
        }

        if (tempRoot.Length > 0)
        {
            Assert.False(Directory.Exists(tempRoot));
        }
    }

    /// <summary>Lets the plugin dispose its source stream while the test keeps ownership of the memory stream.</summary>
    private class NonDisposingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
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
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing)
        {
        }
    }

    /// <summary>Exposes only sequential reads so the plugin must buffer the source itself.</summary>
    private sealed class ForwardOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
        }
    }

    /// <summary>
    /// Cancels the supplied token once enough bytes were read, simulating a cancel that races the entry copy.
    /// Only sequential reads are supported so the plugin must buffer the source into the temp directory.
    /// </summary>
    private sealed class CancellingReadStream(Stream inner, CancellationTokenSource source,
        CancellationToken cancellationToken, int bytesBeforeCancel) : Stream
    {
        private long bytesRead;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = inner.Read(buffer.Span);
            bytesRead += read;
            if (bytesRead >= bytesBeforeCancel)
            {
                source.Cancel();
            }

            return ValueTask.FromResult(read);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
        }
    }

    private sealed class TestMetadataCapability : IMediaMetadataCapability
    {
        public MediaDescriptor Descriptor { get; } = new(
            Extensions: ["*"],
            ContentTypes: ["*"],
            Schema: new MediaMetadataSchema("test.schema", 1,
                [new MediaMetadataField("indexed", MediaMetadataType.Text)]),
            Priority: 0,
            IsOptIn: false);

        public Task<MediaExtractionResult> ExtractAsync(
            MediaItem item,
            IMediaSource source,
            IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new MediaExtractionResult([
                new MediaMetadataValue("indexed", MediaMetadataType.Text, Text: "yes")
            ]));
        }
    }
}
