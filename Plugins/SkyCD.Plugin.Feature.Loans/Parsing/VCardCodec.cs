using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SkyCD.Plugin.Feature.Loans.Models;
using vCardLib.Deserialization;
using vCardLib.Enums;
using vCardLib.Models;
using vCardLib.Serialization;

namespace SkyCD.Plugin.Feature.Loans.Parsing;

/// <summary>
/// Maps vCard contact exchange onto <see cref="BorrowerDocument"/> using vCardLib,
/// normalizing the legacy 2.1 quirks the library does not tolerate: missing
/// VERSION, grouped properties, unterminated cards and quoted-printable values.
/// </summary>
public static class VCardCodec
{
    public static IReadOnlyList<BorrowerDocument> Parse(string text)
    {
        var contacts = new List<BorrowerDocument>();
        foreach (var card in SplitCards(Unfold(text)))
        {
            foreach (var parsed in ParseCard(card))
            {
                var contact = Map(parsed);
                Normalize(contact);
                if (contact.Name.Length > 0)
                {
                    contacts.Add(contact);
                }
            }
        }

        return contacts;
    }

    public static string Write(IEnumerable<BorrowerDocument> contacts) =>
        vCardSerializer.Serialize(contacts.Select(ToCard).ToArray(), vCardVersion.v3);

