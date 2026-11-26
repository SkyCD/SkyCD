using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Couchbase.Lite;
using SkyCD.App.Views;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Feature.Loans;
using SkyCD.Plugin.Feature.Loans.Models;
using SkyCD.Plugin.Feature.Loans.Views;
using SkyCD.Presentation.Catalog;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.SDK.Capabilities.Options;
using SkyCD.TestSupport;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.UI.Tests;

public sealed class OptionsContributedPageLayoutTests
{
    [AvaloniaFact]
    public void ContributedPage_RendersItsContentInsideOptions()
    {
        var capability = new TestCapability();
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPlugins([
            new OptionsPluginItem("Locations", "test", "test", id: TestCapability.PluginId, isEnabled: true)
        ]);
        viewModel.SetPluginOptionsPages([
            new OptionsPageProvider(TestCapability.PluginId, "Locations", capability)
        ]);
        viewModel.SelectedSettingCategory = viewModel.SettingCategories
            .Single(category => category.ContributedPage is not null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.ShowContributedOptionsPageSection);

        var window = new OptionsWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            Assert.Equal(1, capability.CreateCount);
            var rendered = window.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(border => ReferenceEquals(border, capability.Content));
            Assert.NotNull(rendered);
            Assert.True(rendered!.IsEffectivelyVisible);

            // Issue #1213: the host section no longer prints the page title above the content.
            var section = window.GetVisualDescendants().OfType<ContentControl>()
                .First(control => ReferenceEquals(control.Content, capability.Content))
                .Parent as Grid;
            Assert.NotNull(section);
            Assert.DoesNotContain(section!.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == "Locations");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task LoansContactsMenuSelectsTheEmbeddedManager()
    {
        const string pluginId = "skycd.loans";
        using var workspace = new TestWorkspace();
        using var database = new Database("options-contacts", new DatabaseConfiguration { Directory = workspace.Root });
        var collection = database.GetDefaultCollection();
        var repository = new CatalogDocumentRepository();
        repository.Initialize(typeof(CatalogDocument), collection.Name, collection);
        var catalog = new OpenCatalogState { Path = "test-catalog" };
        catalog.Attach(repository);

        var plugin = new LoansPlugin();
        plugin.InitializeCatalogRepository(catalog);
        var capability = Assert.IsAssignableFrom<IOptionsPageCapability>(plugin);
        Assert.Equal("contacts", capability.OptionsPage.PageId);
        Assert.Equal(LocalizationService.Current.Get(LoansKeys.Contacts), capability.OptionsPage.Title);
        Assert.IsType<ContactsView>(capability.CreateOptionsPage());

        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPlugins([new OptionsPluginItem("Loans", "test", "test", id: pluginId, isEnabled: true)]);
        viewModel.SetPluginOptionsPages([new OptionsPageProvider(pluginId, "Loans", capability)]);
        var category = Assert.Single(viewModel.SettingCategories, item => item.Id == "options:skycd.loans/contacts");
        plugin.InitializeOptionsNavigation(pageId =>
        {
            var categoryId = OptionsContributedPage.GetCategoryId(pluginId, pageId);
            viewModel.SelectedSettingCategory = viewModel.SettingCategories.Single(item => item.Id == categoryId);
            return Task.CompletedTask;
        });

        await plugin.ExecuteMenuCommandAsync("skycd.loans.contacts", new MenuCommandContext());
        Assert.Equal(category.Id, viewModel.SelectedSettingCategory?.Id);
        Dispatcher.UIThread.RunJobs();

        var window = new OptionsWindow { DataContext = viewModel };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var view = Assert.Single(window.GetVisualDescendants().OfType<ContactsView>());
            Assert.True(view.IsEffectivelyVisible);
            var contacts = Assert.IsType<ContactsDialogViewModel>(view.DataContext);
            contacts.AddContactCommand.Execute(null);
            Assert.Empty(contacts.Error);
            Assert.Single(contacts.Contacts);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class TestCapability : IOptionsPageCapability
    {
        public const string PluginId = "skycd.locations";

        public int CreateCount { get; private set; }
        public Border Content { get; } = new();
        public OptionsPageDescriptor OptionsPage => new("locations", "Locations");
        public object CreateOptionsPage()
        {
            CreateCount++;
            return Content;
        }
    }
}
