using Couchbase.Lite;
using SkyCD.App.Models;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.App.Services.Documents;

public sealed class AppOptionsDocument
{
    public int? WindowLeft { get; init; }

    public int? WindowTop { get; init; }

    public double? WindowWidth { get; init; }

    public double? WindowHeight { get; init; }

    public string WindowState { get; init; } = "Normal";

    public double? TreePaneWidth { get; init; }

    public bool IsStatusBarVisible { get; init; } = true;

    public string BrowserViewMode { get; init; } = "Details";

    public string BrowserSortMode { get; init; } = "Name";

    public string PluginPath { get; init; } = string.Empty;

    public string Language { get; init; } = "English";

    public IReadOnlyList<string> DisabledPluginIds { get; init; } = [];

    public int OptionsTabIndex { get; init; }

    public static AppOptionsDocument FromAppOptions(AppOptions options)
    {
        return new AppOptionsDocument
        {
            WindowLeft = options.WindowLeft,
            WindowTop = options.WindowTop,
            WindowWidth = options.WindowWidth,
            WindowHeight = options.WindowHeight,
            WindowState = options.WindowState,
            TreePaneWidth = options.TreePaneWidth,
            IsStatusBarVisible = options.IsStatusBarVisible,
            BrowserViewMode = options.BrowserViewMode,
            BrowserSortMode = options.BrowserSortMode,
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

        return new AppOptionsDocument
        {
            WindowLeft = document.Contains("windowLeft") ? document.GetInt("windowLeft") : null,
            WindowTop = document.Contains("windowTop") ? document.GetInt("windowTop") : null,
            WindowWidth = document.Contains("windowWidth") ? document.GetDouble("windowWidth") : null,
            WindowHeight = document.Contains("windowHeight") ? document.GetDouble("windowHeight") : null,
            WindowState = document.GetString("windowState") ?? "Normal",
            TreePaneWidth = document.Contains("treePaneWidth") ? document.GetDouble("treePaneWidth") : null,
            IsStatusBarVisible = document.GetBoolean("isStatusBarVisible"),
            BrowserViewMode = document.GetString("browserViewMode") ?? "Details",
            BrowserSortMode = document.GetString("browserSortMode") ?? "Name",
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

        var document = new MutableDocument(documentId);
        document.SetString("windowState", WindowState)
            .SetBoolean("isStatusBarVisible", IsStatusBarVisible)
            .SetString("browserViewMode", BrowserViewMode)
            .SetString("browserSortMode", BrowserSortMode)
            .SetString("pluginPath", PluginPath)
            .SetString("language", Language)
            .SetInt("optionsTabIndex", OptionsTabIndex)
            .SetArray("disabledPluginIds", disabledPluginIds);

        if (WindowLeft.HasValue)
        {
            document.SetInt("windowLeft", WindowLeft.Value);
        }

        if (WindowTop.HasValue)
        {
            document.SetInt("windowTop", WindowTop.Value);
        }

        if (WindowWidth.HasValue)
        {
            document.SetDouble("windowWidth", WindowWidth.Value);
        }

        if (WindowHeight.HasValue)
        {
            document.SetDouble("windowHeight", WindowHeight.Value);
        }

        if (TreePaneWidth.HasValue)
        {
            document.SetDouble("treePaneWidth", TreePaneWidth.Value);
        }

        return document;
    }

    public AppOptions ToAppOptions()
    {
        return new AppOptions
        {
            WindowLeft = WindowLeft,
            WindowTop = WindowTop,
            WindowWidth = WindowWidth,
            WindowHeight = WindowHeight,
            WindowState = WindowState,
            TreePaneWidth = TreePaneWidth,
            IsStatusBarVisible = IsStatusBarVisible,
            BrowserViewMode = BrowserViewMode,
            BrowserSortMode = BrowserSortMode,
            PluginPath = PluginPath,
            Language = Language,
            DisabledPluginIds = DisabledPluginIds.ToList(),
            OptionsTabIndex = OptionsTabIndex
        };
    }
}
