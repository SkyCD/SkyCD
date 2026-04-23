using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Lifecycle;
using Tomlyn;
using Tomlyn.Model;

namespace SkyCD.Plugin.Toml;

public sealed class TomlCatalogPlugin : IPlugin, IFileFormatPluginCapability
{
    private const string SchemaVersion = "skycd.catalog.v1";
    private const string HierarchyStrategy = "adjacency-list";

    public string Id => "skycd.plugin.toml";
    public string Name => "TOML Format Plugin";
    public Version Version => new(1, 0, 0);
    public Version MinHostVersion => new(3, 0, 0);
    public string Description => "Example plugin that exposes TOML file format support.";

    public IReadOnlyCollection<FileFormatDescriptor> SupportedFormats =>
    [
        new FileFormatDescriptor(
            "skycd-toml",
            "SkyCD TOML",
            [".toml"],
            CanRead: true,
            CanWrite: true,
            MimeType: "application/toml")
    ];

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var reader = new StreamReader(request.Source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var text = await reader.ReadToEndAsync(cancellationToken);
            var model = Tomlyn.Toml.ToModel(text) as TomlTable;
            if (model is null)
            {
                return new FileFormatReadResult { Success = false, Error = "Invalid TOML document." };
            }

            if (model["schema"] is not TomlTable schema ||
                schema["version"]?.ToString() is not { } version ||
                !SchemaVersion.Equals(version, StringComparison.Ordinal))
            {
                return new FileFormatReadResult
                {
                    Success = false,
                    Error = "TOML_SCHEMA_ERROR: missing or unsupported schema.version."
                };
            }

            if (model["nodes"] is not TomlTableArray nodes)
            {
                return new FileFormatReadResult { Success = true, Payload = new List<Dictionary<string, object?>>() };
            }

            var rows = nodes
                .Select(node => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nodeId"] = node["nodeId"]?.ToString(),
                    ["parentId"] = node["parentId"]?.ToString(),
                    ["kind"] = node["kind"]?.ToString(),
                    ["name"] = node["name"]?.ToString(),
                    ["sizeBytes"] = node["sizeBytes"]?.ToString()
                })
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

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = request.Payload as List<Dictionary<string, object?>>
                ?? throw new InvalidOperationException("TOML payload must be a list of row dictionaries.");

            var table = new TomlTable
            {
                ["schema"] = new TomlTable
                {
                    ["version"] = SchemaVersion,
                    ["hierarchy"] = HierarchyStrategy
                }
            };

            var array = new TomlTableArray();
            foreach (var row in rows.OrderBy(row => row.TryGetValue("nodeId", out var id) ? id?.ToString() : null, StringComparer.Ordinal))
            {
                array.Add(new TomlTable
                {
                    ["nodeId"] = row.TryGetValue("nodeId", out var nodeId) ? nodeId?.ToString() ?? string.Empty : string.Empty,
                    ["parentId"] = row.TryGetValue("parentId", out var parentId) ? parentId?.ToString() ?? string.Empty : string.Empty,
                    ["kind"] = row.TryGetValue("kind", out var kind) ? kind?.ToString() ?? string.Empty : string.Empty,
                    ["name"] = row.TryGetValue("name", out var name) ? name?.ToString() ?? string.Empty : string.Empty,
                    ["sizeBytes"] = row.TryGetValue("sizeBytes", out var sizeBytes) ? sizeBytes?.ToString() ?? string.Empty : string.Empty
                });
            }

            table["nodes"] = array;

            var text = Tomlyn.Toml.FromModel(table);
            await using var writer = new StreamWriter(request.Target, new UTF8Encoding(false), leaveOpen: true);
            await writer.WriteAsync(text.AsMemory(), cancellationToken);
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
}
