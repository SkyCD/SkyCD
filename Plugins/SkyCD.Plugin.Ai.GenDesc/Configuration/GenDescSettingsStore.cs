using System;
using System.IO;
using System.Text.Json;
using SkyCD.Plugin.Ai.GenDesc.Exceptions;
using SkyCD.Plugin.Ai.GenDesc.Models;

namespace SkyCD.Plugin.Ai.GenDesc.Configuration;

/// <summary>
/// Reads and writes the GenDesc JSON settings and resolves the default on-disk locations. A missing settings
/// file is reported as the safe default (generation disabled), so simply installing the plugin never triggers
/// network traffic; the user must create the file to opt in.
/// </summary>
public sealed class GenDescSettingsStore
{
    private const long MaxConfigurationBytes = 65536;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string configurationPath;

    public GenDescSettingsStore(string configurationPath) => this.configurationPath = configurationPath;

    /// <summary>The default settings file the shipped plugin reads, under the SkyCD application-data folder.</summary>
    public static string DefaultConfigurationPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SkyCD", "ai-gendesc.json");

    /// <summary>The default directory the user edits templates in; created on first use.</summary>
    public static string DefaultTemplateDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SkyCD", "gendesc", "templates");

    /// <summary>Loads the settings and validates them, throwing <see cref="GenDescConfigurationException"/> when unusable.</summary>
    public GenDescSettings Load()
    {
        var info = new FileInfo(configurationPath);
        if (!info.Exists) return new GenDescSettings();
        if (info.Length > MaxConfigurationBytes)
            throw new GenDescConfigurationException("configuration exceeds 64 KiB");

        GenDescSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<GenDescSettings>(File.ReadAllText(configurationPath), SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new GenDescConfigurationException($"configuration is not valid JSON ({exception.Path ?? "document"})");
        }
        catch (IOException exception)
        {
            throw new GenDescConfigurationException($"configuration cannot be read: {exception.GetType().Name}");
        }
        catch (UnauthorizedAccessException)
        {
            throw new GenDescConfigurationException("configuration cannot be read: access denied");
        }

        if (settings is null) throw new GenDescConfigurationException("configuration is empty");
        settings.Validate();
        return settings;
    }

    /// <summary>Writes settings as indented JSON, creating the parent directory when needed.</summary>
    public void Save(GenDescSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var directory = Path.GetDirectoryName(configurationPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(configurationPath, JsonSerializer.Serialize(settings, SerializerOptions));
    }

    /// <summary>The directory templates are loaded from and seeded into, honoring a configuration override.</summary>
    public string TemplateDirectory(GenDescSettings settings) =>
        string.IsNullOrWhiteSpace(settings.TemplateDirectory) ? DefaultTemplateDirectory() : settings.TemplateDirectory!;
}
