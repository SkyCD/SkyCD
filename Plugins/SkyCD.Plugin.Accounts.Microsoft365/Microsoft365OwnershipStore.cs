using System;
using System.IO;
using System.Text.Json;

namespace SkyCD.Plugin.Accounts.Microsoft365;

/// <summary>JSON-file backed Microsoft 365 ownership snapshot. Writes are atomic.</summary>
public sealed class Microsoft365OwnershipStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string filePath;
    private readonly object gate = new();

    public Microsoft365OwnershipStore(string? filePath = null)
    {
        this.filePath = string.IsNullOrWhiteSpace(filePath) ? DefaultPath() : filePath;
    }

    public static string DefaultPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SkyCD",
            "microsoft365.json");
    }

    public Microsoft365OwnershipSnapshot Load()
    {
        lock (gate)
        {
            if (!File.Exists(filePath))
            {
                return new Microsoft365OwnershipSnapshot();
            }

            using var stream = File.OpenRead(filePath);
            return JsonSerializer.Deserialize<Microsoft365OwnershipSnapshot>(stream, Options)
                   ?? new Microsoft365OwnershipSnapshot();
        }
    }

    public void Save(Microsoft365OwnershipSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (gate)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(snapshot, Options));
            File.Move(tempPath, filePath, overwrite: true);
        }
    }
}
