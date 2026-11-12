using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class AddToListDialogViewModelTests
{
    private sealed class FakeDriveProvider(IReadOnlyList<MediaDriveInfo> drives) : IMediaDriveProvider
    {
        public IReadOnlyList<MediaDriveInfo> GetSupportedDrives() => drives;
        public MediaDriveInfo? GetDrive(string drivePathOrName) => drives.FirstOrDefault(d => d.Name == drivePathOrName || d.RootDirectory == drivePathOrName);
    }

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
    public void MediaSource_WithDrives_PopulatesAvailableDrivesAndSelectsFirst()
    {
        var drives = new List<MediaDriveInfo>
        {
            new("D:\\", "AudioCD", "D:\\", DriveType.CDRom, true, 0, 0, 700_000_000, "CDFS"),
            new("E:\\", "USB_DRIVE", "E:\\", DriveType.Removable, true, 1_000_000, 1_000_000, 16_000_000_000, "FAT32")
        };
        var provider = new FakeDriveProvider(drives);
        var vm = new AddToListDialogViewModel(provider);

        Assert.Equal(2, vm.AvailableDrives.Count);
        Assert.NotNull(vm.SelectedDrive);
        Assert.Equal("D:\\", vm.SelectedDrive.Name);
        Assert.Equal("AudioCD", vm.MediaName);
    }

    [Fact]
    public void MediaSource_SelectingDrive_UpdatesMediaNameWhenEmpty()
    {
        var drives = new List<MediaDriveInfo>
        {
            new("D:\\", "Disc1", "D:\\", DriveType.CDRom, true, 0, 0, 700_000_000),
            new("E:\\", "FlashDrive", "E:\\", DriveType.Removable, true, 100, 100, 1000)
        };
        var provider = new FakeDriveProvider(drives);
        var vm = new AddToListDialogViewModel(provider)
        {
            MediaName = ""
        };

        vm.SelectedDrive = drives[1];
        Assert.Equal("FlashDrive", vm.MediaName);
    }

    [Fact]
    public void ValidMediaFlow_CanConfirmAndAccept()
    {
        var drives = new List<MediaDriveInfo>
        {
            new("D:\\", "MyDisc", "D:\\", DriveType.CDRom, true, 0, 0, 700_000_000)
        };
        var provider = new FakeDriveProvider(drives);
        var vm = new AddToListDialogViewModel(provider)
        {
            SourceMode = AddToListSourceMode.Media,
            MediaName = "MyDisc",
            TargetPlacement = AddToListTargetPlacement.NewMedia
        };

        Assert.True(vm.CanConfirm);
        vm.ConfirmCommand.Execute(null);
        Assert.True(vm.DialogAccepted);
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