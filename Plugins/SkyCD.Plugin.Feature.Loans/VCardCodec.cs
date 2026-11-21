using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SkyCD.Plugin.Feature.Loans;

/// <summary>
/// Reads and writes the subset of vCard 2.1/3.0 used by contact exchange.
/// Supports line folding, escaped separators, structured names/addresses, and
/// quoted-printable values.
/// </summary>
public static class VCardCodec
{
    public static IReadOnlyList<BorrowerDocument> Parse(string text)
    {
        var contacts = new List<BorrowerDocument>();
        BorrowerDocument? current = null;
        foreach (var line in Unfold(text))
        {
            if (line.Equals("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                current = new BorrowerDocument { Id = Guid.NewGuid().ToString("N") };
                continue;
            }

            if (line.Equals("END:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null)
                {
                    Normalize(current);
                    if (current.Name.Length > 0)
                    {
                        contacts.Add(current);
                    }
                }

                current = null;
                continue;
            }

            if (current is not null && TrySplit(line, out var name, out var parameters, out var value))
            {
                Apply(current, name, parameters, value);
            }
        }

        return contacts;
    }

    public static string Write(IEnumerable<BorrowerDocument> contacts)
    {
        var builder = new StringBuilder();
        foreach (var contact in contacts)
        {
            AppendLine(builder, "BEGIN:VCARD");
            AppendLine(builder, "VERSION:3.0");
            AppendProperty(builder, "FN", FirstNonEmpty(contact.DisplayName, contact.Name));
            AppendStructured(builder, "N", Pad([contact.Name], 5));
            if (!string.IsNullOrWhiteSpace(contact.Phone))
            {
                AppendProperty(builder, "TEL;TYPE=VOICE", contact.Phone);
            }

            if (!string.IsNullOrWhiteSpace(contact.Fax))
            {
                AppendProperty(builder, "TEL;TYPE=FAX", contact.Fax);
            }

            if (!string.IsNullOrWhiteSpace(contact.Email))
            {
                AppendProperty(builder, "EMAIL", contact.Email);
            }

            if (!string.IsNullOrWhiteSpace(contact.WebPage))
            {
                AppendProperty(builder, "URL", contact.WebPage);
            }

            if (!string.IsNullOrWhiteSpace(contact.Messenger))
            {
                AppendProperty(builder, "IMPP", contact.Messenger);
            }

            if (!string.IsNullOrWhiteSpace(contact.Address))
            {
                AppendStructured(builder, "ADR", Pad(["", "", contact.Address], 7));
            }

            if (!string.IsNullOrWhiteSpace(contact.Company))
            {
                AppendStructured(builder, "ORG", [contact.Company]);
            }

            if (!string.IsNullOrWhiteSpace(contact.Position))
            {
                AppendProperty(builder, "TITLE", contact.Position);
            }

            if (!string.IsNullOrWhiteSpace(contact.Comments))
            {
                AppendProperty(builder, "NOTE", contact.Comments);
            }

            AppendLine(builder, "END:VCARD");
        }

        return builder.ToString();
    }

    private static IEnumerable<string> Unfold(string text)
    {
        var rawLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var lines = new List<string>();
        foreach (var rawLine in rawLines)
        {
            if (rawLine.Length == 0)
            {
                continue;
            }

            if (lines.Count > 0 && rawLine[0] is ' ' or '\t')
            {
                lines[^1] += rawLine[1..];
                continue;
            }

            if (lines.Count > 0 && lines[^1].EndsWith('=') && IsQuotedPrintable(lines[^1]))
            {
                lines[^1] = lines[^1][..^1] + rawLine;
                continue;
            }

            lines.Add(rawLine);
        }

        return lines;
    }

    private static bool TrySplit(string line, out string name, out string parameters, out string value)
    {
        name = string.Empty;
        parameters = string.Empty;
        value = string.Empty;
        var colon = line.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        var head = line[..colon];
        value = line[(colon + 1)..];
        var groupSeparator = head.LastIndexOf('.');
        if (groupSeparator >= 0)
        {
            head = head[(groupSeparator + 1)..];
        }

        var parameterSeparator = head.IndexOf(';');
        if (parameterSeparator < 0)
        {
            name = head;
        }
        else
        {
            name = head[..parameterSeparator];
            parameters = head[(parameterSeparator + 1)..];
        }

        return name.Length > 0;
    }

    private static void Apply(BorrowerDocument contact, string name, string parameters, string value)
    {
        switch (name.ToUpperInvariant())
        {
            case "FN":
                contact.DisplayName = Decode(parameters, value);
                break;
            case "N":
                var formatted = FormatName(SplitEscaped(value, ';'));
                if (formatted.Length > 0 && contact.Name.Length == 0)
                {
                    contact.Name = formatted;
                }

                break;
            case "NOTE":
                contact.Comments = AppendText(contact.Comments, Decode(parameters, value));
                break;
            case "TEL":
                if (parameters.Contains("FAX", StringComparison.OrdinalIgnoreCase))
                {
                    contact.Fax = AppendText(contact.Fax, Decode(parameters, value));
                }
                else
                {
                    contact.Phone = AppendText(contact.Phone, Decode(parameters, value));
                }

                break;
            case "EMAIL":
                contact.Email = AppendText(contact.Email, Decode(parameters, value));
                break;
            case "URL":
                contact.WebPage = AppendText(contact.WebPage, Decode(parameters, value));
                break;
            case "IMPP":
            case "X-MESSENGER":
            case "X-SKYPE":
                contact.Messenger = AppendText(contact.Messenger, Decode(parameters, value));
                break;
            case "ADR":
                contact.Address = AppendText(contact.Address, FormatAddress(SplitEscaped(value, ';')));
                break;
            case "ORG":
                contact.Company = AppendText(contact.Company, FormatOrganization(SplitEscaped(value, ';')));
                break;
            case "TITLE":
                contact.Position = AppendText(contact.Position, Decode(parameters, value));
                break;
        }
    }

    private static void Normalize(BorrowerDocument contact)
    {
        contact.Name = contact.Name.Trim();
        contact.DisplayName = contact.DisplayName.Trim();
        contact.Comments = contact.Comments.Trim();
        contact.Phone = contact.Phone.Trim();
        contact.Fax = contact.Fax.Trim();
        contact.Email = contact.Email.Trim();
        contact.WebPage = contact.WebPage.Trim();
        contact.Messenger = contact.Messenger.Trim();
        contact.Address = contact.Address.Trim();
        contact.Company = contact.Company.Trim();
        contact.Position = contact.Position.Trim();

        if (contact.Name.Length == 0)
        {
            contact.Name = contact.DisplayName;
        }

        if (contact.DisplayName.Length == 0)
        {
            contact.DisplayName = contact.Name;
        }
    }

    private static string FormatName(IReadOnlyList<string> components)
    {
        var family = Component(components, 0);
        var given = Component(components, 1);
        var prefix = Component(components, 3);
        var suffix = Component(components, 4);
        return string.Join(" ", new[] { prefix, given, family, suffix }.Where(part => part.Length > 0));
    }

    private static string FormatAddress(IReadOnlyList<string> components)
    {
        var parts = Enumerable.Range(0, 7).Select(index => Component(components, index));
        return string.Join(", ", parts.Where(part => part.Length > 0));
    }

    private static string FormatOrganization(IReadOnlyList<string> components)
    {
        var parts = components.Select(part => Unescape(part).Trim());
        return string.Join(", ", parts.Where(part => part.Length > 0));
    }

    private static string Component(IReadOnlyList<string> components, int index) =>
        index < components.Count ? Unescape(components[index]).Trim() : string.Empty;

    private static string[] SplitEscaped(string value, char separator)
    {
        var parts = new List<string>();
        var builder = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (character == '\\' && i + 1 < value.Length)
            {
                builder.Append(character).Append(value[++i]);
                continue;
            }

            if (character == separator)
            {
                parts.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(character);
        }

        parts.Add(builder.ToString());
        return [.. parts];
    }

    private static string Unescape(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                builder.Append(value[i]);
                continue;
            }

            var next = value[++i];
            builder.Append(next switch
            {
                'n' or 'N' => '\n',
                _ => next
            });
        }

        return builder.ToString();
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n")
        .Replace("\r", "\\n");

    private static string Decode(string parameters, string value)
    {
        var decoded = IsQuotedPrintable(parameters) ? DecodeQuotedPrintable(parameters, value) : value;
        return Unescape(decoded).Trim();
    }

    private static bool IsQuotedPrintable(string parameters) =>
        parameters.Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase);

