using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.Markdown;

public sealed class MarkdownCatalogExportPlugin : IPlugin, IFileFormatPluginCapability
{
    public PluginDescriptor Descriptor => new(
        "skycd.plugin.markdown",
        "Markdown Export Plugin",
        new Version(1, 0, 0),
        new Version(3, 0, 0),
        "Example plugin that exports catalog payloads to Markdown.");

    public IReadOnlyCollection<FileFormatDescriptor> SupportedFormats =>
    [
        new FileFormatDescriptor(
            "skycd-md",
            "SkyCD Markdown Export",
            [".md"],
            CanRead: false,
            CanWrite: true,
            MimeType: "text/markdown")
    ];

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatReadResult
        {
            Success = false,
            Error = "Markdown export plugin is write-only."
        });
    }

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = request.Payload as List<Dictionary<string, object?>>
                ?? throw new InvalidOperationException("Markdown export payload must be a list of row dictionaries.");

            var orderedRows = rows
                .OrderBy(row => row.TryGetValue("nodeId", out var nodeId) ? nodeId?.ToString() : null, StringComparer.Ordinal)
                .ToList();

            var byParent = orderedRows
                .GroupBy(row => GetValue(row, "parentId"))
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

            var builder = new StringBuilder();
            builder.AppendLine("# SkyCD Catalog Export");
            builder.AppendLine();
            builder.AppendLine("## Nodes");

            WriteChildren(builder, byParent, parentId: string.Empty, depth: 0);

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

    private static void WriteChildren(
        StringBuilder builder,
        IReadOnlyDictionary<string, List<Dictionary<string, object?>>> byParent,
        string parentId,
        int depth)
    {
        if (!byParent.TryGetValue(parentId, out var children))
        {
            return;
        }

        foreach (var row in children)
        {
            var indent = new string(' ', depth * 2);
            var kind = EscapeMarkdown(GetValue(row, "kind"));
            var name = EscapeMarkdown(GetValue(row, "name"));
            var nodeId = EscapeMarkdown(GetValue(row, "nodeId"));
            var sizeBytes = EscapeMarkdown(GetValue(row, "sizeBytes"));

            builder.AppendLine($"{indent}- `{kind}` {name} (`nodeId={nodeId}`) (`sizeBytes={sizeBytes}`)");
            WriteChildren(builder, byParent, GetValue(row, "nodeId"), depth + 1);
        }
    }

    private static string GetValue(IReadOnlyDictionary<string, object?> row, string key)
    {
        return row.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;
    }

    private static string EscapeMarkdown(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal);
    }
}
