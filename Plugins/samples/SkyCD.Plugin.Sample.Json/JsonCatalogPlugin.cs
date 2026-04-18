using System.Text;
using System.Text.Json;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.Sample.Json;

public sealed class JsonCatalogPlugin : IPlugin, IFileFormatPluginCapability
{
    public PluginDescriptor Descriptor => new(
        "skycd.plugin.sample.json",
        "Sample JSON Format Plugin",
        new Version(1, 0, 0),
        new Version(3, 0, 0),
        "Example plugin that exposes JSON file format support.");

    public IReadOnlyCollection<FileFormatDescriptor> SupportedFormats =>
    [
        new FileFormatDescriptor(
            "skycd-json",
            "SkyCD JSON",
            [".json"],
            CanRead: true,
            CanWrite: true,
            MimeType: "application/json")
    ];

    public ValueTask OnLoadAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask OnInitializeAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask OnActivateAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var reader = new StreamReader(request.Source, Encoding.UTF8, leaveOpen: true);
            var json = await reader.ReadToEndAsync(cancellationToken);

            var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
            return new FileFormatReadResult
            {
                Success = true,
                Payload = payload ?? new Dictionary<string, object?>()
            };
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

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await JsonSerializer.SerializeAsync(request.Target, request.Payload, cancellationToken: cancellationToken);
            await request.Target.FlushAsync(cancellationToken);

            return new FileFormatWriteResult
            {
                Success = true
            };
        }
        catch (Exception exception)
        {
            return new FileFormatWriteResult
            {
                Success = false,
                Error = exception.Message
            };
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