    private static IEnumerable<string> SplitCards(IEnumerable<string> lines)
    {
        List<string>? current = null;
        foreach (var line in lines)
        {
            if (line.Equals("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                current = ["BEGIN:VCARD"];
                continue;
            }

            if (line.Equals("END:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                if (current is null)
                {
                    continue;
                }

                current.Add("END:VCARD");
                yield return BuildCard(current);
                current = null;
                continue;
            }

            if (current is not null && TryNormalizeProperty(line, out var property))
            {
                current.Add(property);
            }
        }
    }

    private static string BuildCard(IReadOnlyList<string> lines)
    {
        // vCardLib resolves fields with StartsWith, so an N line must precede
        // NOTE or NICKNAME and a missing N must be supplied explicitly.
        var version = lines.FirstOrDefault(line => IsProperty(line, "VERSION")) ?? "VERSION:3.0";
        var name = lines.FirstOrDefault(line => IsProperty(line, "N")) ?? "N:;;;;";

        var builder = new StringBuilder();
        builder.Append("BEGIN:VCARD\r\n");
        builder.Append(version).Append("\r\n");
        builder.Append(name).Append("\r\n");
        foreach (var line in lines)
        {
            if (IsProperty(line, "BEGIN") || IsProperty(line, "END") ||
                IsProperty(line, "VERSION") || IsProperty(line, "N"))
            {
                continue;
            }

            builder.Append(line).Append("\r\n");
        }

        builder.Append("END:VCARD\r\n");
        return builder.ToString();
    }

    private static bool IsProperty(string line, string name)
    {
        var colon = line.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        var head = line[..colon];
        var parameterSeparator = head.IndexOf(';');
        var propertyName = parameterSeparator >= 0 ? head[..parameterSeparator] : head;
        return propertyName.Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeProperty(string line, out string normalized)
    {
        normalized = line;
        var colon = line.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        var head = line[..colon];
        var value = line[(colon + 1)..];
        var groupSeparator = head.LastIndexOf('.');
        if (groupSeparator >= 0)
        {
            head = head[(groupSeparator + 1)..];
        }

        var name = head;
        var parameters = string.Empty;
        var parameterSeparator = head.IndexOf(';');
        if (parameterSeparator >= 0)
        {
            name = head[..parameterSeparator];
            parameters = head[(parameterSeparator + 1)..];
        }

        if (name.Length == 0)
        {
            return false;
        }

        if (IsQuotedPrintable(parameters))
        {
            value = EscapeNewlines(DecodeQuotedPrintable(parameters, value));
            parameters = RemoveParameters(parameters, "ENCODING", "CHARSET");
        }

        normalized = parameters.Length == 0
            ? name + ":" + value
            : name + ";" + parameters + ":" + value;
        return true;
    }

    private static string RemoveParameters(string parameters, params string[] names) =>
        string.Join(
            ";",
            parameters
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(parameter => !names.Any(name => parameter.StartsWith(name, StringComparison.OrdinalIgnoreCase))));

    private static string EscapeNewlines(string value) =>
        value.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

    private static IReadOnlyList<vCard> ParseCard(string text)
    {
        try
        {
            return vCardDeserializer.FromContent(text).ToList();
        }
        catch (Exception)
        {
            // vCardLib rejects cards the legacy importer must skip instead of failing the file.
            return [];
        }
    }

    private static BorrowerDocument Map(vCard card)
    {
        var contact = new BorrowerDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = FormatName(card.Name),
            DisplayName = Clean(card.FormattedName),
            Comments = Clean(card.Note),
            WebPage = Clean(card.Url?.Value),
            Position = Clean(card.Title),
            Company = FormatOrganization(card.Organization)
        };

        foreach (var phone in card.PhoneNumbers)
        {
            var number = Clean(phone.Number);
            if (number.Length == 0)
            {
                continue;
            }

            if (phone.Type.HasFlag(TelephoneNumberType.Fax))
            {
                contact.Fax = AppendText(contact.Fax, number);
            }
            else
            {
                contact.Phone = AppendText(contact.Phone, number);
            }
        }

        foreach (var email in card.EmailAddresses)
        {
            contact.Email = AppendText(contact.Email, Clean(email.Value));
        }

        foreach (var address in card.Addresses)
        {
            contact.Address = AppendText(contact.Address, FormatAddress(address));
        }

        foreach (var field in card.CustomFields)
        {
            var key = field.Key.Split(';', ':')[0].Trim();
            if (!IsMessengerKey(key))
            {
                continue;
            }

            var raw = field.Key + ":" + field.Value;
            var separator = raw.IndexOf(':');
            if (separator >= 0 && separator < raw.Length - 1)
            {
                contact.Messenger = AppendText(contact.Messenger, Clean(raw[(separator + 1)..]));
            }
        }

        return contact;
    }

    private static vCard ToCard(BorrowerDocument contact)
    {
        var card = new vCard(vCardVersion.v3)
        {
            FormattedName = NullIfEmpty(FirstNonEmpty(contact.DisplayName, contact.Name)),
            Name = new Name(contact.Name, null, null, null, null),
            Note = NullIfEmpty(contact.Comments),
            Title = NullIfEmpty(contact.Position),
            Url = string.IsNullOrWhiteSpace(contact.WebPage) ? null : new Url(contact.WebPage),
            Organization = string.IsNullOrWhiteSpace(contact.Company)
                ? null
                : new Organization(contact.Company, null, null)
        };

        if (!string.IsNullOrWhiteSpace(contact.Phone))
        {
            card.PhoneNumbers.Add(new TelephoneNumber(contact.Phone, TelephoneNumberType.Voice));
        }

        if (!string.IsNullOrWhiteSpace(contact.Fax))
        {
            card.PhoneNumbers.Add(new TelephoneNumber(contact.Fax, TelephoneNumberType.Fax));
        }

        if (!string.IsNullOrWhiteSpace(contact.Email))
        {
            card.EmailAddresses.Add(new EmailAddress(contact.Email));
        }

        if (!string.IsNullOrWhiteSpace(contact.Address))
        {
            card.Addresses.Add(new Address(string.Empty, string.Empty, contact.Address, string.Empty, string.Empty, string.Empty, string.Empty));
        }

        if (!string.IsNullOrWhiteSpace(contact.Messenger))
        {
            card.CustomFields.Add(new KeyValuePair<string, string>("IMPP", contact.Messenger));
        }

        return card;
    }

    private static string FormatName(Name? name)
    {
        if (name is not { } value)
        {
            return string.Empty;
        }

        return Join([value.HonorificPrefix, value.GivenName, value.FamilyName, value.HonorificSuffix], " ");
    }

    private static string FormatAddress(Address address) =>
        Join(
            [
                address.PostOfficeBox,
                address.ApartmentOrSuiteNumber,
                address.StreetAddress,
                address.CityOrLocality,
                address.StateOrProvinceOrRegion,
                address.PostalOrZipCode,
                address.Country
            ],
            ", ");

    private static string FormatOrganization(Organization? organization)
    {
        if (organization is not { } value)
        {
            return string.Empty;
        }

        return Join([value.Name, value.PrimaryUnit, value.SecondaryUnit], ", ");
    }

    private static string Join(IEnumerable<string?> parts, string separator) =>
        string.Join(
            separator,
            parts.Select(part => part?.Trim() ?? string.Empty).Where(part => part.Length > 0));

    private static bool IsMessengerKey(string key) =>
        key.Equals("IMPP", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("X-MESSENGER", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("X-SKYPE", StringComparison.OrdinalIgnoreCase);

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

    private static string Clean(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    private static string FirstNonEmpty(string first, string second) =>
        !string.IsNullOrWhiteSpace(first) ? first : second;

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
