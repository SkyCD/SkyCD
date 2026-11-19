using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SkyCD.Presentation.Scanning;

public enum SftpHostKeyState
{
    Unknown = 0,

    Trusted = 1,

    Changed = 2,
}

public sealed record SftpHostKeyInfo(string Fingerprint, string KeyName)
{
    public static string FormatFingerprint(string fingerPrintSha256) => "SHA256:" + fingerPrintSha256;
}

public sealed record SftpHostKeyRecord(string Host, int Port, string Fingerprint, string KeyName, DateTime TrustedUtc);

/// <summary>
/// File-backed store of explicitly confirmed SFTP host keys. A host key is trusted only when it
/// has an entry here; entries are created exclusively by explicit user confirmation.
/// </summary>
public sealed class SftpHostKeyStore
{
    private const string AppDirectoryName = "SkyCD";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string filePath;
    private readonly object gate = new();

    public SftpHostKeyStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        this.filePath = filePath;
    }

    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppDirectoryName,
        "sftp-known-hosts.json");

    public static SftpHostKeyStore Default { get; } = new(DefaultFilePath);

    public SftpHostKeyRecord? Find(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        lock (gate)
        {
            foreach (var record in ReadFile())
            {
                if (record.Port == port && string.Equals(record.Host, host, StringComparison.OrdinalIgnoreCase))
                {
                    return record;
                }
            }

            return null;
        }
    }

    public void Trust(string host, int port, SftpHostKeyInfo key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.Fingerprint);

        lock (gate)
        {
            var records = new List<SftpHostKeyRecord>();
            foreach (var record in ReadFile())
            {
                if (record.Port == port && string.Equals(record.Host, host, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                records.Add(record);
            }

            records.Add(new SftpHostKeyRecord(host, port, key.Fingerprint, key.KeyName, DateTime.UtcNow));
            WriteFile(records);
        }
    }

    public bool Remove(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        lock (gate)
        {
            var records = new List<SftpHostKeyRecord>();
            var removed = false;
            foreach (var record in ReadFile())
            {
                if (record.Port == port && string.Equals(record.Host, host, StringComparison.OrdinalIgnoreCase))
                {
                    removed = true;
                    continue;
                }

                records.Add(record);
            }

            if (removed)
            {
                WriteFile(records);
            }

            return removed;
        }
    }

    private List<SftpHostKeyRecord> ReadFile()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return [];
            }

            var document = JsonSerializer.Deserialize<StoreDocument>(File.ReadAllText(filePath), SerializerOptions);
            return document?.Entries is null ? [] : new List<SftpHostKeyRecord>(document.Entries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable store is an empty store: nothing is trusted until it is confirmed again.
            return [];
        }
    }

    private void WriteFile(List<SftpHostKeyRecord> records)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new StoreDocument { Entries = records }, SerializerOptions));
        File.Move(temporaryPath, filePath, overwrite: true);
    }

    private sealed class StoreDocument
    {
        public List<SftpHostKeyRecord>? Entries { get; set; }
    }
}
