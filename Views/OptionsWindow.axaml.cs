using Avalonia.Controls;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using SkyCD.Services;

namespace SkyCD.Views
{
    public partial class OptionsWindow : Window
    {
        private object[] _themeVariants = Array.Empty<object>();

        public OptionsWindow()
        {
            AvaloniaXamlLoader.Load(this);
            // initialize UI
            PopulateThemeList();
            PopulateLanguageList();
            LoadSelections();
            WireButtons();
        }

        // Resolve a flag value (emoji, 2-letter country code, http(s) url or local path)
        private string? ResolveFlagPath(string? flag)
        {
            if (string.IsNullOrWhiteSpace(flag))
                return null;

            var v = flag.Trim();

            // If it's already a URL or existing local file, return as-is
            if (v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || v.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return v;
            if (System.IO.File.Exists(v))
                return v;

            // Prepare Twemoji file name (hex codepoints separated by '-') for country codes or emoji
            string? hexName = null;
            if (Regex.IsMatch(v, "^[A-Za-z]{2}$"))
            {
                var cps = v.ToUpperInvariant().Select(c => 0x1F1E6 + (c - 'A'));
                hexName = string.Join("-", cps.Select(cp => cp.ToString("x")));
            }
            else
            {
                var codepoints = GetCodepoints(v);
                if (codepoints.Count > 0)
                    hexName = string.Join("-", codepoints.Select(cp => cp.ToString("x")));
            }

            if (!string.IsNullOrEmpty(hexName))
            {
                // check for bundled/local Twemoji first
                var localDir = System.IO.Path.Combine(AppContext.BaseDirectory ?? ".", "Data", "twemoji", "72x72");
                var localPath = System.IO.Path.Combine(localDir, hexName + ".png");
                if (System.IO.File.Exists(localPath))
                    return localPath;

                // fallback to CDN
                return $"https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/{hexName}.png";
            }

            return null;
        }

        private static List<int> GetCodepoints(string s)
        {
            var list = new List<int>();
            for (int i = 0; i < s.Length;)
            {
                if (Rune.TryGetRuneAt(s, i, out var rune))
                {
                    // Only include non-control runes
                    if (!char.IsControl((char)rune.Value))
                        list.Add(rune.Value);
                    i += rune.Utf16SequenceLength;
                }
                else
                {
                    i++;
                }
            }
            return list;
        }

        private void OnSave(object? sender, RoutedEventArgs e)
        {
            var themeCombo = this.FindControl<ComboBox>("ThemeCombo");
            var langCombo = this.FindControl<ComboBox>("LanguageCombo");

            var selectedVariantObj = themeCombo.SelectedItem;
            var theme = selectedVariantObj?.ToString() ?? "Default";

            var li = langCombo.SelectedItem as LanguageInfo;
            var lang = li?.Code ?? SettingsService.Current.SelectedLanguage;

            ApplyAndReloadSettings(theme, lang ?? "en");

            // close and recreate main window to refresh localized XAML
            RecreateMainWindowIfNeeded();
            Close();
        }

        private void PopulateThemeList()
        {
            var themeCombo = this.FindControl<ComboBox>("ThemeCombo");
            var themeType = typeof(Avalonia.Styling.ThemeVariant);
            var staticProps = themeType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            _themeVariants = staticProps.Select(p => p.GetValue(null)).Where(v => v != null).ToArray();
            themeCombo.ItemsSource = _themeVariants;
        }

        private void PopulateLanguageList()
        {
            var dataDir = System.IO.Path.Combine(AppContext.BaseDirectory ?? ".", "Data"); // Updated data directory path
            // only use XLIFF files now
            var files = new System.Collections.Generic.List<string>();
            if (System.IO.Directory.Exists(dataDir))
                files.AddRange(System.IO.Directory.GetFiles(dataDir, "*.xlf"));

            var infos = files.Distinct().Select(f =>
            {
                try
                {
                    var doc = System.Xml.Linq.XDocument.Load(f);
                    var fileElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "file");
                    var code = System.IO.Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    string? name = null;
                    string? flag = null;
                    if (fileElem != null)
                    {
                        var header = fileElem.Elements().FirstOrDefault(e => e.Name.LocalName == "header");
                        var notes = header?.Elements().Where(e => e.Name.LocalName == "note");
                        if (notes != null)
                        {
                            foreach (var note in notes)
                            {
                                var cat = note.Attribute("category")?.Value?.ToLowerInvariant();
                                var text = (note.Value ?? string.Empty).Trim();
                                if (!string.IsNullOrEmpty(cat))
                                {
                                    if (cat == "name") name = text;
                                    else if (cat == "flag") flag = text;
                                }
                            }
                        }
                    }
                    if (string.IsNullOrEmpty(name))
                    {
                        try { name = new System.Globalization.CultureInfo(code).DisplayName; } catch { name = code; }
                    }
                    // Resolve flag to a usable image path (Twemoji PNG URL, http(s) or local path)
                    var flagPath = ResolveFlagPath(flag);
                    return new LanguageInfo { Code = code, Name = name ?? code, FlagPath = flagPath };
                }
                catch { return null; }
            }).Where(x => x != null).Cast<LanguageInfo>().ToArray();
            var langCombo = this.FindControl<ComboBox>("LanguageCombo"); // Finding language combo box
            langCombo.ItemsSource = infos; // Setting items source for language combo box
        }

