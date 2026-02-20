using System;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SkyCD.Services
{
    // view/list display modes saved in settings
    public enum ListViewMode : byte
    {
        Tiles = 0,
        SmallIcons = 1,
        LargeIcons = 2,
        List = 3,
        Details = 4,
    }
    public class Settings
    {
        public string SelectedLanguage { get; set; } = "en";
        // Color theme: "White", "Black", or "System" (auto)
        // store theme as enum name from Avalonia.Styling.ThemeVariant (Default/Light/Dark)
        public string SelectedTheme { get; set; } = "Default";
        public bool ShowStatusBar { get; set; } = true;
        public ListViewMode ViewMode { get; set; } = ListViewMode.LargeIcons;
        // column widths for details (pixels)
        public double DetailsNameColumnWidth { get; set; } = 300.0; // Width for the name column in details view
        public double DetailsTypeColumnWidth { get; set; } = 150.0; // Width for the type column in details view
        public double DetailsDateColumnWidth { get; set; } = 200.0; // Width for the date column in details view
        public double DetailsSizeColumnWidth { get; set; } = 100.0; // Width for the size column in details view
    }

    public static class SettingsService
    {
        private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SkyCD");
        private static readonly string SettingsFile = Path.Combine(Dir, "settings.yml");

        public static Settings Current { get; private set; } = Load();

        private static Settings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var yaml = File.ReadAllText(SettingsFile);
                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(UnderscoredNamingConvention.Instance)
                        .IgnoreUnmatchedProperties()
                        .Build();

                    return deserializer.Deserialize<Settings>(yaml) ?? new Settings();
                }

            }
            catch { }

            return new Settings();
        }

        public static void Save()
        {
            try
            {
                if (!Directory.Exists(Dir))
                    Directory.CreateDirectory(Dir);

                var serializer = new SerializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .Build();

                var yaml = serializer.Serialize(Current);
                File.WriteAllText(SettingsFile, yaml);
            }
            catch { }
        }

        // Reload settings from disk (useful to refresh Current after external changes)
        public static void Reload()
        {
            Current = Load();
        }
    }
}
