using Couchbase.Lite;
using SkyCD.App.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SkyCD.App.Services;

public sealed class AppOptionsStore : IDisposable
{
    private readonly CouchbaseLocalStore localStore;
    private readonly bool ownsLocalStore;
    private readonly string legacyOptionsFilePath;

    public AppOptionsStore(CouchbaseLocalStore? localStore = null, string? appDataRoot = null)
    {
        this.localStore = localStore ?? new CouchbaseLocalStore(appDataRoot);
        ownsLocalStore = localStore is null;
        legacyOptionsFilePath = Path.Combine(this.localStore.DatabaseDirectory, "options.json");
    }

    public AppOptions Load()
    {
        var settingsCollection = localStore.GetCollection(LocalCollection.Settings);
        using var document = settingsCollection.GetDocument(CouchbaseLocalStore.AppOptionsDocumentId);
        if (document is not null)
        {
            return ToAppOptions(document);
        }

        if (TryLoadLegacyOptions(out var legacyOptions))
        {
            Save(legacyOptions);
            return legacyOptions;
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

    public void Dispose()
    {
        if (ownsLocalStore)
        {
            localStore.Dispose();
        }
    }

    private bool TryLoadLegacyOptions(out AppOptions options)
    {
        options = new AppOptions();

        if (!File.Exists(legacyOptionsFilePath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(legacyOptionsFilePath);
            options = JsonSerializer.Deserialize<AppOptions>(json) ?? new AppOptions();
            return true;
        }
        catch
        {
            return false;
        }
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