        private void LoadSelections()
        {
            var themeCombo = this.FindControl<ComboBox>("ThemeCombo");
            var selThemeStr = SettingsService.Current.SelectedTheme ?? "Default";
            var match = _themeVariants.OfType<object?>().FirstOrDefault(v => string.Equals(v?.ToString(), selThemeStr, StringComparison.OrdinalIgnoreCase));
            themeCombo.SelectedItem = match;

            var langCombo = this.FindControl<ComboBox>("LanguageCombo");
            var infos = langCombo.ItemsSource as LanguageInfo[] ?? Array.Empty<LanguageInfo>();
            var selCode = SettingsService.Current.SelectedLanguage ?? "en";
            var selInfo = infos.FirstOrDefault(i => string.Equals(i.Code, selCode, StringComparison.OrdinalIgnoreCase));
            langCombo.SelectedItem = selInfo ?? (infos.Length > 0 ? infos[0] : null);
        }

        private void WireButtons()
        {
            var save = this.FindControl<Button>("SaveButton");
            save.Click += OnSave;
            var cancel = this.FindControl<Button>("CancelButton");
            cancel.Click += (_, __) => Close();
        }

        private void ApplyAndReloadSettings(string theme, string lang)
        {
            SettingsService.Current.SelectedTheme = theme;
            SettingsService.Current.SelectedLanguage = lang;
            SettingsService.Save();

            // apply language immediately and persist returned active language
            var selected = LocalizationService.Instance.SetLanguage(lang, System.IO.Path.Combine(AppContext.BaseDirectory ?? ".", "Data"));
            SettingsService.Current.SelectedLanguage = selected;
            SettingsService.Save();

            SettingsService.Reload();
            LocalizationService.Instance.SetLanguage(SettingsService.Current.SelectedLanguage, System.IO.Path.Combine(AppContext.BaseDirectory ?? ".", "Data"));
            (Application.Current as App)?.ApplySavedTheme();
        }

        private void RecreateMainWindowIfNeeded()
        {
            var desktopLifetime = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var oldMain = desktopLifetime?.MainWindow as Window;
            var vm = oldMain?.DataContext;

            try
            {
                if (desktopLifetime != null && oldMain != null)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            var newMain = new MainWindow
                            {
                                DataContext = vm,
                                Width = oldMain.Width,
                                Height = oldMain.Height,
                                WindowState = oldMain.WindowState,
                            };

                            desktopLifetime.MainWindow = newMain;
                            newMain.Show();
                            oldMain.Close();
                        }
                        catch { }
                    });
                }
            }
            catch { }
        }
    }
}
