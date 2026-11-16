using System;
using System.IO;
using System.Reflection;
using Couchbase.Lite;
using SkyCD.App;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class DatabaseRecoveryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Recreate_PreservesBackupAndRestoresOnlyExistingSource(bool sourceExists)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SkyCD-recovery-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
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

        Assert.Single(Directory.GetDirectories(directory, "default.backup-*.cblite2"));
        using var replacement = new Database("default", new DatabaseConfiguration { Directory = directory });
        using var settings = replacement.GetCollection("settings");
        using var document = settings!.GetDocument(AppOptionsDocument.DocumentId);
        Assert.Equal(sourceExists ? source : string.Empty, document!.GetString(nameof(AppOptionsDocument.LastOpenedCatalogPath)));
    }
}
