using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Katalog.CsvIdx;
using SkyCD.Plugin.FileFormat.Katalog.Sqlite;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.FileFormat.Katalog.Tests;

public sealed class KatalogPluginDiscoveryTests : IDisposable
{
    private readonly KatalogFixtureBuilder fixtures = new();

    public void Dispose() => fixtures.Dispose();

    [Fact]
    public void Assembly_DiscoversOnePluginWithTwoFormatCapabilities()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(KatalogCsvIdxPlugin).Assembly);

        Assert.Equal("SkyCD.Plugin.FileFormat.Katalog", plugin.Id);
        var formats = plugin.Capabilities
            .OfType<IFileFormatPluginCapability>()
            .Select(capability => capability.SupportedFormat.FormatId)
            .OrderBy(formatId => formatId, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(["katalog-csv-idx", "katalog-sqlite"], formats);
    }

    [Fact]
    public void FileFormatManager_ResolvesCollectionFolderToCsvIdx()
    {
        var manager = CreateManager();
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "KatalogCollection");

        var resolved = manager.GetInstanceFor(folder);

        Assert.Equal("katalog-csv-idx", resolved.SupportedFormat.FormatId);
    }

    [Fact]
    public void FileFormatManager_ResolvesKatalogDatabaseByContent()
    {
        var manager = CreateManager();
        var path = fixtures.CreateFullCollection("detect.db");
        using var stream = File.OpenRead(path);

        var resolved = manager.GetInstanceFor("detect.db", stream);

        Assert.Equal("katalog-sqlite", resolved.SupportedFormat.FormatId);
    }

    private static FileFormatManager CreateManager() =>
        new([new UnrelatedDbPlugin(), new KatalogCsvIdxPlugin(), new KatalogSqlitePlugin()]);

    private sealed class UnrelatedDbPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
    {
        public FileFormatDescriptor SupportedFormat =>
            new(
                FormatId: "unrelated-db",
                DisplayName: "Unrelated database",
                Extensions: [".db"],
                MimeTypes: [],
                CanRead: true,
                CanWrite: false);

        public bool CanHandleContent(Stream stream, string? fileName = null) => false;

        public Task<FileFormatReadResult> ReadAsync(
            FileFormatReadRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileFormatReadResult { Success = false, Error = "unsupported" });

        public Task<FileFormatWriteResult> WriteAsync(
            FileFormatWriteRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileFormatWriteResult { Success = false, Error = "unsupported" });
    }
}
