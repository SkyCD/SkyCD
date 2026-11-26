using System;
using System.IO;
using SkyCD.Plugin.Feature.GenDesc.Parsing;

namespace SkyCD.Plugin.Feature.GenDesc.Models;

/// <summary>The user template directory shared by the menu commands, the Options page and the CLI.</summary>
public static class GenDescTemplatePaths
{
    /// <summary>%APPDATA%/SkyCD/gendesc/templates.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SkyCD",
        "gendesc",
        "templates");

    /// <summary>Loads the built-ins plus the user templates; a missing directory is not an error.</summary>
    public static TemplateStore LoadStore(string? directory = null) =>
        TemplateStore.Load(directory ?? DefaultDirectory);

    /// <summary>Seeds every built-in template on first run, then loads the store.</summary>
    public static TemplateStore LoadAndSeed(string? directory = null)
    {
        var target = directory ?? DefaultDirectory;
        try
        {
            Directory.CreateDirectory(target);
            LoadStore(target).SeedUserTemplates(target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A read-only location still leaves the built-ins selectable.
        }

        return LoadStore(target);
    }
}
