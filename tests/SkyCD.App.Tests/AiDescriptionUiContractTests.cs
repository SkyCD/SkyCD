using System;
using System.IO;
using System.Linq;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Couchbase.Repository;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.ViewModels;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>Issue #462: the Properties surface exposes the documented actions.</summary>
public sealed class AiDescriptionUiContractTests
{
    [Fact]
    public void PropertiesWindow_ShowsUnifiedDescriptionAsARowInThePropertiesList()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "PropertiesWindow.axaml");
        var propertiesTab = SectionOf(xaml, "<TabItem Header=\"{i18n:Translate {x:Static i18n:UiKeys.Properties}}\"");
        var generalTab = SectionOf(xaml, "<TabItem Header=\"{i18n:Translate {x:Static i18n:UiKeys.General}}\"");

        // The description row lives in the Properties list, not as a block under the comments editor.
        Assert.Contains("<cp:PropertiesList x:Name=\"InfoPropertiesList\"", propertiesTab, StringComparison.Ordinal);
        Assert.Contains("<cp:PropertiesList.ValueCellTemplate>", propertiesTab, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsDescriptionRow}\"", propertiesTab, StringComparison.Ordinal);
        Assert.Contains(".Description.DescriptionText}", propertiesTab, StringComparison.Ordinal);
        Assert.Contains(".Description.SourceText}", propertiesTab, StringComparison.Ordinal);
        Assert.Contains(".Description.StateText}", propertiesTab, StringComparison.Ordinal);
        Assert.Contains(".Description.GenerateCommand}", propertiesTab, StringComparison.Ordinal);
        Assert.Contains(".Description.CopyCommand}", propertiesTab, StringComparison.Ordinal);
        Assert.Contains(".Description.ClearCommand}", propertiesTab, StringComparison.Ordinal);
        Assert.Contains(".Description.CanShowGenerate}", propertiesTab, StringComparison.Ordinal);
        Assert.Contains(".Description.CanCopy}", propertiesTab, StringComparison.Ordinal);
        Assert.Contains(".Description.CanShowClear}", propertiesTab, StringComparison.Ordinal);
        Assert.DoesNotContain("Description.AdoptCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("UiKeys.AiDescription}}", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RtfText=\"{Binding DescriptionText", xaml, StringComparison.Ordinal);

        // General keeps only the name, rating and comments; comments take the space the block used.
        Assert.Contains("RtfText=\"{Binding Comments, Mode=TwoWay}\"", generalTab, StringComparison.Ordinal);
        Assert.Contains("RowDefinitions=\"Auto,*\"", generalTab, StringComparison.Ordinal);
        Assert.DoesNotContain("Description", generalTab, StringComparison.Ordinal);
        Assert.DoesNotContain("HasDescriptionSection", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RowDefinitions=\"Auto,*,Auto\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>Returns the XAML of one tab, up to the next tab item or the tab control end.</summary>
    private static string SectionOf(string xaml, string start)
    {
        var begin = xaml.IndexOf(start, StringComparison.Ordinal);
        Assert.True(begin >= 0, $"'{start}' was not found.");
        var end = xaml.IndexOf("<TabItem ", begin + start.Length, StringComparison.Ordinal);
        var close = xaml.IndexOf("</TabControl>", begin, StringComparison.Ordinal);
        if (end < 0 || (close >= 0 && close < end))
        {
            end = close;
        }

        return end < 0 ? xaml[begin..] : xaml[begin..end];
    }

    [Fact]
    public void ToolsMenu_OmitsManualDescriptionBatch()
    {
        var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        try
        {
            Assert.DoesNotContain(vm.ToolsMenuItems, menu => menu.Key == "ai_description_batch");
            Assert.DoesNotContain(vm.ToolsMenuItems, menu => menu.Header == "Generate AI descriptions...");
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact]
    public void Toolbar_OmitsManualDescriptionBatch_AndDropsItFromSavedLayouts()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        Assert.DoesNotContain("ai-descriptions", ToolbarCommandIds.All);
        Assert.DoesNotContain(vm.ToolbarCommands(), command => command.Id == "ai-descriptions");

        var saved = new[]
        {
            ToolbarItemDocument.Command(ToolbarCommandIds.Refresh),
            ToolbarItemDocument.Command("ai-descriptions"),
            ToolbarItemDocument.Command(ToolbarCommandIds.Find)
        };
        vm.LoadToolbarLayout(saved);

        Assert.Equal(
            new[] { ToolbarCommandIds.Refresh, ToolbarCommandIds.Find },
            vm.ToolbarEntries.Select(entry => entry.CommandId));
    }

    [Fact]
    public void AiDescriptionPlugin_NoLongerContributesToolsMenuItems()
    {
        var plugin = ReadRepoFile("Plugins", "SkyCD.Plugin.Ai.Description", "AiDescriptionPlugin.cs");

        Assert.DoesNotContain("Generate AI description for selected file", plugin, StringComparison.Ordinal);
        Assert.DoesNotContain("AI description configuration", plugin, StringComparison.Ordinal);
        Assert.DoesNotContain("IMenuPluginCapability", plugin, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionsWindow_ExposesAiScanExclusionSettings()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "OptionsWindow.axaml");

        Assert.Contains("IsVisible=\"{Binding ShowAiDescriptionSection}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding AiScanMaxFileSizeMegabytes, UpdateSourceTrigger=PropertyChanged}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding AiScanExclusions}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ResetAiScanExclusionsCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AddAiScanExclusionCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RemoveAiScanExclusionCommand}\"", xaml, StringComparison.Ordinal);

        foreach (var language in new[] { "en", "lt" })
        {
            var ui = ReadRepoFile("src", "SkyCD.UI", "Translations", $"{language}-UI.yaml");
            var text = ReadRepoFile("src", "SkyCD.UI", "Translations", $"{language}-Text.yaml");

            foreach (var key in new[]
                     {
                         "ui.AiScanSettings:", "ui.AiMimeExclusions:", "ui.MimeType:",
                         "ui.MaxAiScanFileSize:", "ui.AddMimeType:", "ui.RemoveMimeType:",
                         "ui.ResetMimeTypesTooltip:", "ui.ResetMimeTypesTitle:",
                         "ui.ResetMimeTypesConfirmation:", "ui.NewMimeType:"
                     })
            {
                Assert.Contains(key, ui, StringComparison.Ordinal);
            }

            Assert.Contains("text.AllMimeTypesNeedValue:", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OptionsWindow_ExposesSeparateAiProviderCategory()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "OptionsWindow.axaml");

        Assert.Contains("IsVisible=\"{Binding ShowAiProviderSection}\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "Text=\"{i18n:Translate {x:Static i18n:UiKeys.AiProviderCategory}}\"", xaml, StringComparison.Ordinal);

        foreach (var language in new[] { "en", "lt" })
        {
            var ui = ReadRepoFile("src", "SkyCD.UI", "Translations", $"{language}-UI.yaml");
            Assert.Contains("ui.AiProviderCategory:", ui, StringComparison.Ordinal);
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
                         "ui.AiDescription:", "ui.Description:", "ui.DescriptionEmpty:",
                         "ui.DescriptionCatalogSource:", "ui.Generate:", "ui.Regenerate:",
                         "ui.AiDescriptionStale:", "ui.DestinationUnknown:"
                     })
            {
                Assert.Contains(key, ui, StringComparison.Ordinal);
            }

            foreach (var key in new[]
                     {
                         "text.AiDescriptionProvider:",
                         "text.AiDescriptionProvenance:", "text.AiDescriptionBatchSummary:"
                     })
            {
                Assert.Contains(key, text, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("text.GenerateAiDescriptions:", text, StringComparison.Ordinal);
            Assert.DoesNotContain("text.AiDescriptionAdoptConfirmation:", text, StringComparison.Ordinal);

            Assert.Contains("metadata.AiDescription:", metadata, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PropertiesDialog_ShowsUnifiedDescriptionAndCopiesWithoutTouchingComments()
    {
        using var stack = new DescriptionStack();
        var service = stack.Service("entry", "Generated text");
        var dialog = new PropertiesDialogViewModel(
            "item", "entry", string.Empty, "Existing comment", new PropertiesCollection());
        dialog.InitializeDescription(service, "entry");
        string? copied = null;
        dialog.CopyTextRequested += text => copied = text;

        Assert.True(dialog.HasDescription);
        Assert.Equal("Generated text", dialog.InfoProperties[dialog.DescriptionHeader]);
        Assert.Single(dialog.InfoProperties);
        Assert.NotNull(dialog.Description);
        Assert.Equal("Generated text", dialog.Description!.DescriptionText);
        Assert.Equal("Existing comment", dialog.Comments);

        dialog.Description.CopyCommand.Execute(null);

        Assert.Equal("Generated text", copied);
        Assert.Equal("Existing comment", dialog.Comments);
        Assert.Equal("Existing comment", stack.Catalog.Get("entry")!.Properties["Comment"]);
    }

    [Fact]
    public void PropertiesDialog_ImportedDescriptionWinsAndShowsOnceInThePropertyList()
    {
        using var stack = new DescriptionStack();
        var dialog = new PropertiesDialogViewModel(
            "item", "entry", string.Empty, "Existing comment",
            new PropertiesCollection { ["Description"] = "Imported description", ["Size"] = 10 });

        Assert.True(dialog.HasDescription);
        Assert.NotNull(dialog.Description);
        Assert.Equal("Imported description", dialog.Description!.DescriptionText);
        Assert.Equal(DescriptionCatalogSourceText, dialog.Description.SourceText);
        Assert.False(dialog.Description.IsAiSourced);
        Assert.False(dialog.Description.HasActions);
        Assert.True(dialog.InfoProperties.ContainsKey("Size"));

        // Exactly one description row, carrying the stored value; AI text never replaces it.
        dialog.InitializeDescription(stack.Service("entry", "Generated text"), "entry");
        var row = Assert.Single(dialog.InfoProperties,
            item => string.Equals(item.Key, dialog.DescriptionHeader, StringComparison.CurrentCultureIgnoreCase));
        Assert.Equal("Imported description", row.Value);
        Assert.Equal("Imported description", dialog.Description.DescriptionText);
        Assert.False(dialog.Description.CanGenerate);
    }

    private static string DescriptionCatalogSourceText =>
        SkyCD.UI.Localization.LocalizationService.Current.Get(SkyCD.UI.Localization.UiKeys.DescriptionCatalogSource);

    private sealed class DescriptionStack : IDisposable
    {
        private readonly AppDataSandbox sandbox = new();
        private readonly Container container;

        public DescriptionStack()
        {
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
            sandbox.Dispose();
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
