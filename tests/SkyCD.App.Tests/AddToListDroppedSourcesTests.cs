using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class AddToListDroppedSourcesTests
{
    private sealed class FakeDriveProvider(IReadOnlyList<MediaDriveInfo> drives) : IMediaDriveProvider
    {
        public event EventHandler? DrivesChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<MediaDriveInfo> GetSupportedDrives() => drives;

        public MediaDriveInfo? GetDrive(string drivePathOrName) =>
            drives.FirstOrDefault(drive => drive.Name == drivePathOrName || drive.RootDirectory == drivePathOrName);
    }

    private static MediaDriveInfo Drive(string name, string root, string? label = null) =>
        new(name, label, root, DriveType.CDRom, true, 0, 0, 0);

    [Fact]
    public void SingleFolder_PrefillsFolderModeAndName()
    {
        using var vm = new AddToListDialogViewModel(new FakeDriveProvider([]));

        vm.ApplyDroppedSources([new DroppedSource(AddToListSourceMode.Folder, "/media/My Disc", "My Disc")]);

        Assert.Equal(AddToListSourceMode.Folder, vm.SourceMode);
        Assert.Equal("/media/My Disc", vm.SourceValue);
        Assert.Equal("My Disc", vm.MediaName);
        Assert.Single(vm.DroppedRoots);
        Assert.False(vm.HasMultipleDroppedRoots);
    }

    [Fact]
    public void MultipleFolders_ExposeAPickerWithTheFirstRootSelected()
    {
        using var vm = new AddToListDialogViewModel(new FakeDriveProvider([]));
        var sources = new[]
        {
            new DroppedSource(AddToListSourceMode.Folder, "/media/One", "One"),
            new DroppedSource(AddToListSourceMode.Folder, "/media/Two", "Two"),
        };

        vm.ApplyDroppedSources(sources);

        Assert.True(vm.HasMultipleDroppedRoots);
        Assert.Equal(2, vm.DroppedRoots.Count);
        Assert.Same(sources[0], vm.SelectedDroppedRoot);
        Assert.Equal("/media/One", vm.SourceValue);

        vm.SelectedDroppedRoot = vm.DroppedRoots[1];

        Assert.Equal("/media/Two", vm.SourceValue);
        Assert.Equal("Two", vm.MediaName);
        Assert.Equal(AddToListSourceMode.Folder, vm.SourceMode);
    }

    [Fact]
    public void DriveSource_SelectsTheMatchingAvailableDrive()
    {
        var drive = Drive("E:\\", "E:\\", "AUDIO");
        using var vm = new AddToListDialogViewModel(new FakeDriveProvider([drive]));

        vm.ApplyDroppedSources([new DroppedSource(AddToListSourceMode.Media, "E:\\", "AUDIO")]);

        Assert.Equal(AddToListSourceMode.Media, vm.SourceMode);
        Assert.NotNull(vm.SelectedDrive);
        Assert.Equal("E:\\", vm.SelectedDrive!.Name);
        Assert.Equal("AUDIO", vm.MediaName);
    }

    [Fact]
    public void DriveSource_WithoutMatchingDrive_FallsBackToFolder()
    {
        using var vm = new AddToListDialogViewModel(new FakeDriveProvider([]));

        vm.ApplyDroppedSources([new DroppedSource(AddToListSourceMode.Media, "E:\\", "AUDIO")]);

        Assert.Equal(AddToListSourceMode.Folder, vm.SourceMode);
        Assert.Equal("E:\\", vm.SourceValue);
        Assert.Equal("AUDIO", vm.MediaName);
    }

    [Theory]
    [InlineData("https://example.com/media", "webdavs", "/media")]
    [InlineData("http://example.com/media", "webdav", "/media")]
    public void HttpUrlSource_PrefillsInternetModeThroughWebDav(string url, string expectedProtocol, string expectedPath)
    {
        using var vm = new AddToListDialogViewModel(new FakeDriveProvider([]));

        vm.ApplyDroppedSources([new DroppedSource(AddToListSourceMode.Internet, url, null)]);

        Assert.Equal(AddToListSourceMode.Internet, vm.SourceMode);
        Assert.Equal(expectedProtocol, vm.InternetProtocol);
        Assert.Equal("example.com", vm.InternetHost);
        Assert.Equal(expectedPath, vm.InternetPath);
    }

    [Fact]
    public void RemoteCatalogUrlSource_PrefillsInternetModeDirectly()
    {
        using var vm = new AddToListDialogViewModel(new FakeDriveProvider([]));

        vm.ApplyDroppedSources([new DroppedSource(AddToListSourceMode.Internet, "ftp://example.com/media", null)]);

        Assert.Equal(AddToListSourceMode.Internet, vm.SourceMode);
        Assert.Equal("ftp", vm.InternetProtocol);
        Assert.Equal("example.com", vm.InternetHost);
        Assert.Equal("/media", vm.InternetPath);
    }
}
