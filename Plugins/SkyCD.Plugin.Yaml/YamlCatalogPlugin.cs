using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Lifecycle;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SkyCD.Plugin.Yaml;

public sealed class YamlCatalogPlugin : IPlugin, IFileFormatPluginCapability
{
    private const string SchemaVersion = "skycd.catalog.v1";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public IReadOnlyCollection<FileFormatDescriptor> SupportedFormats =>
    [
        new(
            "skycd-yaml",
            "SkyCD YAML",
            [".yaml", ".yml"],
            true,
            true,
            "application/yaml")
    ];

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var reader = new StreamReader(request.Source, Encoding.UTF8, leaveOpen: true);
            var yaml = await reader.ReadToEndAsync(cancellationToken);

            if (yaml.Contains("<<:", StringComparison.Ordinal) || yaml.Contains('*'))
                return new FileFormatReadResult
                {
                    Success = false,
                    Error = "YAML_UNSUPPORTED_CONSTRUCT: aliases and merge keys are not supported in strict mode."
                };

            var document = Deserializer.Deserialize<YamlCatalogDocument>(yaml);
            if (document is null || !SchemaVersion.Equals(document.SchemaVersion, StringComparison.Ordinal))
                return new FileFormatReadResult
                {
                    Success = false,
                    Error = "YAML_SCHEMA_ERROR: missing or unsupported schemaVersion."
                };

            var rows = (document.Payload ?? [])
                .Select(row => row.ToDictionary(
                    pair => pair.Key,
                    pair => (object?)pair.Value,
                    StringComparer.OrdinalIgnoreCase))
                .ToList();

            return new FileFormatReadResult
            {
                Success = true,
                Payload = rows
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
            var rows = request.Payload as List<Dictionary<string, object?>>
                       ?? throw new InvalidOperationException("YAML payload must be a list of row dictionaries.");

            var orderedRows = rows
                .Select(row => new SortedDictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["kind"] = row.TryGetValue("kind", out var kind) ? kind?.ToString() : null,
                    ["name"] = row.TryGetValue("name", out var name) ? name?.ToString() : null,
                    ["nodeId"] = row.TryGetValue("nodeId", out var nodeId) ? nodeId?.ToString() : null,
                    ["parentId"] = row.TryGetValue("parentId", out var parentId) ? parentId?.ToString() : null,
                    ["sizeBytes"] = row.TryGetValue("sizeBytes", out var sizeBytes) ? sizeBytes?.ToString() : null
                })
                .OrderBy(row => row["nodeId"], StringComparer.Ordinal)
                .ToList();

            var document = new
            {
                schemaVersion = SchemaVersion,
                payload = orderedRows
            };

            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .DisableAliases()
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
                .Build();

            var yaml = serializer.Serialize(document);
            await using var writer = new StreamWriter(request.Target, new UTF8Encoding(false), leaveOpen: true);
            await writer.WriteAsync(yaml.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);

            return new FileFormatWriteResult { Success = true };
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
        "skycd.plugin.yaml",
        "YAML Format Plugin",
        new Version(1, 0, 0),
        new Version(3, 0, 0),
        "Example plugin that exposes YAML file format support.");

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

    private sealed class YamlCatalogDocument
    {
        public string? SchemaVersion { get; set; }
        public List<Dictionary<string, string?>>? Payload { get; set; }
    }
}