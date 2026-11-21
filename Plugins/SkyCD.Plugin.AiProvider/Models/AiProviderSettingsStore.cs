using System;
using System.IO;
using System.Text.Json;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.AiProvider.Exceptions;

namespace SkyCD.Plugin.AiProvider.Models;

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
            return JsonSerializer.Deserialize<AiProviderSettings>(File.ReadAllText(ConfigurationPath),
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 16 })
                   ?? throw new AiProviderException("configuration is invalid");
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
