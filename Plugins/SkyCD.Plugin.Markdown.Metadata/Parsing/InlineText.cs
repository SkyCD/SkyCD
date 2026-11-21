using System.Text;
using Markdig.Syntax.Inlines;

namespace SkyCD.Plugin.Markdown.Metadata.Parsing;

/// <summary>Reads the visible text of an inline tree, collapsing runs of whitespace.</summary>
internal static class InlineText
{
    public static string Extract(ContainerInline? container)
    {
        var builder = new StringBuilder();
        Append(container, builder);
        return Normalize(builder.ToString());
    }

    private static void Append(ContainerInline? container, StringBuilder builder)
    {
        if (container is null)
        {
            return;
        }

        foreach (var inline in container)
        {
            Append(inline, builder);
        }
    }

    private static void Append(Inline inline, StringBuilder builder)
    {
        switch (inline)
        {
            case LiteralInline literal:
                builder.Append(literal.Content);
                break;
            case CodeInline code:
                builder.Append(code.Content);
                break;
            case LineBreakInline:
            case HtmlInline:
                builder.Append(' ');
                break;
            case HtmlEntityInline entity:
                builder.Append(entity.Transcoded);
                break;
            case AutolinkInline autolink:
                builder.Append(autolink.Url);
                break;
            case ContainerInline container:
                Append(container, builder);
                break;
        }
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}
