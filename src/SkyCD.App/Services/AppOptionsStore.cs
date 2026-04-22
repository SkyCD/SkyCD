using System;
using System.IO;
using System.Text.Json;
using SkyCD.App.Models;

namespace SkyCD.App.Services;

public sealed class AppOptionsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string optionsFilePath;

    public AppOptionsStore()
    {
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var optionsDirectory = Path.Combine(appDataRoot, "SkyCD");
        optionsFilePath = Path.Combine(optionsDirectory, "options.json");
    }

    public AppOptions Load()
    {
        if (!File.Exists(optionsFilePath)) return new AppOptions();

        try
        {
            var json = File.ReadAllText(optionsFilePath);
            return JsonSerializer.Deserialize<AppOptions>(json) ?? new AppOptions();
        }
        catch
        {
            return new AppOptions();
        }
    }

    public void Save(AppOptions options)
    {
        var directory = Path.GetDirectoryName(optionsFilePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(options, SerializerOptions);
        File.WriteAllText(optionsFilePath, json);
    }
}