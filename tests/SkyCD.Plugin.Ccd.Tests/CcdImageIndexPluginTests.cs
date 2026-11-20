using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Ccd.Tests;

public sealed class CcdImageIndexPluginTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersFileFormatAndWorkItemProviders_OnSameInstance()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(CcdImageIndexPlugin).Assembly);

        var workItems = Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());
        var fileFormat = Assert.Single(plugin.Capabilities.OfType<IFileFormatPluginCapability>());

        Assert.Same(workItems, fileFormat);
        Assert.Equal("skycd-ccd", fileFormat.SupportedFormat.FormatId);
    }

    [Fact]
    public void OpenFormats_IncludeCcd_ButSaveFormatsDoNot()
    {
        var service = new FileFormatManager([new CcdImageIndexPlugin()]);

        Assert.Contains(service.GetOpenFormats(),
            format => format.FormatId == "skycd-ccd" && format.Extensions.Contains(".ccd"));
        Assert.DoesNotContain(service.GetSaveFormats(), format => format.FormatId == "skycd-ccd");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyCcdFormat()
    {
        var service = new FileFormatManager([new CcdImageIndexPlugin()]);
        await using var target = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-ccd",
                Target = target,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(2048, 0, "MODE1/2048")]
    [InlineData(2352, 16, "MODE1/2352")]
    [InlineData(2352, 24, "MODE2/2352")]
    [InlineData(2336, 8, "MODE2/2336")]
    public async Task ReadAsync_SectorLayout_ExposesIsoFilesystem(int sectorSize, int dataOffset, string mode)
    {
        using var temp = new TempDirectory();
        var image = CcdTestImage.Repack(CcdTestImage.BuildIso(), sectorSize, dataOffset);
        var descriptor = CcdTestImage.BuildDescriptor([(mode, 0)]);
        var path = CcdTestImage.WriteSet(temp.Path, "catalog", image, descriptor);

        var result = await ReadAsync(path);

        Assert.True(result.Success, result.Error);
        var rows = Rows(result);
        Assert.Contains(rows, row => Equals(row["fullPath"], "ROOT/DEEP") && Equals(row["kind"], "folder"));
        var file = Assert.Single(rows, row => Equals(row["fullPath"], "ROOT/DEEP/FIXTURE.TXT"));
        Assert.Equal("file", file["kind"]);
        Assert.Equal("FIXTURE.TXT", file["name"]);
        Assert.Equal(CcdTestImage.FixtureContent.Length.ToString(), file["sizeBytes"]);
    }

    [Fact]
    public async Task ReadAsync_DataTrackAfterAudioTrack_IsRead()
    {
        using var temp = new TempDirectory();
        var image = CcdTestImage.WithAudioPrefix(CcdTestImage.BuildIso(), audioSectors: 10);
        var descriptor = CcdTestImage.BuildDescriptor([("AUDIO", 0), ("MODE1/2352", 10)]);
        var path = CcdTestImage.WriteSet(temp.Path, "mixed", image, descriptor);

        var result = await ReadAsync(path);

        Assert.True(result.Success, result.Error);
        Assert.Contains(Rows(result), row => Equals(row["fullPath"], "ROOT/DEEP/FIXTURE.TXT"));
    }

    [Fact]
    public async Task ReadAsync_DescriptorImageFileEntry_IsResolvedCaseInsensitively()
    {
        using var temp = new TempDirectory();
        var image = CcdTestImage.Repack(CcdTestImage.BuildIso(), 2352, 16);
        var descriptor = CcdTestImage.BuildDescriptor([("MODE1/2352", 0)], imageFile: "DATA.IMG");
        var path = CcdTestImage.WriteSet(temp.Path, "catalog", image, descriptor, imageFileName: "data.img");

        var result = await ReadAsync(path);

        Assert.True(result.Success, result.Error);
        Assert.Contains(Rows(result), row => Equals(row["fullPath"], "ROOT/README.TXT"));
    }

    [Fact]
    public async Task ReadAsync_MissingCompanion_FailsWithCompanionName()
    {
        using var temp = new TempDirectory();
        var descriptorPath = Path.Combine(temp.Path, "catalog.ccd");
        await File.WriteAllTextAsync(descriptorPath, CcdTestImage.BuildDescriptor([("MODE1/2352", 0)]));

        var result = await ReadAsync(descriptorPath);

        Assert.False(result.Success);
        Assert.Contains("catalog.img", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_DescriptorTraversal_DoesNotResolveOutsideDirectory()
    {
        using var temp = new TempDirectory();
        var setDirectory = Path.Combine(temp.Path, "set");
        File.WriteAllBytes(Path.Combine(temp.Path, "escape.img"), CcdTestImage.BuildIso());
        var descriptor = CcdTestImage.BuildDescriptor([("MODE1/2048", 0)], imageFile: "../escape.img");
        var path = CcdTestImage.WriteSet(setDirectory, "catalog", [], descriptor);

        var result = await ReadAsync(path);

        Assert.False(result.Success);
        Assert.Contains("escape.img", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_DamagedDescriptor_FailsGracefully()
    {
        using var temp = new TempDirectory();
        var descriptor = new string('x', 32) + "\0[Disc]\n[TRACK 1]\nMODE=MODE1/2352\n";
        var path = CcdTestImage.WriteSet(temp.Path, "catalog", CcdTestImage.BuildIso(), descriptor);

        var result = await ReadAsync(path);

        Assert.False(result.Success);
        Assert.Contains("descriptor", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_AudioOnlyDescriptor_FailsWithNoDataTrack()
    {
        using var temp = new TempDirectory();
        var descriptor = CcdTestImage.BuildDescriptor([("AUDIO", 0)]);
        var path = CcdTestImage.WriteSet(temp.Path, "audio", CcdTestImage.Repack(CcdTestImage.BuildIso(), 2352, 16),
            descriptor);

        var result = await ReadAsync(path);

        Assert.False(result.Success);
        Assert.Contains("data track", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_ScrambledImage_IsRejected()
    {
        using var temp = new TempDirectory();
        var descriptor = CcdTestImage.BuildDescriptor([("MODE1/2352", 0)], scrambled: 1);
        var path = CcdTestImage.WriteSet(temp.Path, "catalog", CcdTestImage.Repack(CcdTestImage.BuildIso(), 2352, 16),
            descriptor);

        var result = await ReadAsync(path);

        Assert.False(result.Success);
        Assert.Contains("scrambled", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetWorkItemsAsync_ListsContainedFiles_AndOpensTheirContent()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var before = TemporaryEntryFiles();

        await using var set = await new CcdImageIndexPlugin().GetWorkItemsAsync(RequestFor(path));

        Assert.Equal(2, set.Items.Count(item => item.Name.EndsWith(".TXT", StringComparison.Ordinal)));
        var item = Assert.Single(set.Items, candidate => candidate.Name == "ROOT/DEEP/FIXTURE.TXT");
        Assert.Equal(CcdTestImage.FixtureContent.Length, item.SizeBytes);
        await using (var content = await item.OpenSource(CancellationToken.None))
        using (var reader = new StreamReader(content, Encoding.UTF8))
        {
            Assert.Equal(CcdTestImage.FixtureContent, await reader.ReadToEndAsync());
        }

        Assert.Equal(before, TemporaryEntryFiles());
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonCcdSource_ReturnsEmptySet()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        await using var set = await new CcdImageIndexPlugin().GetWorkItemsAsync(new IndexWorkItemRequest
        {
            FileName = "notes.txt",
            SizeBytes = stream.Length,
            OpenSource = _ => Task.FromResult<Stream>(new NonDisposingStream(stream))
        });

        Assert.Empty(set.Items);
    }

    [Fact]
    public async Task GetWorkItemsAsync_MissingCompanion_ReturnsEmptySet()
    {
        using var temp = new TempDirectory();
        var descriptorPath = Path.Combine(temp.Path, "catalog.ccd");
        await File.WriteAllTextAsync(descriptorPath, CcdTestImage.BuildDescriptor([("MODE1/2352", 0)]));

        await using var set = await new CcdImageIndexPlugin().GetWorkItemsAsync(RequestFor(descriptorPath));

        Assert.Empty(set.Items);
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped_WhileSafeEntryIsReturned()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var plugin = new CcdImageIndexPlugin(new CcdIndexLimits(MaxEntries: 100, MaxEntryBytes: 8));

        await using var set = await plugin.GetWorkItemsAsync(RequestFor(path));

        var item = Assert.Single(set.Items);
        Assert.Equal("ROOT/README.TXT", item.Name);
    }

    [Fact]
    public async Task GetWorkItemsAsync_MaxEntries_LimitsContributedItems()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var plugin = new CcdImageIndexPlugin(new CcdIndexLimits(MaxEntries: 1));

        await using var set = await plugin.GetWorkItemsAsync(RequestFor(path));

        Assert.Single(set.Items);
    }

    [Fact]
    public async Task GetWorkItemsAsync_TotalBudget_BoundsMaterializedBytes()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var plugin = new CcdImageIndexPlugin(new CcdIndexLimits(MaxTotalBytes: 20));

        await using var set = await plugin.GetWorkItemsAsync(RequestFor(path));

        var item = Assert.Single(set.Items);
        Assert.True(item.SizeBytes <= 20);
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CcdImageIndexPlugin().GetWorkItemsAsync(RequestFor(path), cancellation.Token));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Constructor_RejectsNonPositiveLimits(int maxEntries, long maxEntryBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CcdImageIndexPlugin(new CcdIndexLimits(maxEntries, maxEntryBytes)));
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContainedItems()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new CcdImageIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(path));

        Assert.Equal(2, indexed.Count);
        var entry = Assert.Single(indexed, candidate => candidate.Name == "ROOT/DEEP/FIXTURE.TXT");
        var providerResult = Assert.Single(entry.Results);
        Assert.False(providerResult.Failed);
        Assert.Contains(providerResult.Metadata!.Values, value =>
            value.Key == "indexed" && value.Text == "yes");
    }

    [Fact]
    public async Task Fixture_ReadsThroughCommittedFiles()
    {
        var descriptorPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Ccd", "catalog-sample.ccd");
        Assert.True(File.Exists(descriptorPath));
        Assert.True(File.Exists(Path.ChangeExtension(descriptorPath, ".img")));

        var result = await ReadAsync(descriptorPath);

        Assert.True(result.Success, result.Error);
        Assert.Contains(Rows(result), row => Equals(row["fullPath"], "ROOT/DEEP/FIXTURE.TXT"));
    }

    private static async Task<FileFormatReadResult> ReadAsync(string descriptorPath)
    {
        await using var source = File.OpenRead(descriptorPath);
        return await new CcdImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-ccd",
            Source = source,
            FileName = Path.GetFileName(descriptorPath),
            SourcePath = descriptorPath
        });
    }

    private static List<Dictionary<string, object?>> Rows(FileFormatReadResult result) =>
        Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);

    private static string WriteCatalog(string directory)
    {
        var image = CcdTestImage.Repack(CcdTestImage.BuildIso(), 2352, 16);
        var descriptor = CcdTestImage.BuildDescriptor([("MODE1/2352", 0)]);
        return CcdTestImage.WriteSet(directory, "catalog", image, descriptor);
    }

    private static IndexWorkItemRequest RequestFor(string descriptorPath) => new()
    {
        FileName = Path.GetFileName(descriptorPath),
        SizeBytes = new FileInfo(descriptorPath).Length,
        SourcePath = descriptorPath,
        OpenSource = _ => Task.FromResult<Stream>(File.OpenRead(descriptorPath))
    };

    private static List<string> TemporaryEntryFiles() =>
        Directory.Exists(Path.GetTempPath())
            ? Directory.GetFiles(Path.GetTempPath(), "skycd-ccd-entry-*.tmp").ToList()
            : [];

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"skycd-ccd-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    /// <summary>Lets the plugin dispose its stream while the test keeps ownership of the memory stream.</summary>
    private sealed class NonDisposingStream(Stream inner) : Stream
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
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
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