    private static string DecodeQuotedPrintable(string parameters, string value)
    {
        var encoding = parameters.Contains("ISO-8859-1", StringComparison.OrdinalIgnoreCase) ||
                       parameters.Contains("LATIN", StringComparison.OrdinalIgnoreCase)
            ? Encoding.Latin1
            : Encoding.UTF8;
        var bytes = new List<byte>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (character == '=' && i + 2 < value.Length &&
                Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            {
                bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16));
                i += 2;
                continue;
            }

            if (character <= 0xFF)
            {
                bytes.Add((byte)character);
            }
            else
            {
                bytes.AddRange(encoding.GetBytes(character.ToString()));
            }
        }

        return encoding.GetString([.. bytes]);
    }

    private static string AppendText(string existing, string value)
    {
        if (value.Length == 0)
        {
            return existing;
        }

        return existing.Length == 0 ? value : existing + ", " + value;
    }

    private static string FirstNonEmpty(string first, string second) =>
        !string.IsNullOrWhiteSpace(first) ? first : second;

    private static string[] Pad(string[] components, int count)
    {
        if (components.Length >= count)
        {
            return components;
        }

        var padded = new string[count];
        Array.Copy(components, padded, components.Length);
        for (var i = components.Length; i < count; i++)
        {
            padded[i] = string.Empty;
        }

        return padded;
    }

    private static void AppendStructured(StringBuilder builder, string property, IReadOnlyList<string> components) =>
        Fold(builder, property + ":" + string.Join(";", components.Select(Escape)));

    private static void AppendProperty(StringBuilder builder, string property, string value) =>
        Fold(builder, property + ":" + Escape(value));

    private static void AppendLine(StringBuilder builder, string line) => builder.Append(line).Append("\r\n");

    private static void Fold(StringBuilder builder, string line)
    {
        const int limit = 75;
        while (line.Length > limit)
        {
            builder.Append(line, 0, limit).Append("\r\n ");
            line = line[limit..];
        }

        AppendLine(builder, line);
    }
}
