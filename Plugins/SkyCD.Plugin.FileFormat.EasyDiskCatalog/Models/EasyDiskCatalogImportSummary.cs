using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.EasyDiskCatalog.Models;

public sealed class EasyDiskCatalogImportSummary
{
    public int Disks { get; set; }

    public int Folders { get; set; }

    public int Files { get; set; }

    public int Categories { get; set; }

    public int Locations { get; set; }

    public int Contacts { get; set; }

    public int CurrentLoans { get; set; }

    public int LoanHistoryEntries { get; set; }

    public int SkippedRows { get; set; }

    public int PreviewsFound { get; set; }

    public int PreviewsMissing { get; set; }

    public int PreviewsUnresolved { get; set; }

    public int UnknownTables { get; set; }

    public int UnknownColumns { get; set; }

    public bool OpenedEncrypted { get; set; }

    public List<string> Notes { get; } = [];
}
