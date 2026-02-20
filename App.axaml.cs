using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using SkyCD.ViewModels;
using SkyCD.Views;
using System.Linq;
using System.Globalization;
using SkyCD.Services;
using SkyCD.Tools;
using System.IO;
using System;

namespace SkyCD
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // apply saved theme on startup
            ApplySavedTheme();

            // initialize localization - default to English
            var baseDir = AppContext.BaseDirectory ?? ".";
            var dataDir = Path.Combine(baseDir, "Data");
            // Convert any existing YAML translations to XLIFF and remove old YAML files
            try
            {
                YamlToXliffConverter.ConvertAllYamlToXliff(dataDir);
                // remove original .yml/.yaml after conversion
                if (Directory.Exists(dataDir))
                {
                    foreach (var f in Directory.GetFiles(dataDir, "*.yml").Concat(Directory.GetFiles(dataDir, "*.yaml")))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
                // also check base data folder
                var baseData = Path.Combine(AppContext.BaseDirectory ?? ".", "Data");
                if (Directory.Exists(baseData))
                {
                    foreach (var f in Directory.GetFiles(baseData, "*.yml").Concat(Directory.GetFiles(baseData, "*.yaml")))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
            }
            catch { }
            System.Diagnostics.Debug.WriteLine($"Localization: baseDir={baseDir}, dataDir={dataDir}");
            // determine requested language from saved settings or system UI culture
            var loc = LocalizationService.Instance;
            var savedLang = Services.SettingsService.Current.SelectedLanguage;
            var systemLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            var dataDirForLookup = dataDir;

            // if savedLang is empty, use systemLang
            var requested = string.IsNullOrEmpty(savedLang) ? systemLang : savedLang;
            System.Diagnostics.Debug.WriteLine($"Localization: requested language={requested}");

            var selected = loc.SetLanguage(requested, dataDirForLookup);
            // persist chosen language
            Services.SettingsService.Current.SelectedLanguage = selected;
            Services.SettingsService.Save();
            System.Diagnostics.Debug.WriteLine($"Localization: active language={selected}");

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
                // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
                DisableAvaloniaDataAnnotationValidation();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(),
                };
            }
            base.OnFrameworkInitializationCompleted();
        }

        public void ApplySavedTheme()
        {
            try
            {
                var theme = Services.SettingsService.Current.SelectedTheme ?? "System";
                // use Application.RequestedThemeVariant to switch between Light/Dark/Default
                // set explicit variant (Default, Light, Dark) by string compare
                if (theme.Equals("Light", StringComparison.OrdinalIgnoreCase))
                {
                    this.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
                }
                else if (theme.Equals("Dark", StringComparison.OrdinalIgnoreCase))
                {
                    this.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
                }
                else
                {
                    this.RequestedThemeVariant = null; // Default / follow system
                }
            }
            catch { }
        }

        private void DisableAvaloniaDataAnnotationValidation()
        {
            // Get an array of plugins to remove
            var dataValidationPluginsToRemove =
                BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

            // remove each entry found
            foreach (var plugin in dataValidationPluginsToRemove)
            {
                BindingPlugins.DataValidators.Remove(plugin);
            }
        }
    }
}