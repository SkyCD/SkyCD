using System.Globalization;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Tests;

public class PropertiesDialogViewModelTests
{
    [Fact]
    public void Constructor_InitializesAllProperties()
    {
        var infoItems = new Dictionary<string, object?> { ["Size"] = "1024 KB" };
        var vm = new PropertiesDialogViewModel(
            "file123",
            "document.pdf",
            "folder",
            "Important document",
            infoItems);

        Assert.Equal("file123", vm.ObjectKey);
        Assert.Equal("document.pdf", vm.Name);
        Assert.Equal("folder", vm.IconGlyph);
        Assert.Equal("Important document", vm.Comments);
        Assert.Single(vm.InfoProperties);
    }

    [Fact]
    public void DialogAccepted_DefaultsToFalse()
    {
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "comments", new Dictionary<string, object?>());

        Assert.False(vm.DialogAccepted);
    }

    [Fact]
    public void HasInfoTab_IsTrueWhenInfoPropertiesNotEmpty()
    {
        var infoItems = new Dictionary<string, object?> { ["Property"] = "Value" };
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "comments", infoItems);

        Assert.True(vm.HasInfoTab);
    }

    [Fact]
    public void HasInfoTab_IsFalseWhenInfoPropertiesEmpty()
    {
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "comments", new Dictionary<string, object?>());

        Assert.False(vm.HasInfoTab);
    }

    [Fact]
    public void Comments_CanBeModified()
    {
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "initial", new Dictionary<string, object?>());

        vm.Comments = "updated comments";

        Assert.Equal("updated comments", vm.Comments);
    }

    [Fact]
    public void Name_CanBeModified()
    {
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "initial", new Dictionary<string, object?>());

        vm.Name = "renamed";

        Assert.Equal("renamed", vm.Name);
    }

    [Fact]
    public void ConfirmCommand_SetsDialogAcceptedTrue()
    {
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "comments", new Dictionary<string, object?>());

        vm.ConfirmCommand.Execute(null);

        Assert.True(vm.DialogAccepted);
    }

    [Fact]
    public void Constructor_NormalizesEmptyValuesToUnknown()
    {
        var vm = new PropertiesDialogViewModel(
            "key",
            "name",
            "icon",
            "comments",
            new Dictionary<string, object?> { ["Size"] = string.Empty });

        Assert.Equal("Unknown", vm.InfoProperties["Size"]);
    }

    [Fact]
    public void Constructor_SortsInfoPropertiesAscendingByProperty()
    {
        var vm = new PropertiesDialogViewModel(
            "key",
            "name",
            "icon",
            "comments",
            new Dictionary<string, object?>
            {
                ["Zeta"] = "1",
                ["Alpha"] = "2",
                ["Middle"] = "3"
            });

        Assert.Equal(["Alpha", "Middle", "Zeta"], vm.InfoProperties.Keys);
    }

    [Fact]
    public void Constructor_LocalizesBooleanValuesForLithuanian()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("lt-LT");
            var vm = new PropertiesDialogViewModel(
                "key",
                "name",
                "icon",
                "comments",
                new Dictionary<string, object?> { ["Flag"] = true });

            Assert.Equal("Taip", vm.InfoProperties["Flag"]);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}