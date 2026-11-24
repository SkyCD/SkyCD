using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Ai.GenDesc.Models;

namespace SkyCD.Plugin.Ai.GenDesc.Parsing;

/// <summary>
/// The fixed whitelist of helper functions a template may call. Every helper is pure text transformation or a
/// fact lookup; nothing here reaches the filesystem, network, or reflection, so an edited template cannot run
/// arbitrary code. Unknown function names are rejected by the compiler before these are ever consulted.
/// </summary>
internal static class TemplateFunctions
{
    private static readonly Dictionary<string, Func<TemplateValue[], DescriptionFacts, TemplateValue>> Registry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["isset"] = (a, f) => TemplateValue.Bool(f.Has(Name(a))),
            ["has"] = (a, f) => TemplateValue.Bool(f.Has(Name(a))),
            ["default"] = (a, f) => TemplateValue.Text(f.Has(Name(a)) ? f.Get(Name(a)) : TextArg(a, 1)),
            ["get"] = (a, f) => TemplateValue.Text(f.Get(Name(a))),
            ["count"] = (a, f) => TemplateValue.Number(ListItems(f.Get(Name(a))).Length),
            ["join"] = (a, f) => TemplateValue.Text(string.Join(a.Length > 1 ? TextArg(a, 1) : ", ", ListItems(f.Get(Name(a))))),
            ["nl"] = (_, _) => TemplateValue.Text("\n"),
            ["newline"] = (_, _) => TemplateValue.Text("\n"),
            ["twriteline"] = (a, _) => TemplateValue.Text(TextArg(a, 0) + "\n"),
            ["len"] = (a, _) => TemplateValue.Number(TextArg(a, 0).Length),
            ["trim"] = (a, _) => TemplateValue.Text(TextArg(a, 0).Trim()),
            ["upper"] = (a, _) => TemplateValue.Text(TextArg(a, 0).ToUpperInvariant()),
            ["lower"] = (a, _) => TemplateValue.Text(TextArg(a, 0).ToLowerInvariant()),
            ["cut"] = (a, _) => TemplateValue.Text(Cut(TextArg(a, 0), a.Length > 1 ? (int)a[1].AsNumber() : 0)),
            ["replace"] = (a, _) => a.Length >= 3
                ? TemplateValue.Text(TextArg(a, 0).Replace(TextArg(a, 1), TextArg(a, 2), StringComparison.Ordinal))
                : TemplateValue.Text(TextArg(a, 0)),
            ["contains"] = (a, _) => TemplateValue.Bool(a.Length >= 2 && TextArg(a, 0).Contains(TextArg(a, 1), StringComparison.OrdinalIgnoreCase)),
            ["b"] = (a, _) => Wrap(a, "b"),
            ["i"] = (a, _) => Wrap(a, "i"),
            ["u"] = (a, _) => Wrap(a, "u"),
            ["s"] = (a, _) => Wrap(a, "s"),
            ["url"] = Url,
            ["year"] = (a, f) => TemplateValue.Text(YearOf(f))
        };

    public static bool IsKnown(string name) => Registry.ContainsKey(name);

    public static TemplateValue Invoke(string name, TemplateValue[] args, DescriptionFacts facts) =>
        Registry[name](args, facts);

    private static TemplateValue Url(TemplateValue[] a, DescriptionFacts _)
    {
        var text = TextArg(a, 0);
        var href = a.Length > 1 ? TextArg(a, 1) : text;
        if (text.Length == 0 && href.Length == 0) return TemplateValue.Text(string.Empty);
        return TemplateValue.Text(a.Length > 1 ? $"[url={href}]{text}[/url]" : $"[url]{href}[/url]");
    }

    private static TemplateValue Wrap(TemplateValue[] a, string tag)
    {
        var inner = TextArg(a, 0);
        return inner.Length == 0 ? TemplateValue.Text(string.Empty) : TemplateValue.Text($"[{tag}]{inner}[/{tag}]");
    }

    private static string YearOf(DescriptionFacts facts)
    {
        foreach (var key in new[] { "Year", "ReleaseYear", "Released", "Premiered", "FirstAired" })
        {
            var value = facts.Get(key);
            if (value.Length == 0) continue;
            var match = System.Text.RegularExpressions.Regex.Match(value, "(1[89]\\d{2}|20\\d{2}|21\\d{2})");
            if (match.Success) return match.Value;
        }
        return string.Empty;
    }

    private static string[] ListItems(string value) => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(['|', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Cut(string value, int length) =>
        length > 0 && value.Length > length ? value[..length].TrimEnd() : value;

    private static string Name(TemplateValue[] args) => args.Length > 0 ? args[0].AsText() : string.Empty;

    private static string TextArg(TemplateValue[] args, int index) =>
        args.Length > index ? args[index].Display() : string.Empty;
}
