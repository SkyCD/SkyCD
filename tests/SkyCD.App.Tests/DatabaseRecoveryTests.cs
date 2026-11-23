using System;
using System.IO;
using System.Reflection;
using Couchbase.Lite;
using SkyCD.App;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class DatabaseRecoveryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Recreate_PreservesBackupAndRestoresOnlyExistingSource(bool sourceExists)
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.CreateDirectory("database");
        var source = Path.Combine(directory, "catalog.skycd");
        if (sourceExists) File.WriteAllText(source, "test");

        using (var database = new Database("default", new DatabaseConfiguration { Directory = directory }))
        using (var collection = database.CreateCollection("settings"))
        {
            var repository = new AppOptionsDocumentRepository();
            repository.Initialize(typeof(AppOptionsDocument), "settings", collection);
            var options = repository.GetOrCreateAppOptions();
            options.LastOpenedCatalogPath = source;
            repository.Save(AppOptionsDocument.DocumentId, options);
        }

        var recovery = typeof(App).Assembly.GetType("SkyCD.App.Views.DatabaseRecovery", throwOnError: true)!;
        recovery.GetMethod("Recreate", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [directory, null]);
        // The stored path round-trips through LiteCore, which may normalize the casing of the drive or
        // temp-folder segment, so compare canonically instead of demanding byte equality.
        var expectedSource = sourceExists ? Path.GetFullPath(source) : string.Empty;

        Assert.Single(Directory.GetDirectories(directory, "default.backup-*.cblite2"));
        using var replacement = new Database("default", new DatabaseConfiguration { Directory = directory });
        using var settings = replacement.GetCollection("settings");
        using var document = settings!.GetDocument(AppOptionsDocument.DocumentId);
        Assert.Equal(expectedSource, Canonicalize(document!.GetString(nameof(AppOptionsDocument.LastOpenedCatalogPath))));
    }

    private static string? Canonicalize(string? path) =>
        string.IsNullOrEmpty(path) ? path : Path.GetFullPath(path);
}
