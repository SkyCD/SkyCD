using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents.Enum;
using SkyCD.Plugin.Host.Media;
using SkyCD.Presentation.Scanning;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class FolderCatalogScannerTests : IDisposable
{
    private readonly TestWorkspace workspace = new();

    private string testDirectory => workspace.Root;

    public void Dispose() => workspace.Dispose();

    [Fact]
    public void Scan_NonExistentFolder_ThrowsDirectoryToScanNotFoundException()
    {
        var scanner = new FolderCatalogScanner();
        var nonExistentPath = Path.Combine(testDirectory, "DoesNotExist");

        var options = new FolderScanOptions(nonExistentPath);

        var ex = Assert.Throws<DirectoryToScanNotFoundException>(() => scanner.Scan(options));
        Assert.Contains(nonExistentPath, ex.Message);
    }

    [Fact]
    public void Scan_BasicFolderWithoutSubfolders_ReturnsRootAndFiles()
    {
        File.WriteAllText(Path.Combine(testDirectory, "file1.txt"), "Hello World");
        File.WriteAllText(Path.Combine(testDirectory, "file2.bin"), "12345");

        var subDir = Path.Combine(testDirectory, "SubFolder");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "ignored.txt"), "ignored content");

        var scanner = new FolderCatalogScanner();
        var options = new FolderScanOptions(
            FolderPath: testDirectory,
            IncludeSubfolders: false,
            TargetParentId: "library");

        var documents = scanner.Scan(options);

        var rootDoc = Assert.Single(documents, doc => doc.ParentId == "library");
        Assert.Equal(CatalogDocumentType.Folder, rootDoc.Type);
        Assert.Equal(2, rootDoc.ChildrenCount);
        Assert.Equal(16, rootDoc.Size); // 11 bytes + 5 bytes

        var fileDocs = documents.Where(doc => doc.ParentId == rootDoc.Id).ToList();
        Assert.Equal(2, fileDocs.Count);
        Assert.Contains(fileDocs, f => f.Name == "file1.txt" && f.Size == 11);
        Assert.Contains(fileDocs, f => f.Name == "file2.bin" && f.Size == 5);
        Assert.DoesNotContain(documents, doc => doc.Name == "SubFolder");
        Assert.DoesNotContain(documents, doc => doc.Name == "ignored.txt");
    }

    [Fact]
    public void Scan_FolderWithSubfolders_WhenIncludeSubfoldersTrue_ReturnsAllHierarchy()
    {
        File.WriteAllText(Path.Combine(testDirectory, "root.txt"), "abc");

        var subDir1 = Path.Combine(testDirectory, "Sub1");
        Directory.CreateDirectory(subDir1);
        File.WriteAllText(Path.Combine(subDir1, "sub1_file.txt"), "123456");

        var nestedDir = Path.Combine(subDir1, "Nested");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(nestedDir, "nested.txt"), "00");

        var scanner = new FolderCatalogScanner();
        var options = new FolderScanOptions(
            FolderPath: testDirectory,
            MediaName: "Custom Media",
            TargetParentId: null,
            RootType: CatalogDocumentType.Media,
            IncludeSubfolders: true);

        var documents = scanner.Scan(options);

        var rootDoc = Assert.Single(documents, doc => doc.ParentId == null);
        Assert.Equal("Custom Media", rootDoc.Name);
        Assert.Equal(CatalogDocumentType.Media, rootDoc.Type);
        Assert.Equal(2, rootDoc.ChildrenCount); // root.txt and Sub1
        Assert.Equal(11, rootDoc.Size); // 3 + 6 + 2

        var sub1Doc = Assert.Single(documents, doc => doc.ParentId == rootDoc.Id && doc.Type == CatalogDocumentType.Folder);
        Assert.Equal("Sub1", sub1Doc.Name);
        Assert.Equal(2, sub1Doc.ChildrenCount); // sub1_file.txt and Nested
        Assert.Equal(8, sub1Doc.Size); // 6 + 2

        var nestedDoc = Assert.Single(documents, doc => doc.ParentId == sub1Doc.Id && doc.Type == CatalogDocumentType.Folder);
        Assert.Equal("Nested", nestedDoc.Name);
        Assert.Equal(1, nestedDoc.ChildrenCount);
        Assert.Equal(2, nestedDoc.Size);

        var nestedFile = Assert.Single(documents, doc => doc.ParentId == nestedDoc.Id);
        Assert.Equal("nested.txt", nestedFile.Name);
        Assert.Equal(2, nestedFile.Size);
    }

    [Fact]
    public void Scan_WhenIncludeFilesFalse_ReturnsFoldersWithoutFileEntries()
    {
        File.WriteAllText(Path.Combine(testDirectory, "root.txt"), "abc");
        var subDir = Path.Combine(testDirectory, "Sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "inside.txt"), "123");

        var scanner = new FolderCatalogScanner();
        var documents = scanner.Scan(new FolderScanOptions(
            FolderPath: testDirectory,
            IncludeSubfolders: true,
            IncludeFiles: false));

        var rootDoc = Assert.Single(documents, doc => doc.ParentId == "library");
        var subDirDoc = Assert.Single(documents, doc => doc.Type == CatalogDocumentType.Folder && doc.Id != rootDoc.Id);
        Assert.Equal(rootDoc.Id, subDirDoc.ParentId);
        Assert.Equal(1, rootDoc.ChildrenCount);
        Assert.Equal(0L, rootDoc.Size);
        Assert.Equal(0, subDirDoc.ChildrenCount);
        Assert.DoesNotContain(documents, doc => doc.Type == CatalogDocumentType.File);
    }

    [Fact]
    public void Scan_WhenIncludeFoldersFalse_ReturnsOnlyDirectFiles()
    {
        File.WriteAllText(Path.Combine(testDirectory, "root.txt"), "abc");
        var subDir = Path.Combine(testDirectory, "Sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "inside.txt"), "123");

        var scanner = new FolderCatalogScanner();
        var documents = scanner.Scan(new FolderScanOptions(
            FolderPath: testDirectory,
            IncludeSubfolders: false,
            IncludeFiles: true));

        var rootDoc = Assert.Single(documents, doc => doc.ParentId == "library");
        Assert.Equal(1, rootDoc.ChildrenCount);
        Assert.Equal(3L, rootDoc.Size);
        var fileDoc = Assert.Single(documents, doc => doc.Type == CatalogDocumentType.File);
        Assert.Equal("root.txt", fileDoc.Name);
        Assert.DoesNotContain(documents, doc => doc.Name == "Sub" || doc.Name == "inside.txt");
    }

    [Fact]
    public void Scan_WhenFilesAndFoldersExcluded_ReturnsTheSelectedRootOnly()
    {
        File.WriteAllText(Path.Combine(testDirectory, "root.txt"), "abc");
        Directory.CreateDirectory(Path.Combine(testDirectory, "Sub"));

        var scanner = new FolderCatalogScanner();
        var documents = scanner.Scan(new FolderScanOptions(
            FolderPath: testDirectory,
            IncludeSubfolders: false,
            IncludeFiles: false));

        var rootDoc = Assert.Single(documents);
        Assert.Equal(Path.GetFileName(testDirectory), rootDoc.Name);
        Assert.Equal(0, rootDoc.ChildrenCount);
        Assert.Equal(0L, rootDoc.Size);
    }

    [Fact]
    public void Scan_WithIncludeExtendedInfo_PopulatesFileAndFolderProperties()
    {
        var filePath = Path.Combine(testDirectory, "sample.txt");
        File.WriteAllText(filePath, "test content");

        var scanner = new FolderCatalogScanner();
        var options = new FolderScanOptions(
            FolderPath: testDirectory,
            IncludeExtendedInfo: true);

        var documents = scanner.Scan(options);

        var rootDoc = documents.First(doc => doc.Type == CatalogDocumentType.Folder);
        Assert.True(rootDoc.Properties.ContainsKey("FullName"));
        Assert.True(rootDoc.Properties.ContainsKey("RealDirectoryFullName"));
        Assert.True(rootDoc.Properties.ContainsKey("RealDirectoryName"));
        Assert.True(rootDoc.Properties.ContainsKey("LastWriteTime"));

        var fileDoc = documents.First(doc => doc.Type == CatalogDocumentType.File);
        Assert.True(fileDoc.Properties.ContainsKey("FullName"));
        Assert.True(fileDoc.Properties.ContainsKey("Size"));
        Assert.True(fileDoc.Properties.ContainsKey("Attributes"));
        Assert.True(fileDoc.Properties.ContainsKey("IsReadOnly"));
    }

    [Fact]
    public void Scan_ReportsProgressDuringExecution()
    {
        File.WriteAllText(Path.Combine(testDirectory, "file.txt"), "data");

        var progressReports = new List<(string Status, int Percentage)>();
        var progress = new SynchronousProgress<(string Status, int Percentage)>(p =>
        {
            lock (progressReports)
            {
                progressReports.Add(p);
            }
        });

        var scanner = new FolderCatalogScanner();
        var options = new FolderScanOptions(testDirectory);

        scanner.Scan(options, progress);

        lock (progressReports)
        {
            Assert.NotEmpty(progressReports);
            Assert.Contains(progressReports, p => p.Percentage == 100);
        }
    }

    [Fact]
    public void Scan_FolderRoot_IsOfferedToMediaTargetsAsFolder()
    {
        File.WriteAllText(Path.Combine(testDirectory, "file.txt"), "data");
        var mediaTargets = new FakeMediaTargetProvider();
        var scanner = new FolderCatalogScanner(mediaTargets: mediaTargets);

        var documents = scanner.Scan(new FolderScanOptions(testDirectory, RootType: CatalogDocumentType.Media));

        var root = Assert.Single(documents, doc => doc.ParentId == "library");
        Assert.Equal("captured", root.Properties["FolderFact"]?.ToString());
        Assert.Equal(MediaTargetKind.Folder, mediaTargets.LastTarget?.Kind);
        Assert.Equal(testDirectory, mediaTargets.LastTarget?.Location);
        Assert.Equal(root.Name, mediaTargets.LastTarget?.Name);
        Assert.Equal(testDirectory, mediaTargets.LastTarget?.Facts?["FullName"]);
    }

    [Fact]
    public void Scan_FailingMediaTarget_StillCompletesWithoutCapturedValues()
    {
        File.WriteAllText(Path.Combine(testDirectory, "file.txt"), "data");
        var scanner = new FolderCatalogScanner(mediaTargets: new FakeMediaTargetProvider { Failure = true });

        var documents = scanner.Scan(new FolderScanOptions(testDirectory));

        var root = Assert.Single(documents, doc => doc.ParentId == "library");
        Assert.False(root.Properties.ContainsKey("FolderFact"));
        Assert.Contains(documents, doc => doc.Type == CatalogDocumentType.File);
    }

    private sealed class FakeMediaTargetProvider : IMediaTargetMetadataProvider
    {
        public MediaTarget? LastTarget { get; private set; }
        public bool Failure { get; init; }

        public bool HasProviders => true;

        public Task<IReadOnlyList<MediaTargetProviderCapture>> CaptureAsync(MediaTarget target,
            CancellationToken cancellationToken)
        {
            LastTarget = target;
            if (Failure)
            {
                throw new InvalidOperationException("plugin failure");
            }

            return Task.FromResult<IReadOnlyList<MediaTargetProviderCapture>>(
            [
                new("fake", "1", "fake", new MediaTargetCapture(new MediaMetadataSchema("fake", 1, []),
                    [new MediaMetadataValue("FolderFact", MediaMetadataType.Text, Text: "captured")]))
            ]);
        }
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
