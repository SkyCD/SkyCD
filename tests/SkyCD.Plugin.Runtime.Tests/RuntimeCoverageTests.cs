using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using Avalonia.Platform.Storage;
using SkyCD.Couchbase;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Documents;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Runtime.Repositories;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class RuntimeCoverageTests
{
    [Fact]
    public void PluginCompatibilityEvaluator_RespectsMinAndMaxBounds()
    {
        var min = new Version(3, 0, 0);
        var max = new Version(4, 0, 0);

        Assert.True(PluginCompatibilityEvaluator.IsCompatible(min, max, new Version(3, 5, 0)));
        Assert.False(PluginCompatibilityEvaluator.IsCompatible(min, max, new Version(2, 9, 9)));
        Assert.False(PluginCompatibilityEvaluator.IsCompatible(min, max, new Version(4, 0, 1)));
    }

    [Fact]
    public void FileFormatFilterCollection_CreatesPickerTypesWithAggregateEntries()
    {
        var collection = new FileFormatFilterCollection(
        [
            new FilePickerFileType("JSON") { Patterns = ["*.json"] },
            new FilePickerFileType("YAML") { Patterns = ["*.yaml", "*.yml"] }
        ]);

        var picker = collection.ToFilePickerTypes("All supported", "All files");

        Assert.Equal(4, picker.Count);
        Assert.Equal("All supported", picker[0].Name);
        Assert.Contains("*.json", picker[0].Patterns ?? []);
        Assert.Equal("All files", picker[^1].Name);
        Assert.Equal("*.*", (picker[^1].Patterns ?? []).Single());
    }

    [Fact]
    public async Task FileFormatManager_ResolvesFormatsAndExecutesReadWrite()
    {
        var readWriteCapability = new FakeFileFormatCapability(
            new FileFormatDescriptor("json", "JSON", [".json"], ["application/json"], true, true));
        var duplicateCapability = new FakeFileFormatCapability(
            new FileFormatDescriptor("json", "JSON Duplicate", [".json"], ["application/json"], true, true));
        var readOnlyCapability = new FakeFileFormatCapability(
            new FileFormatDescriptor("xml", "XML", [".xml"], ["application/xml"], true, false));

        var manager = new FileFormatManager([readWriteCapability, duplicateCapability, readOnlyCapability]);

        Assert.Equal(2, manager.GetOpenFormats().Count);
        Assert.Single(manager.GetSaveFormats());
        Assert.Equal("json", manager.GetPreferredSaveExtension());
        Assert.Same(readWriteCapability, manager.GetInstanceFor("data.JSON"));

        await using var readStream = new MemoryStream();
        var readResult = await manager.ReadAsync(new FileFormatReadRequest
        {
            Source = readStream,
            FormatId = "json",
            FileName = "data.json"
        }, CancellationToken.None);
        Assert.True(readResult.Success);

        await using var writeStream = new MemoryStream();
        var writeResult = await manager.WriteAsync(new FileFormatWriteRequest
        {
            Target = writeStream,
            FormatId = "json",
            FileName = "data.json",
            Payload = new object()
        }, CancellationToken.None);
        Assert.True(writeResult.Success);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () =>
            await manager.WriteAsync(new FileFormatWriteRequest
            {
                Target = new MemoryStream(),
                FormatId = "xml",
                FileName = "data.xml",
                Payload = new object()
            }, CancellationToken.None));
    }

    [Fact]
    public void DiscoveredPlugin_ToDocument_MapsPluginToDocument()
    {
        var discovered = new DiscoveredPlugin
        {
            Id = "plugin.id",
            Name = "Plugin",
            Author = new PluginAuthorDocument { Name = "Author", Url = "https://example.test" },
            ProjectUrl = "https://project.test",
            Version = new Version(1, 2, 3),
            MinHostVersion = new Version(3, 0, 0),
            MaxHostVersion = new Version(3, 9, 0),
            Description = "description",
            FileName = "plugin.dll",
            Capabilities = []
        };

        var document = discovered.ToDocument();

        Assert.Equal("plugin.id", document.Id);
        Assert.Equal("Plugin", document.Name);
        Assert.Equal("1.2.3", document.Version);
        Assert.Equal("3.0.0", document.Constraints.MinHostVersion);
        Assert.Equal("3.9.0", document.Constraints.MaxHostVersion);
        Assert.True(document.IsEnabled);
        Assert.True(document.IsAvailable);
        Assert.NotEqual(default, document.LastDiscoveredAt);
    }

    [Fact]
    public void PluginRepository_UpsertPreservesEnabledAndMarksMissingUnavailable()
    {
        var path = Path.Combine(Path.GetTempPath(), "skycd-plugin-runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        using var databaseManager = new DatabaseManager();
        databaseManager.Connect("default", path);
        var repositoryManager = new RepositoryManager(databaseManager);
        var repository = Assert.IsType<PluginRepository>(repositoryManager.For<PluginDocument>());

        repository.Save("a", new PluginDocument { Id = "a", Name = "A", IsEnabled = false, Constraints = null! });
        repository.Save("b", new PluginDocument { Id = "b", Name = "B" });
        repository.Save("c",
            new PluginDocument { Id = "c", Name = "C", IsEnabled = false, IsAvailable = false, Constraints = null! });

        repository.UpsertPluginDocuments(
        [
            new PluginDocument { Id = "a", Name = "A updated" },
            new PluginDocument { Id = "c", Name = "C updated" }
        ]);

        var all = repository.GetAll().OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();

        Assert.Equal(3, all.Length);
        Assert.False(all.Single(x => x.Id == "a").IsEnabled);
        Assert.True(all.Single(x => x.Id == "c").IsEnabled);
        Assert.True(all.Single(x => x.Id == "b").IsAvailable == false);
        Assert.NotNull(all.Single(x => x.Id == "a").Constraints);
    }

    [Fact]
    public void PluginServiceRegistrator_RegistersCapabilityTypesForPlugin()
    {
        using var services = new DryIoc.Container();
        var instance = new FakePluginCapability();
        var plugin = new DiscoveredPlugin
        {
            Id = "test.plugin",
            Name = "Test Plugin",
            Version = new Version(1, 0, 0),
            MinHostVersion = new Version(3, 0, 0),
            FileName = "test.plugin.dll",
            Capabilities = [instance]
        };
        PluginServiceRegistrator.RegisterServices(services, plugin);

        Assert.NotNull(services.Resolve<IFakePluginCapability>(ifUnresolved: IfUnresolved.ReturnDefault));
        Assert.NotNull(services.Resolve<IFakePluginCapability>(serviceKey: typeof(IFakePluginCapability),
            ifUnresolved: IfUnresolved.ReturnDefault));
    }

    private interface IFakePluginCapability : IPluginCapability
    {
    }

    private sealed class FakePluginCapability : IFakePluginCapability
    {
    }

    private sealed class FakeFileFormatCapability(FileFormatDescriptor descriptor) : IFileFormatPluginCapability
    {
        public FileFormatDescriptor SupportedFormat { get; } = descriptor;

        public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FileFormatReadResult { Success = true, Payload = new object() });
        }

        public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FileFormatWriteResult { Success = true });
        }
    }
}