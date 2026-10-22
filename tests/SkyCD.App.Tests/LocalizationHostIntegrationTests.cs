using System;
using System.Globalization;
using DryIoc;
using SkyCD.Core.DependencyInjection;
using SkyCD.Plugin.Abstractions.Localization;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class LocalizationHostIntegrationTests
{
    [Fact]
    public void SharedHostRegistration_IsAvailableToSiblingContainersAndPluginRefresh()
    {
        ServiceProvider.RegisterInstance<ILocalizationService>(LocalizationService.Current);
        using var application = ServiceProvider.RegisterChildContainer(static registrator =>
            registrator.Register<LocalizationConsumer>());
        using var sibling = ServiceProvider.RegisterChildContainer(static registrator =>
            registrator.Register<LocalizationConsumer>());

        Assert.Same(LocalizationService.Current, application.Resolve<LocalizationConsumer>().Service);
        Assert.Same(LocalizationService.Current, sibling.Resolve<LocalizationConsumer>().Service);
        Assert.Same(LocalizationService.Current, ServiceProvider.Resolve<ILocalizationService>());

        ServiceProvider.ReregisterPluginsService();

        Assert.Same(LocalizationService.Current, ServiceProvider.Resolve<ILocalizationService>());
    }

    [Theory]
    [InlineData("Veiksmai")]
    [InlineData("Atnaujinti")]
    [InlineData("Konfigūruoti")]
    public void SettingsSearch_FindsPluginActionsUsingTranslatedLabels(string search)
    {
        var service = LocalizationService.Current;
        var originalServiceCulture = service.CurrentCulture;
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            service.SetCulture(CultureInfo.GetCultureInfo("lt"));
            var viewModel = new OptionsDialogViewModel(["English", "Lithuanian"])
            {
                SettingsSearchText = search
            };

            Assert.True(viewModel.ShowPluginActionsSection);
            Assert.True(viewModel.HasVisibleCategoryContent);
            Assert.False(viewModel.ShowNoSearchResults);
        }
        finally
        {
            service.SetCulture(originalServiceCulture);
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
        }
    }

    public sealed class LocalizationConsumer(ILocalizationService service)
    {
        public ILocalizationService Service { get; } = service;
    }
}
