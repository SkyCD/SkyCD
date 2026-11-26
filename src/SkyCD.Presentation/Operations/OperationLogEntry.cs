using System;
using System.IO;

namespace SkyCD.Presentation.Operations;

public sealed record OperationLogEntry
{
    public OperationLogEntry(
        OperationLogLevel level,
        string message,
        string? stage = null,
        string? itemPath = null,
        string? details = null,
        DateTimeOffset? timestamp = null)
    {
        Timestamp = timestamp ?? DateTimeOffset.Now;
        Level = level;
        Stage = string.IsNullOrWhiteSpace(stage) ? null : SecretSanitizer.Sanitize(stage);
        ItemPath = string.IsNullOrWhiteSpace(itemPath) ? null : SecretSanitizer.Sanitize(itemPath);
        Message = SecretSanitizer.Sanitize(message);
        Details = string.IsNullOrWhiteSpace(details) ? null : SecretSanitizer.Sanitize(details);
    }

    public DateTimeOffset Timestamp { get; init; }
    public OperationLogLevel Level { get; init; }
    public string Message { get; init; }
    public string? Stage { get; init; }
    public string? ItemPath { get; init; }
    public string? Details { get; init; }

    private string? scanRoot;

    /// <summary>
    /// The scan root the item belongs to. Relativizes <see cref="CompactMessage"/> paths to
    /// <c>.\Name</c>; entries that carry no root keep absolute paths. Sanitized like
    /// <see cref="ItemPath"/> because remote roots can embed credentials.
    /// </summary>
    public string? ScanRoot
    {
        get => scanRoot;
        init => scanRoot = string.IsNullOrWhiteSpace(value) ? null : SecretSanitizer.Sanitize(value);
    }

    public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss.fff");

    public string DisplayText
    {
        get
        {
            var stagePrefix = string.IsNullOrEmpty(Stage) ? string.Empty : $"[{Stage}] ";
            var itemSuffix = string.IsNullOrEmpty(ItemPath) ? string.Empty : $" ({ItemPath})";
            var detailsSuffix = string.IsNullOrEmpty(Details) ? string.Empty : $" - {Details}";
            return $"[{FormattedTimestamp}] [{Level}] {stagePrefix}{Message}{itemSuffix}{detailsSuffix}";
        }
    }

    /// <summary>
    /// Short clock for the dialog log: milliseconds are dropped because a scan emits
    /// hundreds of lines per second and sub-second precision carries no signal.
    /// </summary>
    public string CompactTimestamp => Timestamp.ToString("HH:mm:ss");

    /// <summary>
    /// Level marker for the fixed-width dialog column. Emoji render through the Segoe UI Emoji
    /// fallback of the monospace stack, so the column width comes from a layout token instead
    /// of padding. All eight glyphs ship in Segoe UI Emoji on Windows 10/11.
    /// </summary>
    public string LevelGlyph => Level switch
    {
        OperationLogLevel.Debug => "🔍",
        OperationLogLevel.Information => "ℹ️",
        OperationLogLevel.StageChanged => "⏳",
        OperationLogLevel.Warning => "⚠️",
        OperationLogLevel.Error => "❌",
        OperationLogLevel.ItemProcessed => "✅",
        OperationLogLevel.ItemSkipped => "↪️",
        OperationLogLevel.ItemFailed => "🛑",
        _ => "ℹ️",
    };

    /// <summary>
    /// One-line dialog rendering: <c>message: path</c> with no stage segment, the path exactly
    /// once at the end (relative to <see cref="ScanRoot"/> when the item nests under it), skip
    /// and failure reasons stated once, and multi-line details (exception stacks) reduced to
    /// their first line. The full-fidelity <see cref="DisplayText"/> stays the Copy-log format.
    /// </summary>
    public string CompactMessage
    {
        get
        {
            var body = Level switch
            {
                OperationLogLevel.ItemSkipped => "Skipped due " + TrimReasonPunctuation(ExtractSkipReason()),
                OperationLogLevel.ItemFailed => "Failed due " + TrimReasonPunctuation(ExtractFailureCause()),
                _ => AppendShortDetails(Message),
            };
            return AppendDisplayPath(body);
        }
    }

