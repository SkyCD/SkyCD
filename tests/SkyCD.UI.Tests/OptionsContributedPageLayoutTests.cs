using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Couchbase.Lite;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Feature.Loans;
using SkyCD.Plugin.Feature.Loans.Models;
using SkyCD.Plugin.Feature.Loans.Views;
using SkyCD.Presentation.Catalog;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.TestSupport;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Issue #1340: plugin Options pages are configuration pages, so the Loans contacts manager
/// arrives through <see cref="IPluginConfigurationCapability"/> and the menu opens that page.
/// </summary>
public sealed class OptionsContributedPageLayoutTests
{
    [AvaloniaFact]
    public async Task LoansContactsPage_RendersTheManagerAndAcceptsVerbatimSettings()
    {
        using var workspace = new TestWorkspace();
        using var database = new Database("options-contacts", new DatabaseConfiguration { Directory = workspace.Root });
        var collection = database.GetDefaultCollection();
        var repository = new CatalogDocumentRepository();
        repository.Initialize(typeof(CatalogDocument), collection.Name, collection);
        var catalog = new OpenCatalogState { Path = "test-catalog" };
        catalog.Attach(repository);

        var plugin = new LoansPlugin();
        plugin.InitializeCatalogRepository(catalog);
        var capability = Assert.IsAssignableFrom<IPluginConfigurationCapability>(plugin);
        var configuration = Assert.Single(capability.Configurations);
        Assert.Equal("contacts", configuration.ConfigurationId);
        Assert.Equal(LocalizationService.Current.Get(LoansKeys.Contacts), configuration.Title);
        Assert.Equal(-30, configuration.Order);

        var current = new Dictionary<string, string?> { ["unrelated"] = "value" };
        using var page = await capability.CreatePageAsync(new PluginConfigurationRequest
        {
            ConfigurationId = configuration.ConfigurationId,
            CurrentSettings = current
        });
        var view = Assert.IsType<ContactsView>(page.Content);
        var contacts = Assert.IsType<ContactsDialogViewModel>(view.DataContext);
        contacts.AddContactCommand.Execute(null);
        Assert.Empty(contacts.Error);
        Assert.Single(contacts.Contacts);

        var result = await page.AcceptAsync();

        Assert.True(result.Success);
        Assert.Same(current, result.Settings);
    }

    [AvaloniaFact]
    public async Task LoansContactsMenu_OpensTheConfigurationPage()
    {
        var plugin = new LoansPlugin();
        string? opened = null;
        plugin.InitializeOptionsNavigation(pageId =>
        {
            opened = pageId;
            return Task.CompletedTask;
        });

        await plugin.ExecuteMenuCommandAsync("skycd.loans.contacts", new MenuCommandContext());

        Assert.Equal("contacts", opened);
    }

    [AvaloniaFact]
    public async Task UnsupportedConfigurationId_ReportsAnInlineError()
    {
        var plugin = new LoansPlugin();

        using var page = await plugin.CreatePageAsync(new PluginConfigurationRequest
        {
            ConfigurationId = "missing"
        });
        var result = await page.AcceptAsync();

        Assert.False(result.Success);
        Assert.Contains("missing", result.Error);
    }
}
