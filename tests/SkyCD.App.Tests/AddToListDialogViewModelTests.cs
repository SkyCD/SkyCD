using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Tests;

public class AddToListDialogViewModelTests
{
    [Fact]
    public void MediaSource_RequiresMediaName()
    {
        var vm = new AddToListDialogViewModel
        {
            SourceMode = AddToListSourceMode.Media,
            MediaName = ""
        };

        Assert.False(vm.CanConfirm);
        Assert.Equal("Media name is required for media source.", vm.ValidationMessage);
    }

    [Fact]
    public void FolderSource_RequiresFolderPath()
    {
        var vm = new AddToListDialogViewModel
        {
            SourceMode = AddToListSourceMode.Folder,
            SourceValue = ""
        };

        Assert.False(vm.CanConfirm);
        Assert.Equal("Folder path is required for folder source.", vm.ValidationMessage);
    }

    [Fact]
    public void InternetSource_RequiresAddress()
    {
        var vm = new AddToListDialogViewModel
        {
            SourceMode = AddToListSourceMode.Internet,
            SourceValue = ""
        };

        Assert.False(vm.CanConfirm);
        Assert.Equal("Address is required for internet source.", vm.ValidationMessage);
    }

    [Fact]
    public void NewMediaTarget_RequiresMediaNameForFolderFlow()
    {
        var vm = new AddToListDialogViewModel
        {
            SourceMode = AddToListSourceMode.Folder,
            SourceValue = "C:\\Music",
            TargetPlacement = AddToListTargetPlacement.NewMedia,
            MediaName = ""
        };

        Assert.False(vm.CanConfirm);
        Assert.Equal("Media name is required when adding as new media.", vm.ValidationMessage);
    }

    [Fact]
    public void ValidFolderFlow_CanConfirmAndAccept()
    {
        var vm = new AddToListDialogViewModel
        {
            SourceMode = AddToListSourceMode.Folder,
            SourceValue = "C:\\Music",
            MediaName = "My Media",
            TargetPlacement = AddToListTargetPlacement.NewMedia
        };

        Assert.True(vm.CanConfirm);
        vm.ConfirmCommand.Execute(null);
        Assert.True(vm.DialogAccepted);
    }
}
