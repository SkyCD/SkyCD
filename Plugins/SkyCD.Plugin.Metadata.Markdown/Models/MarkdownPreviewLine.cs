namespace SkyCD.Plugin.Metadata.Markdown.Models;

/// <summary>One bounded line of preview text; a heading line is drawn larger and bold.</summary>
public sealed record MarkdownPreviewLine(string Text, bool IsHeading);
