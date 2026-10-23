using System.Linq;
using System.Text.Json;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class CustomPropertyTests
{
    [Fact]
    public void SettingsRoundTrip_IsDetachedAndTrimsNames()
    {
        var options = new AppOptionsDocument
        {
            CustomProperties = [new() { Name = " Owner ", DefaultValue = "Alex" }]
        };
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetCustomProperties(options.CustomProperties);
        vm.CustomProperties[0].DefaultValue = "Sam";
        Assert.Equal("Alex", options.CustomProperties[0].DefaultValue);
        options.CustomProperties = vm.GetCustomProperties();
        var restored = JsonSerializer.Deserialize<AppOptionsDocument>(JsonSerializer.Serialize(options))!;
        Assert.Equal("Owner", Assert.Single(restored.CustomProperties).Name);
        Assert.Equal("Sam", restored.CustomProperties[0].DefaultValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("owner")]
    [InlineData(" Owner ")]
    public void InvalidNames_PreventSaving(string name)
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetCustomProperties([new() { Name = "Owner" }, new() { Name = name }]);
        vm.ConfirmCommand.Execute(null);
        Assert.False(vm.DialogAccepted);
        Assert.NotEmpty(vm.CustomPropertyAlertMessage);
    }

    [Fact]
    public void AddRemoveAndSearch_ExposeCustomProperties()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SettingsSearchText = "Custom properties";
        Assert.True(vm.ShowCustomPropertiesSection);
        vm.AddCustomPropertyCommand.Execute(null);
        vm.SelectedCustomProperty!.Name = "Owner";
        vm.ConfirmCommand.Execute(null);
        Assert.True(vm.DialogAccepted);
        vm.RemoveCustomPropertyCommand.Execute(null);
        Assert.Empty(vm.CustomProperties);
    }

    [Fact]
    public void Defaults_PreserveExistingValuesAndOnlyApplyToAcceptedSnapshot()
    {
        var source = new PropertiesCollection { ["Owner"] = "Sam", ["Count"] = 42 };
        var vm = new PropertiesDialogViewModel("id", "Item", "", "", source);
        vm.ApplyCustomPropertyDefaults([
            new() { Name = "owner", DefaultValue = "Alex" },
            new() { Name = "Count", DefaultValue = "0" },
            new() { Name = "Rating", DefaultValue = "Good" }]);
        Assert.True(vm.HasCustomProperties);
        Assert.Equal("Sam", vm.CustomProperties.First(p => p.Name == "Owner").DefaultValue);
        Assert.False(source.ContainsKey("Rating"));
        vm.CustomProperties.First(p => p.Name == "Rating").DefaultValue = "Great";
        var result = vm.GetProperties();
        Assert.Equal("Great", result["Rating"]);
        Assert.Equal(42, Assert.IsType<int>(result["Count"]));
        Assert.Equal("Sam", result["Owner"]);
    }
}
