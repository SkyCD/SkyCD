using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using Tomlyn;
using Tomlyn.Model;

namespace SkyCD.Plugin.Toml;

public sealed class TomlCatalogPlugin : IFileFormatPluginCapability
{
    private const string SchemaVersion = "skycd.catalog.v1";
    private const string HierarchyStrategy = "adjacency-list";

    public FileFormatDescriptor SupportedFormat =>
        new FileFormatDescriptor(
            "skycd-toml",
            "SkyCD TOML",
            [".toml"],
            CanRead: true,
            CanWrite: true,
            MimeType: "application/toml");

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var reader = new StreamReader(request.Source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var text = await reader.ReadToEndAsync(cancellationToken);
            var model = TomlSerializer.Deserialize<TomlTable>(text);
            if (model is null)
            {
                return new FileFormatReadResult { Success = false, Error = "Invalid TOML document." };
            }

            if (!model.TryGetValue("schema", out var schemaObj) ||
                schemaObj is not TomlTable schema ||
                !schema.TryGetValue("version", out var versionObj) ||
                versionObj?.ToString() is not { } version ||
                !SchemaVersion.Equals(version, StringComparison.Ordinal))
            {
                return new FileFormatReadResult
                {
                    Success = false,
                    Error = "TOML_SCHEMA_ERROR: missing or unsupported schema.version."
                };
            }

            if (!model.TryGetValue("nodes", out var nodesObj) || nodesObj is not TomlTableArray nodes)
            {
                return new FileFormatReadResult { Success = true, Payload = new List<Dictionary<string, object?>>() };
            }

            var rows = nodes
                .Select(node => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nodeId"] = node.TryGetValue("nodeId", out var nodeId) ? nodeId?.ToString() : null,
                    ["parentId"] = node.TryGetValue("parentId", out var parentId) ? parentId?.ToString() : null,
                    ["kind"] = node.TryGetValue("kind", out var kind) ? kind?.ToString() : null,
                    ["name"] = node.TryGetValue("name", out var name) ? name?.ToString() : null,
                    ["sizeBytes"] = node.TryGetValue("sizeBytes", out var sizeBytes) ? sizeBytes?.ToString() : null
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
                var nodeTable = new TomlTable();

                if (row.TryGetValue("nodeId", out var nodeId) && nodeId is not null) nodeTable["nodeId"] = nodeId.ToString()!;
                if (row.TryGetValue("parentId", out var parentId) && parentId is not null) nodeTable["parentId"] = parentId.ToString()!;
                if (row.TryGetValue("kind", out var kind) && kind is not null) nodeTable["kind"] = kind.ToString()!;
                if (row.TryGetValue("name", out var name) && name is not null) nodeTable["name"] = name.ToString()!;
                if (row.TryGetValue("sizeBytes", out var sizeBytes) && sizeBytes is not null) nodeTable["sizeBytes"] = sizeBytes.ToString()!;

                array.Add(nodeTable);
            }

            table["nodes"] = array;

            var text = TomlSerializer.Serialize(table);
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
