using System;
using System.Text.Json;
using System.Xml;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>Parser settings that keep every engine document bounded and inert.</summary>
internal static class GameDevDocuments
{
    /// <summary>Strict JSON: no comments, no trailing commas, depth capped.</summary>
    internal static readonly JsonDocumentOptions StrictJson = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64
    };

    /// <summary>JSON5-like documents used by Unreal project descriptors and comments in some engine files.</summary>
    internal static readonly JsonDocumentOptions TolerantJson = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 64
    };

    /// <summary>XML reader settings: no DTDs, no external entities, no resolver.</summary>
    internal static XmlReaderSettings XmlSettings { get; } = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        MaxCharactersFromEntities = 0,
        CloseInput = true
    };

    internal static XmlReader CreateXml(string text)
    {
        var settings = XmlSettings;
        return XmlReader.Create(new System.IO.StringReader(text), settings);
    }

    /// <summary>Advances past declarations and whitespace to the first element; false when there is none.</summary>
    internal static bool ReadRootElement(XmlReader reader)
    {
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryParseJson(string text, out JsonDocument document, bool tolerant = false)
    {
        try
        {
            document = JsonDocument.Parse(text, tolerant ? TolerantJson : StrictJson);
            return true;
        }
        catch (JsonException)
        {
            document = null!;
            return false;
        }
        catch (ArgumentException)
        {
            document = null!;
            return false;
        }
    }

    internal static string? GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    internal static long? GetInteger(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
    }

    internal static bool? GetBoolean(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    internal static int? CountArray(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return Math.Min(value.GetArrayLength(), 1_000_000);
    }
}
