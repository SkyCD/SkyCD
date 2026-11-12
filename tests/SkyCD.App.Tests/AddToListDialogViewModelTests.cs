using System;
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
        public event EventHandler? DrivesChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<MediaDriveInfo> GetSupportedDrives() => drives;
        public MediaDriveInfo? GetDrive(string drivePathOrName) => drives.FirstOrDefault(d => d.Name == drivePathOrName || d.RootDirectory == drivePathOrName);
    }

    private sealed class DynamicDriveProvider(IReadOnlyList<MediaDriveInfo> initialDrives) : IMediaDriveProvider
    {
        private List<MediaDriveInfo> drives = new(initialDrives);

        public event EventHandler? DrivesChanged;

        public IReadOnlyList<MediaDriveInfo> GetSupportedDrives() => drives;

        public MediaDriveInfo? GetDrive(string drivePathOrName) =>
            drives.FirstOrDefault(d => d.Name == drivePathOrName || d.RootDirectory == drivePathOrName);

        public void SetDrives(IEnumerable<MediaDriveInfo> newDrives, bool raiseEvent = true)
        {
            drives = new List<MediaDriveInfo>(newDrives);
            if (raiseEvent)
            {
                DrivesChanged?.Invoke(this, EventArgs.Empty);
            }
        }
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
    public void RefreshDrives_WhenNewDriveAdded_AppendsToAvailableDrivesAndPreservesSelectedDrive()
    {
        var driveD = new MediaDriveInfo("D:\\", "CD_ROM", "D:\\", DriveType.CDRom, true, 0, 0, 700_000_000);
        var driveE = new MediaDriveInfo("E:\\", "USB_DRIVE", "E:\\", DriveType.Removable, true, 1000, 1000, 10000);
        var provider = new DynamicDriveProvider([driveD]);

        using var vm = new AddToListDialogViewModel(provider);
        Assert.Single(vm.AvailableDrives);
        Assert.Equal("D:\\", vm.SelectedDrive?.Name);

        // Plug in new USB drive
        provider.SetDrives([driveD, driveE]);

        Assert.Equal(2, vm.AvailableDrives.Count);
        Assert.Equal("D:\\", vm.SelectedDrive?.Name);
        Assert.Equal("E:\\", vm.AvailableDrives[1].Name);
    }

    [Fact]
    public void RefreshDrives_WhenSelectedDriveRemoved_SelectsRemainingDrive()
    {
        var driveD = new MediaDriveInfo("D:\\", "CD_ROM", "D:\\", DriveType.CDRom, true, 0, 0, 700_000_000);
        var driveE = new MediaDriveInfo("E:\\", "USB_DRIVE", "E:\\", DriveType.Removable, true, 1000, 1000, 10000);
        var provider = new DynamicDriveProvider([driveD, driveE]);

        using var vm = new AddToListDialogViewModel(provider);
        vm.SelectedDrive = vm.AvailableDrives[1]; // Select E:\
        Assert.Equal("E:\\", vm.SelectedDrive.Name);

        // Unplug E:\
        provider.SetDrives([driveD]);

        Assert.Single(vm.AvailableDrives);
        Assert.NotNull(vm.SelectedDrive);
        Assert.Equal("D:\\", vm.SelectedDrive.Name);
    }

    [Fact]
    public void RefreshDrives_WhenAllDrivesRemoved_ClearsSelection()
    {
        var driveD = new MediaDriveInfo("D:\\", "CD_ROM", "D:\\", DriveType.CDRom, true, 0, 0, 700_000_000);
        var provider = new DynamicDriveProvider([driveD]);

        using var vm = new AddToListDialogViewModel(provider);
        Assert.NotNull(vm.SelectedDrive);

        // Unplug all drives
        provider.SetDrives([]);

        Assert.Empty(vm.AvailableDrives);
        Assert.Null(vm.SelectedDrive);
    }

    [Fact]
    public void RefreshDrives_WhenDriveStateChanged_UpdatesDriveInfoInListAndSelectedDrive()
    {
        var driveDInitial = new MediaDriveInfo("D:\\", null, "D:\\", DriveType.CDRom, false, 0, 0, 0);
        var provider = new DynamicDriveProvider([driveDInitial]);

        using var vm = new AddToListDialogViewModel(provider);
        Assert.False(vm.SelectedDrive?.IsReady);
        Assert.Equal("D:", vm.SelectedDrive?.DisplayName);

        // Disc inserted into drive D
        var driveDUpdated = new MediaDriveInfo("D:\\", "InsertedDisc", "D:\\", DriveType.CDRom, true, 0, 0, 650_000_000);
        provider.SetDrives([driveDUpdated]);

        Assert.True(vm.SelectedDrive?.IsReady);
        Assert.Equal("InsertedDisc", vm.SelectedDrive?.VolumeLabel);
        Assert.Equal("D: [InsertedDisc]", vm.SelectedDrive?.DisplayName);
    }

    [Fact]
    public void Dispose_UnsubscribesFromDrivesChangedEvent()
    {
        var driveD = new MediaDriveInfo("D:\\", "CD_ROM", "D:\\", DriveType.CDRom, true, 0, 0, 700_000_000);
        var driveE = new MediaDriveInfo("E:\\", "USB_DRIVE", "E:\\", DriveType.Removable, true, 1000, 1000, 10000);
        var provider = new DynamicDriveProvider([driveD]);

        var vm = new AddToListDialogViewModel(provider);
        Assert.Single(vm.AvailableDrives);

        vm.Dispose();

        provider.SetDrives([driveD, driveE]);
        Assert.Single(vm.AvailableDrives); // Should not have updated after dispose
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
    public void TitleOnlySource_RequiresOnlyAMediaName()
    {
        var provider = new FakeDriveProvider([]);
        var vm = new AddToListDialogViewModel(provider, AddToListSourceMode.TitleOnly);

        Assert.True(vm.IsSourceTitleOnly);
        Assert.False(vm.IsScanOptionsEnabled);
        Assert.False(vm.CanConfirm);
        Assert.Equal("A media name is required.", vm.ValidationMessage);

        vm.MediaName = "   ";
        Assert.False(vm.CanConfirm);

        vm.MediaName = "Office Backup";
        Assert.True(vm.CanConfirm);
        Assert.Null(vm.SelectedDrive);
        vm.ConfirmCommand.Execute(null);
        Assert.True(vm.DialogAccepted);
    }

    [Fact]
    public void TitleOnlySource_CancelLeavesDialogUnaccepted()
    {
        var vm = new AddToListDialogViewModel(new FakeDriveProvider([]))
        {
            SourceMode = AddToListSourceMode.TitleOnly,
            MediaName = "Unsaved Disc"
        };

        Assert.True(vm.CanConfirm);
        Assert.False(vm.DialogAccepted);
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