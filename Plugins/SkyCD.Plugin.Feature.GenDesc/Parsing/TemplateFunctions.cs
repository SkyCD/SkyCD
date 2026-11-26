using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Scriban;

namespace SkyCD.Plugin.Feature.GenDesc.Parsing;

/// <summary>
/// The fixed whitelist of helper functions a GenDesc template may call. Every helper is pure text
/// transformation or a fact lookup; nothing here reaches the filesystem, network, or reflection, so an
/// edited template cannot run arbitrary code. Methods are imported into the Scriban scope lowercased, so
/// <c>Join</c> is called as <c>join</c>; helpers that read facts take the fact name and work in both the
/// direct form (<c>join("Genre", ", ")</c>) and the pipe form (<c>"Genre" | join ", "</c>).
/// </summary>
internal static class TemplateFunctions
{
    private static readonly HashSet<string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "isset", "has", "default", "get", "count", "join", "nl", "br", "newline", "twriteline", "len",
        "trim", "upper", "lower", "capitalize", "titlecase", "slug", "cut", "truncate", "substr",
        "replace", "regexreplace", "contains", "pad", "padleft", "padright", "filesize", "date", "b",
        "i", "u", "s", "quote", "code", "spoiler", "color", "url", "year"
    };

    internal static bool IsKnown(string name) => KnownNames.Contains(name);

    public static bool Isset(TemplateContext context, object? name) => Has(context, name);

    public static bool Has(TemplateContext context, object? name) =>
        !string.IsNullOrWhiteSpace(Fact(context, Name(context, name)));

    public static string Default(TemplateContext context, object? name, object? fallback) =>
        Has(context, name) ? Fact(context, Name(context, name)) : Str(fallback);

    public static string Get(TemplateContext context, object? name) => Fact(context, Name(context, name));

    public static int Count(TemplateContext context, object? name) =>
        ListItems(Fact(context, Name(context, name))).Length;

    public static string Join(TemplateContext context, object? name, object? separator) =>
        string.Join(Str(separator).Length == 0 ? ", " : Str(separator),
            ListItems(Fact(context, Name(context, name))));

    public static string Nl() => "\n";

    public static string Br() => "\n";

    public static string Newline() => "\n";

    public static string Twriteline(object? value) => Str(value) + "\n";

    public static int Len(object? value) => Str(value).Length;

    public static string Trim(object? value) => Str(value).Trim();

    public static string Upper(object? value) => Str(value).ToUpperInvariant();

    public static string Lower(object? value) => Str(value).ToLowerInvariant();

    public static string Capitalize(object? value) =>
        Str(value) is { Length: > 0 } text ? char.ToUpperInvariant(text[0]) + text[1..] : string.Empty;

    public static string Titlecase(object? value) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Str(value).ToLowerInvariant());

    public static string Slug(object? value)
    {
        var slug = Regex.Replace(Str(value).Trim().ToLowerInvariant(), "[^a-z0-9]+", "-");
        return slug.Trim('-');
    }

    public static string Cut(object? value, object? length) => Truncate(value, length);

    public static string Truncate(object? value, object? length)
    {
        var text = Str(value);
        var max = Int(length);
        return max > 0 && text.Length > max ? text[..max].TrimEnd() : text;
    }

    public static string Substr(object? value, object? start, object? length)
    {
        var text = Str(value);
        var from = Int(start);
        var count = length is null ? int.MaxValue : Int(length);
        if (text.Length == 0 || from < 0 || from >= text.Length || count <= 0) return string.Empty;
        return text.Substring(from, Math.Min(count, text.Length - from));
    }

    public static string Replace(object? value, object? oldValue, object? newValue) =>
        Str(value).Replace(Str(oldValue), Str(newValue), StringComparison.Ordinal);

    public static string Regexreplace(object? value, object? pattern, object? replacement)
    {
        try
        {
            return Regex.Replace(Str(value), Str(pattern), Str(replacement), RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException)
        {
            return Str(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return Str(value);
        }
    }

    public static bool Contains(object? value, object? part) =>
        Str(part).Length > 0 && Str(value).Contains(Str(part), StringComparison.OrdinalIgnoreCase);

    public static string Pad(object? value, object? width, object? padding) =>
        PadValue(Str(value), Int(width), Str(padding), left: false);

    public static string Padleft(object? value, object? width, object? padding) =>
        PadValue(Str(value), Int(width), Str(padding), left: true);

    public static string Padright(object? value, object? width, object? padding) =>
        PadValue(Str(value), Int(width), Str(padding), left: false);

    public static string Filesize(object? value)
    {
        var bytes = Num(value);
        if (double.IsNaN(bytes) || double.IsInfinity(bytes) || bytes < 0) return string.Empty;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var scaled = bytes;
        var unit = 0;
        while (scaled >= 1024 && unit < units.Length - 1)
        {
            scaled /= 1024;
            unit++;
        }

        return scaled.ToString(unit == 0 ? "0" : "0.#", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    public static string Date(object? value, object? format)
    {
        var text = Str(value);
        var pattern = Str(format);
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(pattern)) return text;
        DateTimeOffset parsed;
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            parsed = DateTimeOffset.FromUnixTimeSeconds(unix);
        }
        else if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                     out parsed))
        {
            return text;
        }

        try
        {
            return parsed.ToString(pattern, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return text;
        }
    }

    public static string B(object? value) => Wrap(Str(value), "b");

    public static string I(object? value) => Wrap(Str(value), "i");

    public static string U(object? value) => Wrap(Str(value), "u");

    public static string S(object? value) => Wrap(Str(value), "s");

    public static string Quote(object? value) => Wrap(Str(value), "quote");

    public static string Code(object? value) => Wrap(Str(value), "code");

    public static string Spoiler(object? value) => Wrap(Str(value), "spoiler");

    public static string Color(object? value, object? color)
    {
        var inner = Str(value);
        return inner.Length == 0 ? string.Empty : $"[color={Str(color)}]{inner}[/color]";
    }

    public static string Url(object? value, object? href)
    {
        var text = Str(value);
        var link = href is null ? text : Str(href);
        if (text.Length == 0 && link.Length == 0) return string.Empty;
        return href is null ? $"[url]{link}[/url]" : $"[url={link}]{text}[/url]";
    }

    public static string Year(TemplateContext context)
    {
        foreach (var key in new[] { "Year", "ReleaseYear", "Released", "Premiered", "FirstAired" })
        {
            var value = Fact(context, key);
            if (value.Length == 0) continue;
            var match = Regex.Match(value, "(1[89]\\d{2}|20\\d{2}|21\\d{2})");
            if (match.Success) return match.Value;
        }

        return string.Empty;
    }

    private static string Wrap(string inner, string tag) =>
        inner.Length == 0 ? string.Empty : $"[{tag}]{inner}[/{tag}]";

    private static string PadValue(string value, int width, string padding, bool left)
    {
        if (width <= value.Length || padding.Length == 0) return value;
        var fill = new string(padding[0], width - value.Length);
        return left ? fill + value : value + fill;
    }

    private static string[] ListItems(string value) => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(['|', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Fact(TemplateContext context, string name)
    {
        if (name.Length == 0 || context.CurrentGlobal is not { } global) return string.Empty;
        return global.TryGetValue(context, default, name, out var value) && value is not null
            ? Str(value)
            : string.Empty;
    }

    private static string Name(TemplateContext context, object? name) =>
        name is null ? string.Empty : Str(name);

    private static string Str(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        bool flag => flag ? "true" : "false",
        double number => FormatNumber(number),
        float single => FormatNumber(single),
        long integer => integer.ToString(CultureInfo.InvariantCulture),
        int integer => integer.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

    private static double Num(object? value) => value switch
    {
        null => double.NaN,
        double number => number,
        float single => single,
        bool flag => flag ? 1 : 0,
        _ => double.TryParse(Str(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : double.NaN
    };

    private static int Int(object? value)
    {
        var number = Num(value);
        return double.IsNaN(number) || double.IsInfinity(number) ? 0 : (int)number;
    }

    private static string FormatNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return string.Empty;
        return value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.########", CultureInfo.InvariantCulture);
    }
}
