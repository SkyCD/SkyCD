using System;
using System.IO;
using System.Text.Json;

namespace SkyCD.Plugin.Accounts.JetBrains.Models;

/// <summary>JSON-file backed JetBrains ownership snapshot. Writes are atomic.</summary>
public sealed class JetBrainsOwnershipStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string filePath;
    private readonly object gate = new();

    public JetBrainsOwnershipStore(string? filePath = null)
    {
        this.filePath = string.IsNullOrWhiteSpace(filePath) ? DefaultPath() : filePath;
    }

    public static string DefaultPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SkyCD",
            "jetbrains.json");
    }

    public JetBrainsOwnershipSnapshot Load()
    {
        lock (gate)
        {
            if (!File.Exists(filePath))
            {
                return new JetBrainsOwnershipSnapshot();
            }

            using var stream = File.OpenRead(filePath);
            return JsonSerializer.Deserialize<JetBrainsOwnershipSnapshot>(stream, Options)
                   ?? new JetBrainsOwnershipSnapshot();
        }
    }

    public void Save(JetBrainsOwnershipSnapshot snapshot)
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
