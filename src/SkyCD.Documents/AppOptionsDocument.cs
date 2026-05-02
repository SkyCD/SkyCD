using SkyCD.Couchbase.Attributes;
using System.Collections.Generic;

namespace SkyCD.Documents;

[CouchbaseDocument("settings")]
public sealed class AppOptionsDocument
{
    public const string DocumentId = "app-options";

    public WindowOptionsDocument Window { get; set; } = new();

    public bool IsStatusBarVisible { get; set; } = true;

    public BrowserOptionsDocument Browser { get; set; } = new();

    public string PluginPath { get; set; } = string.Empty;

    public string Language { get; set; } = "English";

    public IReadOnlyList<string> DisabledPluginIds { get; set; } = [];

    public int OptionsTabIndex { get; set; }
}
