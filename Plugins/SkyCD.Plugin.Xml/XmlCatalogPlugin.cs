using System.Globalization;
using System.Text;
using System.Xml;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.Xml;

public sealed class XmlCatalogPlugin : IPlugin, IFileFormatPluginCapability
{
    private const string NamespaceUri = "urn:skycd:catalog";
    private const string SchemaVersion = "1.0";

    public string Id => "skycd.plugin.xml";
    public string Name => "XML Format Plugin";
    public Version Version => new(1, 0, 0);
    public Version MinHostVersion => new(3, 0, 0);
    public string Description => "Example plugin that exposes XML file format support.";

    public IReadOnlyCollection<FileFormatDescriptor> SupportedFormats =>
    [
        new FileFormatDescriptor(
            "skycd-xml",
            "SkyCD XML",
            [".xml"],
            CanRead: true,
            CanWrite: true,
            MimeType: "application/xml")
    ];

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            using var reader = XmlReader.Create(request.Source, settings);
            var document = new XmlDocument
            {
                XmlResolver = null
            };
            document.Load(reader);

            var root = document.DocumentElement;
            if (root is null || root.LocalName != "catalog" || root.NamespaceURI != NamespaceUri)
            {
                return Task.FromResult(new FileFormatReadResult
                {
                    Success = false,
                    Error = "Invalid XML root element. Expected skycd:catalog."
                });
            }

            var version = root.GetAttribute("schemaVersion");
            if (!version.Equals(SchemaVersion, StringComparison.Ordinal))
            {
                return Task.FromResult(new FileFormatReadResult
                {
                    Success = false,
                    Error = $"Unsupported schema version '{version}'."
                });
            }

            var rows = new List<Dictionary<string, object?>>();
            foreach (XmlElement nodeElement in root.GetElementsByTagName("node", NamespaceUri))
            {
                rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["NodeId"] = nodeElement.GetAttribute("nodeId"),
                    ["ParentId"] = nodeElement.GetAttribute("parentId"),
                    ["Kind"] = nodeElement.GetAttribute("kind"),
                    ["Name"] = nodeElement.GetAttribute("name"),
                    ["SizeBytes"] = nodeElement.GetAttribute("sizeBytes")
                });
            }

            return Task.FromResult(new FileFormatReadResult
            {
                Success = true,
                Payload = rows
            });
        }
        catch (Exception exception)
        {
            return Task.FromResult(new FileFormatReadResult
            {
                Success = false,
                Error = exception.Message
            });
        }
    }

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = ResolveRows(request.Payload)
                .OrderBy(row => ParseSortKey(row, "NodeId"))
                .ThenBy(row => GetValue(row, "Name"), StringComparer.Ordinal)
                .ToList();

            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                Async = true
            };

            await using var writer = XmlWriter.Create(request.Target, settings);
            await writer.WriteStartDocumentAsync();
            await writer.WriteStartElementAsync("skycd", "catalog", NamespaceUri);
            await writer.WriteAttributeStringAsync(null, "schemaVersion", null, SchemaVersion);

            foreach (var row in rows)
            {
                await writer.WriteStartElementAsync("skycd", "node", NamespaceUri);
                await writer.WriteAttributeStringAsync(null, "nodeId", null, GetValue(row, "NodeId"));
                await writer.WriteAttributeStringAsync(null, "parentId", null, GetValue(row, "ParentId"));
                await writer.WriteAttributeStringAsync(null, "kind", null, GetValue(row, "Kind"));
                await writer.WriteAttributeStringAsync(null, "name", null, GetValue(row, "Name"));
                await writer.WriteAttributeStringAsync(null, "sizeBytes", null, GetValue(row, "SizeBytes"));
                await writer.WriteEndElementAsync();
            }

            await writer.WriteEndElementAsync();
            await writer.WriteEndDocumentAsync();
            await writer.FlushAsync();
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

    private static List<Dictionary<string, object?>> ResolveRows(object? payload)
    {
        return payload as List<Dictionary<string, object?>>
               ?? throw new InvalidOperationException("XML payload must be a list of row dictionaries.");
    }

    private static string GetValue(IReadOnlyDictionary<string, object?> row, string key)
    {
        return row.TryGetValue(key, out var value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
    }

    private static long ParseSortKey(IReadOnlyDictionary<string, object?> row, string key)
    {
        var value = GetValue(row, key);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : long.MaxValue;
    }
}
