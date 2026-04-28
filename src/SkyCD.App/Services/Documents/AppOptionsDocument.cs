using Couchbase.Lite;
using Couchbase.Lite.Mapping;
using SkyCD.App.Models;
using SkyCD.Presentation.ViewModels;
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
        return document?.ToObject<AppOptionsDocument>();
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
    public BrowserViewMode ViewMode { get; init; } = BrowserViewMode.Details;

    public BrowserSortMode SortMode { get; init; } = BrowserSortMode.Name;
}
