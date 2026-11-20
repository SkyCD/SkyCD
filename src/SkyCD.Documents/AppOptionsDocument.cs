using SkyCD.Couchbase.Attributes;
using SkyCD.Documents.Repository;

namespace SkyCD.Documents;

[CouchbaseDocument("settings", typeof(AppOptionsDocumentRepository))]
public sealed class AppOptionsDocument
{
    public System.Collections.Generic.List<CustomPropertyDocument> CustomProperties { get; set; } = [];

    public const string DocumentId = "app-options";

    public WindowOptionsDocument Window { get; set; } = new();

    public bool IsStatusBarVisible { get; set; } = true;

    public BrowserOptionsDocument Browser { get; set; } = new();

    /// <summary>
    /// Customized toolbar layout. <c>null</c> means the default layout has never been changed; an empty list is
    /// a toolbar the user deliberately cleared.
    /// </summary>
    public System.Collections.Generic.List<ToolbarItemDocument>? Toolbar { get; set; }

    public string PluginPath { get; set; } = string.Empty;

    public string Language { get; set; } = "English";

    public int OptionsTabIndex { get; set; }

    public int AppStartCount { get; set; }

    public string LastOpenedCatalogPath { get; set; } = string.Empty;

    public bool IsMcpServerEnabled { get; set; } = true;

    public int McpPort { get; set; } = 8765;

    public bool IsMcpStatusIconVisible { get; set; } = true;

    public bool IsBackgroundMediaSyncEnabled { get; set; }

    public bool SkipSymbolicLinks { get; set; } = false;

    public string HiddenSystemFilePolicy { get; set; } = "Include";

    public bool SkipRecycleBins { get; set; } = true;

    public bool SkipWellKnownSystemFolders { get; set; } = true;

    public bool SkipEmptyFolders { get; set; } = false;

    public bool SkipOnlineOnlyCloudFiles { get; set; } = false;

    /// <summary>Show the collected scan errors and warnings once a scan finishes.</summary>
    public bool ShowScanErrorsAfterScan { get; set; } = true;

    /// <summary>Write scan errors into the comment of every affected catalog entry.</summary>
    public bool SaveScanErrorsToComments { get; set; } = false;

    /// <summary>Eject the scanned medium once the scan finishes successfully, where the platform and drive allow it.</summary>
    public bool EjectMediaAfterScan { get; set; } = false;

    /// <summary>Bring the main window to the foreground once the scan finishes successfully.</summary>
    public bool ActivateMainWindowAfterScan { get; set; } = false;

    /// <summary>Play a sound once the scan finishes successfully.</summary>
    public bool PlaySoundAfterScan { get; set; } = false;

    /// <summary>Files larger than this are skipped by AI description scanning. Default: 1 GiB.</summary>
    public long AiScanMaxFileSizeBytes { get; set; } = 1024L * 1024L * 1024L;

    /// <summary>Selected AI description implementation id; the ACP agent is used when unset.</summary>
    public string AiDescriptionImplementation { get; set; } = "acp";

}
