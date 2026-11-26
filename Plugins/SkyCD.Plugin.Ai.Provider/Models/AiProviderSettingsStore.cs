using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Ai.Provider.Exceptions;

namespace SkyCD.Plugin.Ai.Provider.Models;

/// <summary>Atomic JSON persistence for provider settings with a bounded size.</summary>
public sealed class AiProviderSettingsStore
{
    public const int MaximumBytes = 65536;

    public AiProviderSettingsStore(string configurationPath)
    {
        ConfigurationPath = Path.GetFullPath(configurationPath);
    }

    public string ConfigurationPath { get; }

    public AiProviderSettings Read()
    {
        var info = new FileInfo(ConfigurationPath);
        if (!info.Exists)
        {
            return new AiProviderSettings();
        }

        if (info.Length > MaximumBytes)
        {
            throw new AiProviderException("configuration exceeds 64 KiB");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(ConfigurationPath),
                new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            var settings = root.Deserialize<AiProviderSettings>(
                               new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                           ?? throw new AiProviderException("configuration is invalid");
            if (!string.Equals(settings.ProviderId, "lmstudio", StringComparison.OrdinalIgnoreCase))
            {
                return settings;
            }

            // Preserve the old preset defaults for fields omitted from an existing file.
            return settings with
            {
                ProviderId = "custom",
                Port = root.EnumerateObject().Any(property =>
                    string.Equals(property.Name, "port", StringComparison.OrdinalIgnoreCase)) ? settings.Port : 1234,
                Model = root.EnumerateObject().Any(property =>
                    string.Equals(property.Name, "model", StringComparison.OrdinalIgnoreCase))
                    ? settings.Model
                    : "google/gemma-4-e2b"
            };
        }
        catch (JsonException)
        {
            throw new AiProviderException("configuration is invalid");
        }
    }

    public void Write(AiProviderSettings settings)
    {
        var content = JsonSerializer.SerializeToUtf8Bytes(settings,
            new JsonSerializerOptions { WriteIndented = true });
        if (content.Length > MaximumBytes)
        {
            throw new AiProviderException("configuration exceeds 64 KiB");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ConfigurationPath)!);
        var temporaryPath = ConfigurationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(temporaryPath, fileOptions))
            {
                stream.Write(content);
            }

            File.Move(temporaryPath, ConfigurationPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
