using System.Text;
using System.Text.Json;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.Json;

public sealed class JsonCatalogPlugin : IPlugin, IFileFormatPluginCapability
{
    private const string SchemaVersion = "skycd.catalog.v1";

    public IReadOnlyCollection<FileFormatDescriptor> SupportedFormats =>
    [
        new(
            "skycd-json",
            "SkyCD JSON",
            [".json"],
            true,
            true,
            "application/json")
    ];

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var reader = new StreamReader(request.Source, Encoding.UTF8, leaveOpen: true);
            var json = await reader.ReadToEndAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("schemaVersion", out var schemaElement) ||
                !schemaElement.ValueKind.Equals(JsonValueKind.String))
                return new FileFormatReadResult
                {
                    Success = false,
                    Error = "JSON catalog payload is missing required 'schemaVersion'."
                };

            var actualSchemaVersion = schemaElement.GetString();
            if (!SchemaVersion.Equals(actualSchemaVersion, StringComparison.Ordinal))
                return new FileFormatReadResult
                {
                    Success = false,
                    Error = $"Unsupported schemaVersion '{actualSchemaVersion}'."
                };

            if (!root.TryGetProperty("payload", out var payloadElement))
                return new FileFormatReadResult
                {
                    Success = false,
                    Error = "JSON catalog payload is missing required 'payload'."
                };

            return new FileFormatReadResult
            {
                Success = true,
                Payload = payloadElement.Clone()
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

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var envelope = new Dictionary<string, object?>
            {
                ["schemaVersion"] = SchemaVersion,
                ["payload"] = request.Payload
            };

            await JsonSerializer.SerializeAsync(
                request.Target,
                envelope,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                },
                cancellationToken);
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

    public PluginDescriptor Descriptor => new(
        "skycd.plugin.json",
        "JSON Format Plugin",
        new Version(1, 0, 0),
        new Version(3, 0, 0),
        "Example plugin that exposes JSON file format support.");

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

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}