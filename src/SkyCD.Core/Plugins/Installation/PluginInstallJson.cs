using System;
using System.IO;
using System.Text.Json;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Installation.Exceptions;

namespace SkyCD.Core.Plugins.Installation;

/// <summary>
/// Durable JSON documents for receipts and pending operations. Every document is written with an unknown
/// enum-free schema, a length check before reading, and a corrupt document is treated as absent or failed
/// instead of crashing startup.
/// </summary>
internal static class PluginInstallJson
{
    private const long MaxDocumentBytes = 256 * 1024;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static void Write<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, Options));
        File.Move(temporaryPath, path, overwrite: true);
    }

    /// <summary>Reads a document, returning <c>null</c> when it is missing, oversized or malformed.</summary>
    public static T? TryRead<T>(string path)
        where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var info = new FileInfo(path);
            if (info.Length is <= 0 or > MaxDocumentBytes)
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static PluginInstallReceipt ReadReceipt(string pluginId, string receiptPath)
    {
        var receipt = TryRead<PluginInstallReceipt>(receiptPath)
                      ?? throw new PluginInstallDestinationException(pluginId,
                          "the existing install receipt is unreadable");

        if (receipt.SchemaVersion > PluginInstallReceipt.CurrentSchemaVersion)
        {
            throw new PluginInstallDestinationException(pluginId,
                $"the install receipt schema '{receipt.SchemaVersion}' is newer than this host");
        }

        return receipt;
    }
}
