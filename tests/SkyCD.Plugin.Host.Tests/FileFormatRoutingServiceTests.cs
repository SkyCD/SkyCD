using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Lifecycle;
using SkyCD.Plugin.Host;
using SkyCD.Plugin.Host.FileFormats;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Plugin.Host.Tests;

public class FileFormatRoutingServiceTests
{
    [Fact]
    public void GetOpenAndSaveFormats_RespectsCapabilities()
    {
        var pluginCatalog = CreateCatalog(new TestReadOnlyPlugin(), new TestReadWritePlugin());
        var service = new FileFormatRoutingService(pluginCatalog);

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "readonly-json");
        Assert.DoesNotContain(saveFormats, format => format.FormatId == "readonly-json");
        Assert.Contains(saveFormats, format => format.FormatId == "rw-json");
    }

    [Fact]
    public async Task WriteAsync_Throws_ForReadOnlyFormat()
    {
        var pluginCatalog = CreateCatalog(new TestReadOnlyPlugin());
        var service = new FileFormatRoutingService(pluginCatalog);

        using var stream = new MemoryStream();
        var request = new FileFormatWriteRequest
        {
            FormatId = "readonly-json",
            Target = stream,
            Payload = new { Value = "x" }
        };

        await Assert.ThrowsAsync<FileFormatRoutingException>(() => service.WriteAsync(request));
    }

    [Fact]
    public async Task ReadAndWriteAsync_UsesResolvedFormatHandlers()
    {
        var pluginCatalog = CreateCatalog(new TestReadWritePlugin());
        var service = new FileFormatRoutingService(pluginCatalog);

        using var writeStream = new MemoryStream();
        var writeResult = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "rw-json",
            Target = writeStream,
            Payload = new { Value = "ok" }
        });
        Assert.True(writeResult.Success);

        writeStream.Position = 0;
        var readResult = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "rw-json",
            Source = writeStream
        });

        Assert.True(readResult.Success);
        Assert.Equal("ok", readResult.Payload?.ToString());
    }

    private static PluginCatalog CreateCatalog(params IFileFormatPluginCapability[] capabilities)
    {
        var catalog = new PluginCatalog();
        var discovered = capabilities.Select(capability => new DiscoveredPlugin
        {
            Plugin = (IPlugin)capability,
            Capabilities = [capability]
        });

        catalog.SetPlugins(discovered);
        return catalog;
    }

    private sealed class TestReadOnlyPlugin : IPlugin, IFileFormatPluginCapability
    {
        public PluginDescriptor Descriptor => new("tests.readonly", "ReadOnly", new Version(1, 0, 0), new Version(3, 0, 0));

        public IReadOnlyCollection<FileFormatDescriptor> SupportedFormats =>
        [
            new FileFormatDescriptor("readonly-json", "Read Only JSON", [".json"], CanRead: true, CanWrite: false)
        ];

        public ValueTask OnLoadAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask OnInitializeAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask OnActivateAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileFormatReadResult { Success = true, Payload = "readonly" });

        public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileFormatWriteResult { Success = false, Error = "not allowed" });
    }

    private sealed class TestReadWritePlugin : IPlugin, IFileFormatPluginCapability
    {
        public PluginDescriptor Descriptor => new("tests.readwrite", "ReadWrite", new Version(1, 0, 0), new Version(3, 0, 0));

        public IReadOnlyCollection<FileFormatDescriptor> SupportedFormats =>
        [
            new FileFormatDescriptor("rw-json", "Read/Write JSON", [".json"], CanRead: true, CanWrite: true)
        ];

        public ValueTask OnLoadAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask OnInitializeAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask OnActivateAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
        {
            request.Source.Position = 0;
            using var reader = new StreamReader(request.Source, leaveOpen: true);
            var text = reader.ReadToEnd();
            return Task.FromResult(new FileFormatReadResult { Success = true, Payload = text });
        }

        public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
        {
            using var writer = new StreamWriter(request.Target, leaveOpen: true);
            await writer.WriteAsync("ok");
            await writer.FlushAsync(cancellationToken);
            return new FileFormatWriteResult { Success = true };
        }
    }
}
