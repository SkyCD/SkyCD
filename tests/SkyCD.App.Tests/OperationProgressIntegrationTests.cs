using System;
using System.Collections.Generic;
using System.IO;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class OperationProgressIntegrationTests : IDisposable
{
    private readonly TestWorkspace workspace = new();

    private string testDirectory => workspace.Root;

    public void Dispose()
    {
        workspace.Dispose();
    }

    [Fact]
    public void FolderScan_ReportsStructuredEventsAndMultiStageProgress()
    {
        // Setup folder tree
        var subDir = Path.Combine(testDirectory, "SubFolder");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(testDirectory, "file1.txt"), "hello");
        File.WriteAllText(Path.Combine(subDir, "file2.txt"), "world");

        var progressVm = new AddingProgressDialogViewModel();
        var scanner = new FolderCatalogScanner();
        var options = new FolderScanOptions(
            FolderPath: testDirectory,
            MediaName: "TestProgress",
            TargetParentId: null,
            RootType: CatalogDocumentType.Folder,
            IncludeSubfolders: true,
            IncludeExtendedInfo: true);

        var docs = scanner.Scan(options, progressVm);

        Assert.Equal(4, docs.Count); // root, file1, SubFolder, file2
        Assert.True(progressVm.TotalLoggedCount > 0);
        Assert.True(progressVm.ProcessedCount >= 2);
        Assert.Equal(100.0, progressVm.OverallProgressValue);
        Assert.False(progressVm.IsOverallIndeterminate);

        var logText = progressVm.GetFullLogText();
        Assert.Contains("Reading source folder...", logText);
        Assert.Contains("file1.txt", logText);
        Assert.Contains("Scanning complete", logText);
    }

    [Fact]
    public void FolderScan_WithLegacyProgress_ReportsCorrectly()
    {
        File.WriteAllText(Path.Combine(testDirectory, "legacy.txt"), "legacy content");

        var reports = new List<(string Status, int Percentage)>();
        var progress = new Progress<(string Status, int Percentage)>(p => reports.Add(p));

        var scanner = new FolderCatalogScanner();
        var options = new FolderScanOptions(testDirectory);

        var docs = scanner.Scan(options, progress);

        Assert.NotEmpty(docs);
    }
}
