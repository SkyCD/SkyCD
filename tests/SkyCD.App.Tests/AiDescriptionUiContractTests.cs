using System;
using System.IO;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Couchbase.Repository;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>Issue #462: the Properties surface and batch dialog expose the documented actions.</summary>
public sealed class AiDescriptionUiContractTests
{
    [Fact]
    public void PropertiesWindow_ExposesDescriptionActionsSeparateFromComments()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "PropertiesWindow.axaml");

        Assert.Contains("Header=\"{i18n:Translate {x:Static i18n:UiKeys.AiDescription}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding HasAiDescriptionTab}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding GenerateCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CopyCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AdoptCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ClearCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding StateText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ProvenanceText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RtfText=\"{Binding Comments, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RtfText=\"{Binding DescriptionText", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void BatchWindow_OffersExplicitStartCancelAndPerItemResults()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "AiDescriptionBatchWindow.axaml");

        Assert.Contains("Command=\"{Binding StartCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CancelCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding RegenerateExisting, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ProgressBar", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Results}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding ProviderText", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding ContextText", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolsMenu_OffersDescriptionBatch_AndStaysDisabledWithoutAProvider()
    {
        var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        try
        {
            var item = Assert.Single(vm.ToolsMenuItems, menu => menu.Key == "ai_description_batch");
            Assert.Equal("Generate AI descriptions...", item.Header);
            Assert.False(vm.GenerateAiDescriptionsCommand.CanExecute(null));
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact]
    public void LocalizationCatalogs_ContainAiDescriptionLabels()
    {
        foreach (var language in new[] { "en", "lt" })
        {
            var ui = ReadRepoFile("src", "SkyCD.UI", "Translations", $"{language}-UI.yaml");
            var text = ReadRepoFile("src", "SkyCD.UI", "Translations", $"{language}-Text.yaml");
            var metadata = ReadRepoFile("src", "SkyCD.UI", "Translations", $"{language}-Metadata.yaml");

            foreach (var key in new[]
                     {
                         "ui.AiDescription:", "ui.Generate:", "ui.Regenerate:", "ui.AdoptAsComment:",
                         "ui.AiDescriptionStale:", "ui.DestinationUnknown:"
                     })
            {
                Assert.Contains(key, ui, StringComparison.Ordinal);
            }

            foreach (var key in new[]
                     {
                         "text.GenerateAiDescriptions:", "text.AiDescriptionProvider:",
                         "text.AiDescriptionProvenance:", "text.AiDescriptionBatchSummary:"
                     })
            {
                Assert.Contains(key, text, StringComparison.Ordinal);
            }

            Assert.Contains("metadata.AiDescription:", metadata, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PropertiesDialog_AdoptsIntoTheCommentEditorOnly()
    {
        using var stack = new DescriptionStack();
        var service = stack.Service("entry", "Generated text");
        var dialog = new PropertiesDialogViewModel(
            "item", "entry", string.Empty, "Existing comment", new PropertiesCollection());
        dialog.InitializeAiDescription(service, "entry");

        Assert.NotNull(dialog.AiDescription);
        Assert.True(dialog.HasAiDescriptionTab);
        dialog.AiDescription!.AdoptCommand.Execute(null);
        Assert.Equal("Existing comment", dialog.Comments);
        dialog.AiDescription.AdoptCommand.Execute(null);
        Assert.Equal("Generated text", dialog.Comments);
        Assert.Equal("Generated text", service.Read("entry").Text);
        Assert.Equal("Existing comment", stack.Catalog.Get("entry")!.Properties["Comment"]);
    }

    private sealed class DescriptionStack : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"skycd-ai-prop-{Guid.NewGuid():N}");
        private readonly string? previousAppData = Environment.GetEnvironmentVariable("APPDATA");
        private readonly string? previousDatabaseDirectory = Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");
        private readonly Container container;

        public DescriptionStack()
        {
            Directory.CreateDirectory(root);
            Environment.SetEnvironmentVariable("APPDATA", root);
            Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", Path.Combine(root, "SkyCD"));
            container = new Container();
            new CouchbaseServiceRegistrator().RegisterServices(container);
            var repositories = container.Resolve<RepositoryManager>();
            Catalog = (CatalogDocumentRepository)repositories.For<CatalogDocument>();
            Store = new CouchbaseMediaCacheStore(Catalog.Collection.Database);
            Catalog.Save("entry", new CatalogDocument
            {
                Id = "entry",
                Name = "entry",
                Type = CatalogDocumentType.File,
                Size = 10,
                Properties = new PropertiesCollection { ["Comment"] = "Existing comment" }
            });
        }

        public CatalogDocumentRepository Catalog { get; }

        public CouchbaseMediaCacheStore Store { get; }

        public AiDescriptionService Service(string entryId, string text)
        {
            var descriptions = new CatalogDescriptionService(Catalog, Store);
            var context = descriptions.Build(entryId, new());
            descriptions.Save(context, new GeneratedDescription(
                text, "agent", "model", "1.0.0", "prompt", "English",
                "content-based AI description", "local-inference", DateTimeOffset.UtcNow));
            return new AiDescriptionService(Catalog, Store);
        }

        public void Dispose()
        {
            Store.Dispose();
            container.Dispose();
            Environment.SetEnvironmentVariable("APPDATA", previousAppData);
            Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", previousDatabaseDirectory);
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SkyCD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root with SkyCD.slnx was not found.");
    }
}
