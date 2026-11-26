using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using SkyCD.Plugin.OnlineMetadata.Models;

namespace SkyCD.Plugin.Feature.GenDesc.Parsing;

/// <summary>
/// The fixed whitelist of helper functions a template may call. Every helper is pure text transformation or a
/// fact lookup; nothing here reaches the filesystem, network, or reflection, so an edited template cannot run
/// arbitrary code. Filters use the same registry, which is how the template syntax stays easy to extend.
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
            ["br"] = (_, _) => TemplateValue.Text("\n"),
            ["newline"] = (_, _) => TemplateValue.Text("\n"),
            ["twriteline"] = (a, _) => TemplateValue.Text(TextArg(a, 0) + "\n"),
            ["len"] = (a, _) => TemplateValue.Number(TextArg(a, 0).Length),
            ["trim"] = (a, _) => TemplateValue.Text(TextArg(a, 0).Trim()),
            ["upper"] = (a, _) => TemplateValue.Text(TextArg(a, 0).ToUpperInvariant()),
            ["lower"] = (a, _) => TemplateValue.Text(TextArg(a, 0).ToLowerInvariant()),
            ["capitalize"] = (a, _) => TemplateValue.Text(Capitalize(TextArg(a, 0))),
            ["titlecase"] = (a, _) => TemplateValue.Text(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(TextArg(a, 0).ToLowerInvariant())),
            ["slug"] = (a, _) => TemplateValue.Text(Slug(TextArg(a, 0))),
            ["cut"] = (a, _) => TemplateValue.Text(Cut(TextArg(a, 0), a.Length > 1 ? (int)a[1].AsNumber() : 0)),
            ["truncate"] = (a, _) => TemplateValue.Text(Cut(TextArg(a, 0), a.Length > 1 ? (int)a[1].AsNumber() : 0)),
            ["substr"] = (a, _) => TemplateValue.Text(Substring(TextArg(a, 0), a.Length > 1 ? (int)a[1].AsNumber() : 0,
                a.Length > 2 ? (int)a[2].AsNumber() : int.MaxValue)),
            ["replace"] = (a, _) => a.Length >= 3
                ? TemplateValue.Text(TextArg(a, 0).Replace(TextArg(a, 1), TextArg(a, 2), StringComparison.Ordinal))
                : TemplateValue.Text(TextArg(a, 0)),
            ["regexreplace"] = (a, _) => a.Length >= 3
                ? TemplateValue.Text(SafeRegexReplace(TextArg(a, 0), TextArg(a, 1), TextArg(a, 2)))
                : TemplateValue.Text(TextArg(a, 0)),
            ["contains"] = (a, _) => TemplateValue.Bool(a.Length >= 2 && TextArg(a, 0).Contains(TextArg(a, 1), StringComparison.OrdinalIgnoreCase)),
            ["pad"] = (a, _) => TemplateValue.Text(Pad(TextArg(a, 0), a.Length > 1 ? (int)a[1].AsNumber() : 0,
                a.Length > 2 ? TextArg(a, 2) : " ", left: false)),
            ["padleft"] = (a, _) => TemplateValue.Text(Pad(TextArg(a, 0), a.Length > 1 ? (int)a[1].AsNumber() : 0,
                a.Length > 2 ? TextArg(a, 2) : " ", left: true)),
            ["padright"] = (a, _) => TemplateValue.Text(Pad(TextArg(a, 0), a.Length > 1 ? (int)a[1].AsNumber() : 0,
                a.Length > 2 ? TextArg(a, 2) : " ", left: false)),
            ["filesize"] = (a, _) => TemplateValue.Text(FormatFileSize(a.Length > 0 ? a[0].AsNumber() : double.NaN)),
            ["date"] = (a, _) => TemplateValue.Text(FormatDate(TextArg(a, 0), a.Length > 1 ? TextArg(a, 1) : "yyyy-MM-dd")),
            ["b"] = (a, _) => Wrap(a, "b"),
            ["i"] = (a, _) => Wrap(a, "i"),
            ["u"] = (a, _) => Wrap(a, "u"),
            ["s"] = (a, _) => Wrap(a, "s"),
            ["quote"] = (a, _) => Wrap(a, "quote"),
            ["code"] = (a, _) => Wrap(a, "code"),
            ["spoiler"] = (a, _) => Wrap(a, "spoiler"),
            ["color"] = (a, _) => a.Length >= 2 && TextArg(a, 0).Length > 0
                ? TemplateValue.Text($"[color={TextArg(a, 1)}]{TextArg(a, 0)}[/color]")
                : TemplateValue.Text(TextArg(a, 0)),
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
            var match = Regex.Match(value, "(1[89]\\d{2}|20\\d{2}|21\\d{2})");
            if (match.Success) return match.Value;
        }
        return string.Empty;
    }

    private static string[] ListItems(string value) => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(['|', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Cut(string value, int length) =>
        length > 0 && value.Length > length ? value[..length].TrimEnd() : value;

    private static string Substring(string value, int start, int length)
    {
        if (value.Length == 0 || start < 0 || start >= value.Length || length <= 0) return string.Empty;
        return value.Substring(start, Math.Min(length, value.Length - start));
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string Slug(string value)
    {
        var slug = Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-");
        return slug.Trim('-');
    }

    private static string SafeRegexReplace(string value, string pattern, string replacement)
    {
        try
        {
            return Regex.Replace(value, pattern, replacement, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException)
        {
            return value;
        }
        catch (RegexMatchTimeoutException)
        {
            return value;
        }
    }

    private static string Pad(string value, int width, string padding, bool left)
    {
        if (width <= value.Length || padding.Length == 0) return value;
        var fill = new string(padding[0], width - value.Length);
        return left ? fill + value : value + fill;
    }

    private static string FormatFileSize(double bytes)
    {
        if (double.IsNaN(bytes) || double.IsInfinity(bytes) || bytes < 0) return string.Empty;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString(unit == 0 ? "0" : "0.#", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    private static string FormatDate(string value, string format)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(format)) return value;
        DateTimeOffset parsed;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            parsed = DateTimeOffset.FromUnixTimeSeconds(unix);
        }
        else if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
        {
            return value;
        }

        try
        {
            return parsed.ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return value;
        }
    }

    private static string Name(TemplateValue[] args) => args.Length > 0 ? args[0].AsText() : string.Empty;

    private static string TextArg(TemplateValue[] args, int index) =>
        args.Length > index ? args[index].Display() : string.Empty;
}
