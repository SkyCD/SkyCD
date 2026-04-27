using Couchbase.Lite;
using SkyCD.App.Models;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.App.Services.Documents;

public sealed class AppOptionsDocument
{
    public WindowOptionsDocument Window { get; init; } = new();

    public bool IsStatusBarVisible { get; init; } = true;

    public BrowserOptionsDocument Browser { get; init; } = new();

    public string PluginPath { get; init; } = string.Empty;

    public string Language { get; init; } = "English";

    public IReadOnlyList<string> DisabledPluginIds { get; init; } = [];

    public int OptionsTabIndex { get; init; }

    public static AppOptionsDocument FromAppOptions(AppOptions options)
    {
        return new AppOptionsDocument
        {
            Window = new WindowOptionsDocument
            {
                Left = options.WindowLeft,
                Top = options.WindowTop,
                Width = options.WindowWidth,
                Height = options.WindowHeight,
                State = options.WindowState,
                TreePaneWidth = options.TreePaneWidth
            },
            IsStatusBarVisible = options.IsStatusBarVisible,
            Browser = new BrowserOptionsDocument
            {
                ViewMode = options.BrowserViewMode,
                SortMode = options.BrowserSortMode
            },
            PluginPath = options.PluginPath,
            Language = options.Language,
            DisabledPluginIds = (options.DisabledPluginIds ?? []).ToArray(),
            OptionsTabIndex = options.OptionsTabIndex
        };
    }

    public static AppOptionsDocument? FromDocument(Document? document)
    {
        if (document is null)
        {
            return null;
        }

        var disabledPluginIds = new List<string>();
        var disabledPluginIdsArray = document.GetArray("disabledPluginIds");
        if (disabledPluginIdsArray is not null)
        {
            for (var index = 0; index < disabledPluginIdsArray.Count; index++)
            {
                var value = disabledPluginIdsArray.GetString(index);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    disabledPluginIds.Add(value);
                }
            }
        }

        var window = document.GetDictionary("window");
        var browser = document.GetDictionary("browser");

        return new AppOptionsDocument
        {
            Window = new WindowOptionsDocument
            {
                Left = TryGetInt(window, "left"),
                Top = TryGetInt(window, "top"),
                Width = TryGetDouble(window, "width"),
                Height = TryGetDouble(window, "height"),
                State = window?.GetString("state") ?? "Normal",
                TreePaneWidth = TryGetDouble(window, "treePaneWidth")
            },
            IsStatusBarVisible = document.GetBoolean("isStatusBarVisible"),
            Browser = new BrowserOptionsDocument
            {
                ViewMode = browser?.GetString("viewMode") ?? "Details",
                SortMode = browser?.GetString("sortMode") ?? "Name"
            },
            PluginPath = document.GetString("pluginPath") ?? string.Empty,
            Language = document.GetString("language") ?? "English",
            DisabledPluginIds = disabledPluginIds,
            OptionsTabIndex = document.GetInt("optionsTabIndex")
        };
    }

    public MutableDocument ToMutableDocument(string documentId)
    {
        var disabledPluginIds = new MutableArrayObject();
        foreach (var id in DisabledPluginIds.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            disabledPluginIds.AddString(id);
        }

        var window = new MutableDictionaryObject();
        window.SetString("state", Window.State);
        if (Window.Left.HasValue)
        {
            window.SetInt("left", Window.Left.Value);
        }

        if (Window.Top.HasValue)
        {
            window.SetInt("top", Window.Top.Value);
        }

        if (Window.Width.HasValue)
        {
            window.SetDouble("width", Window.Width.Value);
        }

        if (Window.Height.HasValue)
        {
            window.SetDouble("height", Window.Height.Value);
        }

        if (Window.TreePaneWidth.HasValue)
        {
            window.SetDouble("treePaneWidth", Window.TreePaneWidth.Value);
        }

        var browser = new MutableDictionaryObject();
        browser.SetString("viewMode", Browser.ViewMode);
        browser.SetString("sortMode", Browser.SortMode);

        var document = new MutableDocument(documentId);
        document.SetDictionary("window", window)
            .SetBoolean("isStatusBarVisible", IsStatusBarVisible)
            .SetDictionary("browser", browser)
            .SetString("pluginPath", PluginPath)
            .SetString("language", Language)
            .SetInt("optionsTabIndex", OptionsTabIndex)
            .SetArray("disabledPluginIds", disabledPluginIds);

        return document;
    }

    public AppOptions ToAppOptions()
    {
        return new AppOptions
        {
            WindowLeft = Window.Left,
            WindowTop = Window.Top,
            WindowWidth = Window.Width,
            WindowHeight = Window.Height,
            WindowState = Window.State,
            TreePaneWidth = Window.TreePaneWidth,
            IsStatusBarVisible = IsStatusBarVisible,
            BrowserViewMode = Browser.ViewMode,
            BrowserSortMode = Browser.SortMode,
            PluginPath = PluginPath,
            Language = Language,
            DisabledPluginIds = DisabledPluginIds.ToList(),
            OptionsTabIndex = OptionsTabIndex
        };
    }

    private static int? TryGetInt(DictionaryObject? dictionary, string key)
    {
        return dictionary?.Contains(key) == true ? dictionary.GetInt(key) : null;
    }

    private static double? TryGetDouble(DictionaryObject? dictionary, string key)
    {
        return dictionary?.Contains(key) == true ? dictionary.GetDouble(key) : null;
    }
}

public sealed class WindowOptionsDocument
{
    public int? Left { get; init; }

    public int? Top { get; init; }

    public double? Width { get; init; }

    public double? Height { get; init; }

    public string State { get; init; } = "Normal";

    public double? TreePaneWidth { get; init; }
}

public sealed class BrowserOptionsDocument
{
    public string ViewMode { get; init; } = "Details";

    public string SortMode { get; init; } = "Name";
}
