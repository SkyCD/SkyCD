using System.Text;

namespace SkyCD.Plugin.Ai.GenDesc.Parsing;

/// <summary>
/// Tidies rendered text so a template's layout does not depend on how conditional blocks were spaced in the
/// source: trailing spaces are removed per line, consecutive blank lines collapse to one, and leading or
/// trailing blank lines are dropped. Interior structure and author-intended blank lines survive.
/// </summary>
internal static class TemplateOutput
{
    public static string Normalize(string rendered)
    {
        if (string.IsNullOrEmpty(rendered)) return string.Empty;
        var lines = rendered.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var builder = new StringBuilder();
        var pendingBlank = false;
        var started = false;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                pendingBlank = started;
                continue;
            }
            if (pendingBlank) builder.Append('\n');
            if (started) builder.Append('\n');
            builder.Append(line);
            started = true;
            pendingBlank = false;
        }
        return builder.ToString();
    }
}
