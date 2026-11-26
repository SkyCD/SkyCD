namespace SkyCD.Plugin.Metadata.Html.Models;

/// <summary>One bounded line of preview text; a heading line is drawn larger and bold.</summary>
public sealed record HtmlPreviewLine(string Text, bool IsHeading);
