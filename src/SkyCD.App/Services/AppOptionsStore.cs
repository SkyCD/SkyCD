using Couchbase.Lite;
using SkyCD.App.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.App.Services;

public sealed class AppOptionsStore
{
    private readonly CouchbaseLocalStore localStore;

    public AppOptionsStore(CouchbaseLocalStore localStore)
    {
        this.localStore = localStore;
    }

    public AppOptions Load()
    {
        var settingsCollection = localStore.GetCollection(LocalCollection.Settings);
        using var document = settingsCollection.GetDocument(CouchbaseLocalStore.AppOptionsDocumentId);
        if (document is not null)
        {
            return ToAppOptions(document);
        }

        return new AppOptions();
    }

    public void Save(AppOptions options)
    {
        var settingsCollection = localStore.GetCollection(LocalCollection.Settings);
        var disabledPluginIds = new MutableArrayObject();
        foreach (var id in (options.DisabledPluginIds ?? []).Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            disabledPluginIds.AddString(id);
        }

        using var document = new MutableDocument(CouchbaseLocalStore.AppOptionsDocumentId);
        document.SetString("windowState", options.WindowState)
            .SetBoolean("isStatusBarVisible", options.IsStatusBarVisible)
            .SetString("browserViewMode", options.BrowserViewMode)
            .SetString("browserSortMode", options.BrowserSortMode)
            .SetString("pluginPath", options.PluginPath)
            .SetString("language", options.Language)
            .SetInt("optionsTabIndex", options.OptionsTabIndex)
            .SetArray("disabledPluginIds", disabledPluginIds);

        SetNullableInt(document, "windowLeft", options.WindowLeft);
        SetNullableInt(document, "windowTop", options.WindowTop);
        SetNullableDouble(document, "windowWidth", options.WindowWidth);
        SetNullableDouble(document, "windowHeight", options.WindowHeight);
        SetNullableDouble(document, "treePaneWidth", options.TreePaneWidth);

        settingsCollection.Save(document);
    }

    private static AppOptions ToAppOptions(Document document)
    {
        var options = new AppOptions
        {
            WindowLeft = TryGetInt(document, "windowLeft"),
            WindowTop = TryGetInt(document, "windowTop"),
            WindowWidth = TryGetDouble(document, "windowWidth"),
            WindowHeight = TryGetDouble(document, "windowHeight"),
            WindowState = document.GetString("windowState") ?? "Normal",
            TreePaneWidth = TryGetDouble(document, "treePaneWidth"),
            IsStatusBarVisible = document.GetBoolean("isStatusBarVisible"),
            BrowserViewMode = document.GetString("browserViewMode") ?? "Details",
            BrowserSortMode = document.GetString("browserSortMode") ?? "Name",
            PluginPath = document.GetString("pluginPath") ?? string.Empty,
            Language = document.GetString("language") ?? "English",
            OptionsTabIndex = document.GetInt("optionsTabIndex")
        };

        var disabledPluginIds = document.GetArray("disabledPluginIds");
        if (disabledPluginIds is not null)
        {
            for (var index = 0; index < disabledPluginIds.Count; index++)
            {
                var id = disabledPluginIds.GetString(index);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    options.DisabledPluginIds.Add(id);
                }
            }
        }

        return options;
    }

    private static void SetNullableInt(MutableDocument document, string key, int? value)
    {
        if (value.HasValue)
        {
            document.SetInt(key, value.Value);
        }
    }

    private static void SetNullableDouble(MutableDocument document, string key, double? value)
    {
        if (value.HasValue)
        {
            document.SetDouble(key, value.Value);
        }
    }

    private static int? TryGetInt(Document document, string key)
    {
        return document.Contains(key) ? document.GetInt(key) : null;
    }

    private static double? TryGetDouble(Document document, string key)
    {
        return document.Contains(key) ? document.GetDouble(key) : null;
    }
}
