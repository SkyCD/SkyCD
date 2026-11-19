using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Archives;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Archives;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Zip;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public sealed class ZipArchiveExtractionTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersArchiveAndFileFormatCapabilities_OnSameInstance()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(ZipArchiveIndexPlugin).Assembly);

        var archive = Assert.Single(plugin.Capabilities.OfType<IArchivePluginCapability>());
        var fileFormat = Assert.Single(plugin.Capabilities.OfType<SkyCD.Plugin.Abstractions.Capabilities.FileFormats.IFileFormatPluginCapability>());

        Assert.Same(archive, fileFormat);
        Assert.Equal("skycd-zip", archive.SupportedArchive.FormatId);
    }

    [Fact]
    public async Task InspectAsync_ListsEntries_AndFlagsUnsafePaths()
    {
        using var zip = CreateZip(
            ("root/deep/file.txt", "data"),
            ("../escape.txt", "evil"),
            ("/absolute.txt", "evil"));

        var plugin = new ZipArchiveIndexPlugin();
        var result = await plugin.InspectAsync(new ArchiveInspectionRequest
        {
            Source = zip,
            FileName = "sample.zip"
        });

        Assert.True(result.Success, result.Error);
        Assert.Contains(result.Entries, entry =>
            entry.Path == "root/deep/file.txt"
            && entry.Kind == ArchiveEntryKind.File
            && entry.SkipReason == ArchiveEntrySkipReason.None);
        Assert.Contains(result.Entries, entry =>
            entry.Path == "../escape.txt" && entry.SkipReason == ArchiveEntrySkipReason.UnsafePath);
        Assert.Contains(result.Entries, entry =>
            entry.Path == "absolute.txt" && entry.SkipReason == ArchiveEntrySkipReason.UnsafePath);
    }

    [Fact]
    public async Task InspectAsync_DamagedArchive_ReturnsFailure()
    {
        using var broken = new MemoryStream(Encoding.UTF8.GetBytes("not a zip archive"));

        var result = await new ZipArchiveIndexPlugin().InspectAsync(new ArchiveInspectionRequest { Source = broken });

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task ExtractToTemporaryDirectory_ExtractsFiles_AndDisposeCleansUp()
    {
        var parent = CreateTempParent();
        try
        {
            using var zip = CreateZip(("root/deep/file.txt", "hello"));
            var plugin = new ZipArchiveIndexPlugin();

            var result = await plugin.ExtractToTemporaryDirectoryAsync(new ArchiveExtractionRequest
            {
                Source = zip,
                FileName = "sample.zip",
                TargetDirectory = parent
            });

            Assert.True(result.Inspection.Success, result.Inspection.Error);
            var temp = Assert.IsType<string>(result.TemporaryDirectory);
            Assert.True(Directory.Exists(temp));

            var file = Assert.Single(result.Entries, entry => entry.Path == "root/deep/file.txt");
            Assert.Equal(ArchiveEntrySkipReason.None, file.SkipReason);
            Assert.NotNull(file.FullPath);
            Assert.StartsWith(temp, file.FullPath!);
            Assert.Equal("hello", await File.ReadAllTextAsync(file.FullPath!));

            result.Dispose();
            Assert.False(Directory.Exists(temp));
        }
        finally
        {
            SafeDeleteDirectory(parent);
        }
    }

    [Fact]
    public async Task ExtractToTemporaryDirectory_SkipsUnsafePaths_WithoutWritingOutsideTemp()
    {
        var parent = CreateTempParent();
        try
        {
            using var zip = CreateZip(
                ("safe.txt", "ok"),
                ("../escape.txt", "evil"),
                ("/absolute.txt", "evil"),
                ("folder/../../escape2.txt", "evil"));
            var plugin = new ZipArchiveIndexPlugin();

            var result = await plugin.ExtractToTemporaryDirectoryAsync(new ArchiveExtractionRequest
            {
                Source = zip,
                TargetDirectory = parent
            });

            try
            {
                Assert.True(result.Inspection.Success, result.Inspection.Error);
                var temp = Assert.IsType<string>(result.TemporaryDirectory);
                Assert.Contains(result.Entries, entry =>
                    entry.Path == "safe.txt" && entry.SkipReason == ArchiveEntrySkipReason.None);
                Assert.All(
                    result.Entries.Where(entry => entry.Path.Contains("..", StringComparison.Ordinal)
                                                  || entry.Path == "absolute.txt"),
                    entry => Assert.Equal(ArchiveEntrySkipReason.UnsafePath, entry.SkipReason));

                Assert.False(File.Exists(Path.Combine(parent, "escape.txt")));
                Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(parent)!, "escape.txt")));
                Assert.Equal(new[] { temp }, Directory.GetDirectories(parent));
            }
            finally
            {
                result.Dispose();
            }
        }
        finally
        {
            SafeDeleteDirectory(parent);
        }
    }

    [Fact]
    public async Task ExtractToTemporaryDirectory_DamagedArchive_ReturnsFailure_AndCleansUp()
    {
        var parent = CreateTempParent();
        try
        {
            using var broken = new MemoryStream(Encoding.UTF8.GetBytes("not a zip archive"));
            var plugin = new ZipArchiveIndexPlugin();

            var result = await plugin.ExtractToTemporaryDirectoryAsync(new ArchiveExtractionRequest
            {
                Source = broken,
                TargetDirectory = parent
            });

            Assert.False(result.Inspection.Success);
            Assert.Null(result.TemporaryDirectory);
            Assert.Empty(result.Entries);
            Assert.Empty(Directory.GetFileSystemEntries(parent));
        }
        finally
        {
            SafeDeleteDirectory(parent);
        }
    }

    [Fact]
    public async Task ExtractToTemporaryDirectory_EntryOverLimit_IsSkipped_WhileSafeEntryExtracts()
    {
        var parent = CreateTempParent();
        try
        {
            using var zip = CreateZip(("small.txt", "ok"), ("large.txt", new string('x', 64)));
            var plugin = new ZipArchiveIndexPlugin(new ArchiveLimits(MaxEntries: 100, MaxEntryBytes: 8,
                MaxTotalBytes: 1024));

            var result = await plugin.ExtractToTemporaryDirectoryAsync(new ArchiveExtractionRequest
            {
                Source = zip,
                TargetDirectory = parent
            });

            try
            {
                Assert.True(result.Inspection.Success, result.Inspection.Error);
                Assert.Contains(result.Entries, entry =>
                    entry.Path == "small.txt" && entry.SkipReason == ArchiveEntrySkipReason.None);
                Assert.Contains(result.Entries, entry =>
                    entry.Path == "large.txt" && entry.SkipReason == ArchiveEntrySkipReason.LimitExceeded);
                Assert.DoesNotContain(result.Entries, entry =>
                    entry.Path == "large.txt" && entry.FullPath is not null);
            }
            finally
            {
                result.Dispose();
            }
        }
        finally
        {
            SafeDeleteDirectory(parent);
        }
    }

    [Fact]
    public async Task ExtractToTemporaryDirectory_Cancellation_CleansUpTempDirectory()
    {
        var parent = CreateTempParent();
        try
        {
            using var zip = CreateZip(("a.txt", "a"), ("b.txt", "b"));
            var plugin = new ZipArchiveIndexPlugin();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                plugin.ExtractToTemporaryDirectoryAsync(new ArchiveExtractionRequest
                {
                    Source = zip,
                    TargetDirectory = parent
                }, cancellation.Token));

            Assert.Empty(Directory.GetFileSystemEntries(parent));
        }
        finally
        {
            SafeDeleteDirectory(parent);
        }
    }

    [Fact]
    public async Task ArchiveMetadataIndexer_RunsEnabledMetadataProviders_AndCleansUp()
    {
        var parent = CreateTempParent();
        try
        {
            using var zip = CreateZip(("images/photo.txt", "metadata-source"));
            var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
            var indexer = new ArchiveMetadataIndexer(manager);

            var result = await indexer.IndexAsync(new ZipArchiveIndexPlugin(), zip, "sample.zip", parent);

            Assert.True(result.Inspection.Success, result.Inspection.Error);
            var entry = Assert.Single(result.EntryMetadata);
            Assert.Equal("images/photo.txt", entry.Path);
            var providerResult = Assert.Single(entry.Results);
            Assert.False(providerResult.Failed);
            Assert.NotNull(providerResult.Metadata);
            Assert.Contains(providerResult.Metadata!.Values, value =>
                value.Key == "indexed" && value.Text == "yes");
            Assert.Empty(Directory.GetFileSystemEntries(parent));
        }
        finally
        {
            SafeDeleteDirectory(parent);
        }
    }

    [Fact]
    public async Task ArchiveMetadataIndexer_WithoutProviders_StillCleansUp()
    {
        var parent = CreateTempParent();
        try
        {
            using var zip = CreateZip(("a.txt", "a"));
            var indexer = new ArchiveMetadataIndexer(new MediaExtensionManager([]));

            var result = await indexer.IndexAsync(new ZipArchiveIndexPlugin(), zip, "sample.zip", parent);

            Assert.True(result.Inspection.Success, result.Inspection.Error);
            Assert.Empty(result.EntryMetadata);
            Assert.Empty(Directory.GetFileSystemEntries(parent));
        }
        finally
        {
            SafeDeleteDirectory(parent);
        }
    }

    private static MemoryStream CreateZip(params (string Path, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false), leaveOpen: false);
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static string CreateTempParent()
    {
        var path = Path.Combine(Path.GetTempPath(), "skycd-zip-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort test cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort test cleanup.
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
