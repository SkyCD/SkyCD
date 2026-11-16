using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Couchbase.Lite;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Repository;

namespace SkyCD.App.Views;

public sealed class DatabaseRecoveryWindow : Window
{
    public DatabaseRecoveryWindow(Exception failure, string? lastOpenedCatalogPath)
    {
        Title = "SkyCD could not open its database";
        Width = 620;
        Height = 320;
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var create = new Button { Content = "Create a new database" };
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Close();
        create.Click += (_, _) =>
        {
            create.IsEnabled = false;
            try
            {
                try
                {
                    DatabaseRecovery.Recreate(CouchbaseServiceRegistrator.ResolveDatabaseDirectory(), lastOpenedCatalogPath);
                }
                catch (UnauthorizedAccessException) when (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY")))
                {
                    DatabaseRecovery.CreateInWritableLocation(lastOpenedCatalogPath);
                }
                var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Could not determine the application executable.");
                var start = new ProcessStartInfo(executable) { UseShellExecute = false };
                if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
                    start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
                Process.Start(start);
                Close();
            }
            catch (Exception exception)
            {
                status.Text = "Could not create a new database: " + exception.Message;
                create.IsEnabled = true;
            }
        };

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = "SkyCD could not open or write its internal database.", FontSize = 20, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Create a new database? The existing database will be kept as a backup. SkyCD will reopen the last catalog file if its path is available and the file still exists.", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = failure.GetBaseException().Message, TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { create, close } },
                status
            }
        };
    }

    internal static bool IsDatabaseFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is CouchbaseException or IOException or UnauthorizedAccessException) return true;
        return false;
    }
}

internal static class DatabaseRecovery
{
    internal static void CreateInWritableLocation(string? lastOpenedCatalogPath)
    {
        var directory = Path.Combine(Environment.CurrentDirectory, ".skycd-recovered-" + Guid.NewGuid().ToString("N"));
        Recreate(directory, lastOpenedCatalogPath);
        var locationFile = CouchbaseServiceRegistrator.RecoveryLocationFile;
        var temporaryFile = locationFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryFile, directory);
        File.Move(temporaryFile, locationFile, overwrite: true);
    }

    internal static void Recreate(string directory, string? lastOpenedCatalogPath)
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        var original = Path.Combine(directory, "default.cblite2");
        var backup = Path.Combine(directory, "default.backup-" + Guid.NewGuid().ToString("N") + ".cblite2");
        var staging = Path.Combine(directory, "recovery-" + Guid.NewGuid().ToString("N"));

        if (string.IsNullOrWhiteSpace(lastOpenedCatalogPath) && Directory.Exists(original))
        {
            try
            {
                using var oldDatabase = new Database("default", new DatabaseConfiguration { Directory = directory });
                using var settings = oldDatabase.GetCollection("settings");
                using var document = settings?.GetDocument(AppOptionsDocument.DocumentId);
                lastOpenedCatalogPath = document?.GetString(nameof(AppOptionsDocument.LastOpenedCatalogPath));
            }
            catch (Exception) { /* An unreadable database has no recoverable source path. */ }
        }

        Directory.CreateDirectory(staging);
        using (var database = new Database("default", new DatabaseConfiguration { Directory = staging }))
        using (var settings = database.CreateCollection("settings"))
        {
            var repository = new AppOptionsDocumentRepository();
            repository.Initialize(typeof(AppOptionsDocument), "settings", settings);
            var options = repository.GetOrCreateAppOptions();
            options.LastOpenedCatalogPath = !string.IsNullOrWhiteSpace(lastOpenedCatalogPath) && File.Exists(lastOpenedCatalogPath)
                ? lastOpenedCatalogPath : string.Empty;
            repository.Save(AppOptionsDocument.DocumentId, options);
        }

        var backedUp = false;
        try
        {
            if (Directory.Exists(original))
            {
                Directory.Move(original, backup);
                backedUp = true;
            }
            Directory.Move(Path.Combine(staging, "default.cblite2"), original);
        }
        catch
        {
            if (backedUp && !Directory.Exists(original)) Directory.Move(backup, original);
            throw;
        }
        try { Directory.Delete(staging); }
        catch (IOException) { /* The recovered database is already in place. */ }
        catch (UnauthorizedAccessException) { /* The recovered database is already in place. */ }
    }
}
