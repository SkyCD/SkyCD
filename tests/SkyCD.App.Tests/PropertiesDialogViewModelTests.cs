using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Tests;

public class PropertiesDialogViewModelTests
{
    [Fact]
    public void Constructor_InitializesAllProperties()
    {
        var infoItems = new[] { new PropertiesInfoItem("Size", "1024 KB") };
        var vm = new PropertiesDialogViewModel(
            objectKey: "file123",
            name: "document.pdf",
            iconGlyph: "📄",
            comments: "Important document",
            infoProperties: infoItems);

        Assert.Equal("file123", vm.ObjectKey);
        Assert.Equal("document.pdf", vm.Name);
        Assert.Equal("📄", vm.IconGlyph);
        Assert.Equal("Important document", vm.Comments);
        Assert.Single(vm.InfoProperties);
    }

    [Fact]
    public void DialogAccepted_DefaultsToFalse()
    {
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "comments", []);

        Assert.False(vm.DialogAccepted);
    }

    [Fact]
    public void HasInfoTab_IsTrueWhenInfoPropertiesNotEmpty()
    {
        var infoItems = new[] { new PropertiesInfoItem("Property", "Value") };
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "comments", infoItems);

        Assert.True(vm.HasInfoTab);
    }

    [Fact]
    public void HasInfoTab_IsFalseWhenInfoPropertiesEmpty()
    {
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "comments", []);

        Assert.False(vm.HasInfoTab);
    }

    [Fact]
    public void Comments_CanBeModified()
    {
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "initial", []);

        vm.Comments = "updated comments";

        Assert.Equal("updated comments", vm.Comments);
    }

    [Fact]
    public void ConfirmCommand_SetsDialogAcceptedTrue()
    {
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "comments", []);

        vm.ConfirmCommand.Execute(null);

        Assert.True(vm.DialogAccepted);
    }
}
