using System;
using System.Collections.Generic;
using System.Globalization;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class LocalizationViewModelTests : IDisposable
{
    private readonly CultureInfo originalServiceCulture = LocalizationService.Current.CurrentCulture;
    private readonly CultureInfo originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
    private readonly CultureInfo? originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

    public void Dispose()
    {
        LocalizationService.Current.SetCulture(originalServiceCulture);
        CultureInfo.CurrentCulture = originalCulture;
        CultureInfo.CurrentUICulture = originalUiCulture;
        CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
        CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
    }

    [Fact]
    public void StatusVariant_CultureChangePreservesSelectedEnumValues()
    {
        LocalizationService.Current.SetCulture(CultureInfo.GetCultureInfo("lt"));
        var vm = new StatusVariantItemViewModel { Name = "Favorite" };
        vm.SetTypeSelected(CatalogDocumentType.File, true);
        vm.SetTypeSelected(CatalogDocumentType.Folder, true);
        var lithuanianDisplay = vm.SelectedItemTypesDisplayText;

        LocalizationService.Current.SetCulture(CultureInfo.GetCultureInfo("en"));

        Assert.NotEqual(lithuanianDisplay, vm.SelectedItemTypesDisplayText);
        Assert.Equal("File, Folder", vm.SelectedItemTypesDisplayText);
        Assert.True(vm.IsTypeSelected(CatalogDocumentType.File));
        Assert.True(vm.IsTypeSelected(CatalogDocumentType.Folder));
        Assert.Equal(new[] { CatalogDocumentType.File, CatalogDocumentType.Folder }, vm.ToDocument().ItemTypes);

        var reloaded = StatusVariantItemViewModel.FromDocument(vm.ToDocument());
        Assert.Equal(vm.ToDocument().ItemTypes, reloaded.ToDocument().ItemTypes);
        reloaded.SetTypeSelected(CatalogDocumentType.File, false);
        Assert.Equal(new[] { CatalogDocumentType.Folder }, reloaded.ToDocument().ItemTypes);
    }

    [Fact]
    public void Properties_CultureChangeRefreshesBooleanAndNullValuesFromRawSource()
    {
        LocalizationService.Current.SetCulture(CultureInfo.GetCultureInfo("en"));
        var source = new PropertiesCollection(new Dictionary<string, object?>
        {
            ["Enabled"] = true,
            ["Disabled"] = false,
            ["Missing"] = null
        });
        var vm = new PropertiesDialogViewModel("key", "Name", "file", "", source);
        Assert.Equal("Yes", vm.InfoProperties["Enabled"]);
        Assert.Equal("No", vm.InfoProperties["Disabled"]);
        var originalMissing = vm.InfoProperties["Missing"];
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        LocalizationService.Current.SetCulture(CultureInfo.GetCultureInfo("lt"));

        Assert.Equal("Taip", vm.InfoProperties["Enabled"]);
        Assert.Equal("Ne", vm.InfoProperties["Disabled"]);
        Assert.NotEqual(originalMissing, vm.InfoProperties["Missing"]);
        Assert.Contains(nameof(PropertiesDialogViewModel.InfoProperties), notifications);
        Assert.True(vm.HasInfoTab);

        LocalizationService.Current.SetCulture(CultureInfo.GetCultureInfo("en"));

        Assert.Equal("Yes", vm.InfoProperties["Enabled"]);
        Assert.Equal("No", vm.InfoProperties["Disabled"]);
        Assert.Equal(originalMissing, vm.InfoProperties["Missing"]);
        Assert.Equal(true, source["Enabled"]);
        Assert.Null(source["Missing"]);
    }
}
