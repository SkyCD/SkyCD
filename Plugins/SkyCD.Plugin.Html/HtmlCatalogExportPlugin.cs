using System.Net;
using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.Html;

public sealed class HtmlCatalogExportPlugin : IPlugin, IFileFormatPluginCapability
{
    public IReadOnlyCollection<FileFormatDescriptor> SupportedFormats =>
    [
        new(
            "skycd-html",
            "SkyCD HTML Export",
            [".html"],
            false,
            true,
            "text/html")
    ];

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatReadResult
        {
            Success = false,
            Error = "HTML export plugin is write-only."
        });
    }

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = request.Payload as List<Dictionary<string, object?>>
                       ?? throw new InvalidOperationException(
                           "HTML export payload must be a list of row dictionaries.");

            var orderedRows = rows
                .OrderBy(row => row.TryGetValue("nodeId", out var nodeId) ? nodeId?.ToString() : null,
                    StringComparer.Ordinal)
                .ToList();

            var builder = new StringBuilder();
            builder.AppendLine("<!doctype html>");
            builder.AppendLine("<html lang=\"en\">");
            builder.AppendLine("<head><meta charset=\"utf-8\"><title>SkyCD Catalog Export</title></head>");
            builder.AppendLine("<body>");
            builder.AppendLine("<h1>SkyCD Catalog Export</h1>");
            builder.AppendLine("<nav><ul>");

            foreach (var row in orderedRows)
            {
                var nodeId = WebUtility.HtmlEncode(GetValue(row, "nodeId"));
                var name = WebUtility.HtmlEncode(GetValue(row, "name"));
                builder.AppendLine($"<li><a href=\"#node-{nodeId}\">{name}</a></li>");
            }

            builder.AppendLine("</ul></nav>");
            builder.AppendLine("<main>");

            foreach (var row in orderedRows)
            {
                var nodeId = WebUtility.HtmlEncode(GetValue(row, "nodeId"));
                var parentId = WebUtility.HtmlEncode(GetValue(row, "parentId"));
                var kind = WebUtility.HtmlEncode(GetValue(row, "kind"));
                var name = WebUtility.HtmlEncode(GetValue(row, "name"));
                var sizeBytes = WebUtility.HtmlEncode(GetValue(row, "sizeBytes"));

                builder.AppendLine($"<section id=\"node-{nodeId}\">");
                builder.AppendLine($"<h2>{name}</h2>");
                builder.AppendLine("<ul>");
                builder.AppendLine($"<li>Kind: {kind}</li>");
                builder.AppendLine($"<li>ParentId: {parentId}</li>");
                builder.AppendLine($"<li>SizeBytes: {sizeBytes}</li>");
                builder.AppendLine("</ul>");
                builder.AppendLine("</section>");
            }

            builder.AppendLine("</main>");
            builder.AppendLine("</body>");
            builder.AppendLine("</html>");

            await using var writer = new StreamWriter(request.Target, new UTF8Encoding(false), leaveOpen: true);
            await writer.WriteAsync(builder.ToString().AsMemory(), cancellationToken);
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
        "skycd.plugin.html",
        "HTML Export Plugin",
        new Version(1, 0, 0),
        new Version(3, 0, 0),
        "Example plugin that exports catalog payloads to HTML.");

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

    private static string GetValue(IReadOnlyDictionary<string, object?> row, string key)
    {
        return row.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;
    }
}