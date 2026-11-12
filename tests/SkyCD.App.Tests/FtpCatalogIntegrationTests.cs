using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class FtpCatalogIntegrationTests : IDisposable
{
    private const string Password = "s3cret-pass";
    private const string UrlPassword = "url-only-secret";
    private const string Username = "catalog-user";

    private readonly string appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-ftp-cblite-{Guid.NewGuid():N}");
    private readonly string ftpRoot = Path.Combine(Path.GetTempPath(), $"skycd-ftp-root-{Guid.NewGuid():N}");
    private readonly string? previousAppData = Environment.GetEnvironmentVariable("APPDATA");
    private readonly string? previousXdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? previousDatabaseDirectory = Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public FtpCatalogIntegrationTests()
    {
        Directory.CreateDirectory(appDataRoot);
        Directory.CreateDirectory(ftpRoot);
        Environment.SetEnvironmentVariable("APPDATA", appDataRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", appDataRoot);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", Path.Combine(appDataRoot, "SkyCD"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APPDATA", previousAppData);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousXdgConfig);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", previousDatabaseDirectory);

        try
        {
            if (Directory.Exists(appDataRoot))
            {
                Directory.Delete(appDataRoot, recursive: true);
            }
        }
        catch
        {
            // The database file can stay locked until the process releases it.
        }

        try
        {
            if (Directory.Exists(ftpRoot))
            {
                Directory.Delete(ftpRoot, recursive: true);
            }
        }
        catch
        {
            // The fixture directory is removed on the next test run if it is still open.
        }
    }

    [Fact]
    public void FtpScan_CatalogsNestedEntries_AndSurvivesSaveAndReopen()
    {
        var stamp = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        var readme = WriteFile("readme.txt", "hello world", stamp);
        var spaced = WriteFile("my file.txt", "spaced", stamp);
        var guide = WriteFile(Path.Combine("docs", "guide.txt"), "guide", stamp);
        var note = WriteFile(Path.Combine("docs", "deep", "note.txt"), "note", stamp);
        var emptyDir = Path.Combine(ftpRoot, "empty-dir");
        Directory.CreateDirectory(emptyDir);
        Directory.SetLastWriteTimeUtc(Path.Combine(ftpRoot, "docs"), stamp);
        Directory.SetLastWriteTimeUtc(Path.Combine(ftpRoot, "docs", "deep"), stamp);
        Directory.SetLastWriteTimeUtc(emptyDir, stamp);

        using var server = new LocalFtpServer(ftpRoot, Username, Password);
        using var provider = CreateRepository();
        var repositoryManager = provider.Resolve<RepositoryManager>();
        var catalogRepository = (CatalogDocumentRepository)repositoryManager.For<CatalogDocument>();
        using var viewModel = new MainWindowViewModel(repositoryManager);
        var progress = new RecordingProgress();
        var address = $"ftp://{Username}:{UrlPassword}@127.0.0.1:{server.Port}/";

        var scanned = new FtpCatalogScanner().Scan(new FtpScanOptions(
            address,
            Username,
            Password,
            anonymous: false,
            mediaName: "Remote Archive",
            targetParentId: "library",
            includeExtendedInfo: true), progress);

        Assert.Equal(8, scanned.Count);
        var root = scanned[0];
        Assert.Equal("Remote Archive", root.Name);
        Assert.Equal(CatalogDocumentType.NetworkFolder, root.Type);
        Assert.Equal("library", root.ParentId);
        Assert.Equal(4, root.ChildrenCount);
        Assert.Equal(readme.Length + spaced.Length + guide.Length + note.Length, root.Size);
        Assert.StartsWith("ftp://127.0.0.1:", root.Properties["URL"]?.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("@", root.Properties["URL"]?.ToString(), StringComparison.Ordinal);

        AssertFile(scanned, "readme.txt", root.Id, readme.Length, stamp);
        AssertFile(scanned, "my file.txt", root.Id, spaced.Length, stamp);
        var docs = AssertFolder(scanned, "docs", root.Id, 2);
        AssertFile(scanned, "guide.txt", docs.Id, guide.Length, stamp);
        var deep = AssertFolder(scanned, "deep", docs.Id, 1);
        AssertFile(scanned, "note.txt", deep.Id, note.Length, stamp);
        var empty = AssertFolder(scanned, "empty-dir", root.Id, 0);
        Assert.Equal(0, empty.Size);
        AssertNoSecrets(scanned);
        Assert.Contains(progress.Messages, message => message.Contains("deep", StringComparison.Ordinal));
        Assert.Contains(progress.Messages, message => message.Contains("note.txt", StringComparison.Ordinal));
        Assert.Contains(progress.Messages, message => message.Contains("Scanning complete", StringComparison.Ordinal));
        AssertDirectoryLog(progress.Messages, "/", 4, "/docs");
        AssertDirectoryLog(progress.Messages, "/docs", 2, "/docs/deep");
        AssertDirectoryLog(progress.Messages, "/docs/deep", 1, followingStart: null);
        AssertDirectoryLog(progress.Messages, "/empty-dir", 0, followingStart: null);
        AssertNoSecrets(progress.Messages);

        var withoutMetadata = new FtpCatalogScanner().Scan(new FtpScanOptions(
            address,
            Username,
            Password,
            anonymous: false,
            mediaName: "Names Only",
            targetParentId: null,
            includeExtendedInfo: false));
        Assert.All(withoutMetadata, document => Assert.Empty(document.Properties));
        Assert.Equal("Names Only", withoutMetadata[0].Name);
        Assert.Null(withoutMetadata[0].ParentId);
        Assert.Contains(withoutMetadata, document => document.Name == "note.txt" && document.ParentId != null);

        viewModel.AddImportedDocuments(scanned);
        viewModel.CompleteSaveCatalog(Path.Combine(appDataRoot, "remote.scd"));
        Assert.False(viewModel.IsDirtyDocument);

        var stored = catalogRepository.GetAll();
        Assert.Contains(stored, document => document.Name == "Remote Archive" && document.Type == CatalogDocumentType.NetworkFolder);
        Assert.Contains(stored, document => document.Name == "note.txt" && document.ParentId == deep.Id);
        AssertNoSecrets(stored);

        using var reloaded = new MainWindowViewModel(repositoryManager);
        var library = reloaded.TreeNodes.Single(node => node.Key == "library");
        reloaded.SelectedTreeNode = library;
        Assert.Contains(reloaded.BrowserItems, item => item.Name == "Remote Archive");

        var archive = library.Children.Single(node => node.Title == "Remote Archive");
        reloaded.SelectedTreeNode = archive;
        Assert.Equal(4, reloaded.BrowserItems.Count);
        Assert.Contains(reloaded.BrowserItems, item => item.Name == "my file.txt");
        Assert.Contains(reloaded.BrowserItems, item => item.Name == "docs");
        Assert.Contains(reloaded.BrowserItems, item => item.Name == "empty-dir");

        var docsNode = archive.Children.Single(node => node.Title == "docs");
        reloaded.SelectedTreeNode = docsNode;
        Assert.Contains(reloaded.BrowserItems, item => item.Name == "guide.txt");
        Assert.Contains(reloaded.BrowserItems, item => item.Name == "deep");
    }

    [Fact]
    public void FtpScan_ReportsAuthenticationFailureWithoutSavingOrEchoingThePassword()
    {
        File.WriteAllText(Path.Combine(ftpRoot, "secret.txt"), "hidden");
        using var server = new LocalFtpServer(ftpRoot, Username, Password);
        using var provider = CreateRepository();
        var repositoryManager = provider.Resolve<RepositoryManager>();
        var catalogRepository = (CatalogDocumentRepository)repositoryManager.For<CatalogDocument>();
        var before = catalogRepository.GetAll().Select(document => document.Id).OrderBy(id => id).ToArray();
        var progress = new RecordingProgress();

        var error = Assert.Throws<FtpAuthenticationException>(() => new FtpCatalogScanner().Scan(new FtpScanOptions(
            $"ftp://127.0.0.1:{server.Port}/",
            Username,
            "wrong-" + Password,
            anonymous: false,
            mediaName: "Should Not Import",
            targetParentId: "library",
            includeExtendedInfo: true), progress));

        Assert.DoesNotContain(Password, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("wrong-", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(progress.Messages, message => message.Contains("Scanning complete", StringComparison.Ordinal));
        Assert.Equal(before, catalogRepository.GetAll().Select(document => document.Id).OrderBy(id => id).ToArray());
        Assert.DoesNotContain(catalogRepository.GetAll(), document => document.Name == "Should Not Import");
    }

    [Fact]
    public void FtpScan_AnonymousAccess_ReadsTheFixture()
    {
        File.WriteAllText(Path.Combine(ftpRoot, "public.txt"), "public-file");
        Directory.CreateDirectory(Path.Combine(ftpRoot, "nested"));
        File.WriteAllText(Path.Combine(ftpRoot, "nested", "inside.txt"), "inside");
        using var server = new LocalFtpServer(ftpRoot, Username, Password, allowAnonymous: true);

        var scanned = new FtpCatalogScanner().Scan(new FtpScanOptions(
            $"ftp://127.0.0.1:{server.Port}/nested",
            username: null,
            password: null,
            anonymous: true,
            mediaName: null,
            targetParentId: "library",
            includeExtendedInfo: true));

        Assert.Equal("nested", scanned[0].Name);
        Assert.Equal(CatalogDocumentType.NetworkFolder, scanned[0].Type);
        var file = Assert.Single(scanned, document => document.Name == "inside.txt");
        Assert.Equal(scanned[0].Id, file.ParentId);
        Assert.Equal("inside".Length, file.Size);
        Assert.DoesNotContain(scanned, document => document.Name == "public.txt");
        AssertNoSecrets(scanned);
    }

    [Fact]
    public void FtpScan_FallsBackToListAndReportsAClosedPort()
    {
        Directory.CreateDirectory(Path.Combine(ftpRoot, "listed"));
        File.WriteAllText(Path.Combine(ftpRoot, "listed", "plain.txt"), "plain");
        using var server = new LocalFtpServer(ftpRoot, Username, Password, supportMlsd: false);

        var scanned = new FtpCatalogScanner().Scan(new FtpScanOptions(
            $"127.0.0.1:{server.Port}/listed",
            Username,
            Password,
            anonymous: false,
            mediaName: "Listed",
            targetParentId: null,
            includeExtendedInfo: true));

        Assert.Equal("Listed", scanned[0].Name);
        Assert.Null(scanned[0].ParentId);
        var file = Assert.Single(scanned, document => document.Name == "plain.txt");
        Assert.Equal("plain".Length, file.Size);
        Assert.Equal("rw-r--r--", file.Properties["Permission"]?.ToString());

        using var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var closedPort = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        var failure = Assert.Throws<FtpConnectionException>(() => new FtpCatalogScanner().Scan(new FtpScanOptions(
            $"ftp://127.0.0.1:{closedPort}/",
            Username,
            Password,
            anonymous: false,
            mediaName: "Offline",
            targetParentId: null,
            includeExtendedInfo: false)));
        Assert.DoesNotContain(Password, failure.ToString(), StringComparison.Ordinal);
        Assert.Contains(closedPort.ToString(CultureInfo.InvariantCulture), failure.Message, StringComparison.Ordinal);
    }

    private string WriteFile(string relativePath, string contents, DateTime stamp)
    {
        var path = Path.Combine(ftpRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = Encoding.UTF8.GetBytes(contents);
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, stamp);
        return contents;
    }

    private static void AssertFile(IReadOnlyList<CatalogDocument> documents, string name, string parentId, long size, DateTime stamp)
    {
        var document = Assert.Single(documents, candidate => candidate.Name == name && candidate.ParentId == parentId);
        Assert.Equal(CatalogDocumentType.File, document.Type);
        Assert.Equal(size, document.Size);
        Assert.Equal(size.ToString(CultureInfo.InvariantCulture), document.Properties["Size"]?.ToString());
        Assert.Equal("rw-r--r--", document.Properties["Permission"]?.ToString());
        Assert.EndsWith("/" + name, document.Properties["FullName"]?.ToString(), StringComparison.Ordinal);
        var modified = DateTime.Parse(document.Properties["FileDateTime"]?.ToString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        Assert.Equal(TruncateToSeconds(stamp), TruncateToSeconds(modified));
    }

    private static CatalogDocument AssertFolder(IReadOnlyList<CatalogDocument> documents, string name, string parentId, int children)
    {
        var document = Assert.Single(documents, candidate => candidate.Name == name && candidate.ParentId == parentId);
        Assert.Equal(CatalogDocumentType.Folder, document.Type);
        Assert.Equal(children, document.ChildrenCount);
        Assert.Equal("rwxr-xr-x", document.Properties["Permission"]?.ToString());
        return document;
    }

    private static DateTime TruncateToSeconds(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, DateTimeKind.Utc);

    private static void AssertDirectoryLog(IReadOnlyList<string> messages, string path, int count, string? followingStart)
    {
        var started = LocalizationService.Current.Format(TextKeys.ReadingRemoteDirectory, path);
        var found = LocalizationService.Current.Format(TextKeys.FoundItemsInRemoteDirectory, count, path);
        var startIndex = messages.ToList().FindIndex(message => message.Contains(started, StringComparison.Ordinal));
        var foundIndex = messages.ToList().FindIndex(message => message.Contains(found, StringComparison.Ordinal));
        Assert.True(startIndex >= 0, started);
        Assert.True(foundIndex > startIndex, found);
        if (followingStart is null)
        {
            return;
        }

        var nextStart = LocalizationService.Current.Format(TextKeys.ReadingRemoteDirectory, followingStart);
        var nextIndex = messages.ToList().FindIndex(message => message.Contains(nextStart, StringComparison.Ordinal));
        Assert.True(nextIndex > foundIndex, nextStart);
    }

    private static void AssertNoSecrets(IEnumerable<CatalogDocument> documents)
    {
        foreach (var document in documents)
        {
            var text = string.Join(
                "\n",
                new[] { document.Id, document.Name, document.ParentId, document.Status }
                    .Concat(document.Properties.Select(pair => pair.Key + "=" + pair.Value)));
            AssertNoSecrets(new[] { text });
        }
    }

    private static void AssertNoSecrets(IEnumerable<string> values)
    {
        var text = string.Join("\n", values);
        Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
        Assert.DoesNotContain(UrlPassword, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Username, text, StringComparison.Ordinal);
    }

    private static Container CreateRepository()
    {
        var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositoryManager = provider.Resolve<RepositoryManager>();
        var catalogRepository = (CatalogDocumentRepository)repositoryManager.For<CatalogDocument>();
        foreach (var entry in catalogRepository.CreateDefaultEntries())
        {
            catalogRepository.Save(entry.Id, entry);
        }

        var appOptionsRepository = (AppOptionsDocumentRepository)repositoryManager.For<AppOptionsDocument>();
        var options = appOptionsRepository.GetOrCreateAppOptions();
        options.AppStartCount = 10;
        appOptionsRepository.Save(AppOptionsDocument.DocumentId, options);
        return provider;
    }

    private sealed class RecordingProgress : IOperationProgress
    {
        public List<string> Messages { get; } = [];

        public void Report(OperationProgressReport value)
        {
        }

        public void ReportStage(string stageName, double? stagePercentage = null, double? overallPercentage = null, long? stageCurrent = null, long? stageTotal = null, long? overallCurrent = null, long? overallTotal = null) =>
            Messages.Add(stageName);

        public void ReportItem(string currentItem, double? stagePercentage = null, double? overallPercentage = null, long? stageCurrent = null, long? stageTotal = null, long? overallCurrent = null, long? overallTotal = null) =>
            Messages.Add(currentItem);

        public void ReportOverall(double? overallPercentage, long? overallCurrent = null, long? overallTotal = null, string? operationTitle = null)
        {
            if (!string.IsNullOrWhiteSpace(operationTitle))
            {
                Messages.Add(operationTitle);
            }
        }

        public void ReportLog(OperationLogLevel level, string message, string? stage = null, string? itemPath = null, string? details = null) =>
            Messages.Add(string.Join(" ", new[] { message, stage, itemPath, details }.Where(static value => !string.IsNullOrWhiteSpace(value))));

        public void ReportInfo(string message, string? itemPath = null, string? stage = null) =>
            ReportLog(OperationLogLevel.Information, message, stage, itemPath);

        public void ReportWarning(string message, string? itemPath = null, string? stage = null) =>
            ReportLog(OperationLogLevel.Warning, message, stage, itemPath);

        public void ReportError(string message, Exception? exception = null, string? itemPath = null, string? stage = null) =>
            ReportLog(OperationLogLevel.Error, message, stage, itemPath, exception?.Message);

        public void ReportSkipped(string itemPath, string reason, string? stage = null) =>
            Messages.Add(itemPath + " " + reason);

        public void ReportFailed(string itemPath, string reason, string? stage = null, Exception? exception = null) =>
            Messages.Add(itemPath + " " + reason + " " + exception?.Message);
    }
}