    /// <summary>Full compact line: <c>[HH:mm:ss] &lt;glyph&gt; message: path</c>.</summary>
    public string CompactText => $"[{CompactTimestamp}] {LevelGlyph} {CompactMessage}";

    private string ExtractSkipReason()
    {
        // Producers pass the bare reason as details ("Skipped: {reason}" + reason); prefer it.
        if (!string.IsNullOrWhiteSpace(Details))
        {
            return Details;
        }

        const string prefix = "Skipped:";
        if (Message.StartsWith(prefix, StringComparison.Ordinal))
        {
            var rest = Message.Substring(prefix.Length).TrimStart();
            if (!string.IsNullOrEmpty(rest))
            {
                return rest;
            }
        }

        return Message;
    }

    private string ExtractFailureCause()
    {
        // ReportFailed stores exception.ToString() in details: keep only the first line
        // (exception type + message) so a stack trace never lands on the dialog line.
        if (!string.IsNullOrWhiteSpace(Details))
        {
            return FirstLine(Details);
        }

        const string prefix = "Failed:";
        if (Message.StartsWith(prefix, StringComparison.Ordinal))
        {
            var rest = Message.Substring(prefix.Length).TrimStart();
            if (!string.IsNullOrEmpty(rest))
            {
                return rest;
            }
        }

        return Message;
    }

    private string AppendShortDetails(string body)
    {
        if (string.IsNullOrWhiteSpace(Details))
        {
            return body;
        }

        var shortDetails = FirstLine(Details);
        if (string.IsNullOrEmpty(shortDetails))
        {
            return body;
        }

        // Details that repeat the message (ReportSkipped-style) add no signal on screen.
        if (body.Contains(shortDetails, StringComparison.Ordinal))
        {
            return body;
        }

        return body + " - " + shortDetails;
    }

    private string AppendDisplayPath(string body)
    {
        if (string.IsNullOrWhiteSpace(ItemPath))
        {
            return body;
        }

        var displayPath = ToDisplayPath(ItemPath, ScanRoot);
        if (body.EndsWith(ItemPath, StringComparison.Ordinal))
        {
            return body.Substring(0, body.Length - ItemPath.Length) + displayPath;
        }

        var name = Path.GetFileName(ItemPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrEmpty(name)
            && body.EndsWith(name, StringComparison.Ordinal)
            && !body.EndsWith(displayPath, StringComparison.Ordinal))
        {
            return body.Substring(0, body.Length - name.Length) + displayPath;
        }

        if (body.Contains(ItemPath, StringComparison.Ordinal))
        {
            return body.Replace(ItemPath, displayPath, StringComparison.Ordinal);
        }

        return body + ": " + displayPath;
    }

    private static string ToDisplayPath(string itemPath, string? scanRoot)
    {
        if (!string.IsNullOrWhiteSpace(scanRoot))
        {
            var root = scanRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (itemPath.Equals(scanRoot, StringComparison.OrdinalIgnoreCase)
                || itemPath.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                return itemPath;
            }

            if (itemPath.Length > root.Length
                && itemPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && (itemPath[root.Length] == Path.DirectorySeparatorChar
                    || itemPath[root.Length] == Path.AltDirectorySeparatorChar))
            {
                return "." + Path.DirectorySeparatorChar + itemPath.Substring(root.Length + 1);
            }
        }

        return itemPath;
    }

    private static string FirstLine(string value)
    {
        var newline = value.IndexOf('\n');
        if (newline >= 0)
        {
            value = value.Substring(0, newline);
        }

        return value.TrimEnd('\r').Trim();
    }

    private static string TrimReasonPunctuation(string reason)
    {
        var trimmed = reason.Trim();
        if (trimmed.EndsWith(".", StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
        }

        return trimmed;
    }
}
