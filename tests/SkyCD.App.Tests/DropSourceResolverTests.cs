using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class DropSourceResolverTests
{
    private static MediaDriveInfo Drive(string name, string root, string? label = null) =>
        new(name, label, root, DriveType.CDRom, true, 0, 0, 0);

    private static IReadOnlyList<DroppedSource> Resolve(
        IEnumerable<DroppedPath> paths,
        string? text = null,
        IReadOnlyList<MediaDriveInfo>? drives = null) =>
        DropSourceResolver.Resolve(paths, text, drives ?? []);

    [Fact]
    public void Folder_MapsToFolderModeWithLeafNameAndPath()
    {
        var source = Assert.Single(Resolve([new DroppedPath("/media/My Discs", true)]));

        Assert.Equal(AddToListSourceMode.Folder, source.Mode);
        Assert.Equal("/media/My Discs", source.Value);
        Assert.Equal("My Discs", source.MediaName);
    }

    [Theory]
    [InlineData("/media/Verbatim/", "/media/Verbatim", "Verbatim")]
    [InlineData(@"D:\Rinkiniai\Įrašai", @"D:\Rinkiniai\Įrašai", "Įrašai")]
    public void Folder_TrailingSeparatorsAreTrimmedAndUnicodeNamesSurvive(
        string dropped, string expectedValue, string expectedName)
    {
        var source = Assert.Single(Resolve([new DroppedPath(dropped, true)]));

        Assert.Equal(AddToListSourceMode.Folder, source.Mode);
        Assert.Equal(expectedValue, source.Value);
        Assert.Equal(expectedName, source.MediaName);
    }

    [Fact]
    public void DriveRoot_WithAvailableDrive_MapsToMediaAndKeepsLabel()
    {
        var drives = new[] { Drive("E:\\", "E:\\", "AUDIO") };

        var source = Assert.Single(Resolve([new DroppedPath("E:\\", true)], drives: drives));

        Assert.Equal(AddToListSourceMode.Media, source.Mode);
        Assert.Equal("E:\\", source.Value);
        Assert.Equal("AUDIO", source.MediaName);
    }

    [Fact]
    public void DriveRoot_WithoutLabel_LeavesMediaNameUnset()
    {
        var drives = new[] { Drive("E:\\", "E:\\") };

        var source = Assert.Single(Resolve([new DroppedPath("E:", true)], drives: drives));

        Assert.Equal(AddToListSourceMode.Media, source.Mode);
        Assert.Null(source.MediaName);
    }

    [Fact]
    public void DriveRoot_ThatIsNotAnAvailableDrive_FallsBackToFolder()
    {
        var source = Assert.Single(Resolve([new DroppedPath("C:\\", true)], drives: [Drive("E:\\", "E:\\")]));

        Assert.Equal(AddToListSourceMode.Folder, source.Mode);
        Assert.Equal("C:\\", source.Value);
    }

    [Fact]
    public void UncPath_MapsToFolderModeWithShareLeafName()
    {
        var source = Assert.Single(Resolve([new DroppedPath(@"\\nas\photos\2026", true)]));

        Assert.Equal(AddToListSourceMode.Folder, source.Mode);
        Assert.Equal(@"\\nas\photos\2026", source.Value);
        Assert.Equal("2026", source.MediaName);
    }

    [Fact]
    public void UncShareRoot_UsesTheShareName()
    {
        var source = Assert.Single(Resolve([new DroppedPath(@"\\nas\photos\", true)]));

        Assert.Equal(AddToListSourceMode.Folder, source.Mode);
        Assert.Equal(@"\\nas\photos", source.Value);
        Assert.Equal("photos", source.MediaName);
    }

    [Theory]
    [InlineData("https://example.com/media")]
    [InlineData("http://example.com/media")]
    [InlineData("ftp://example.com/media")]
    [InlineData("webdavs://example.com/files")]
    public void UrlText_MapsToInternetMode(string url)
    {
        var source = Assert.Single(Resolve([], url));

        Assert.Equal(AddToListSourceMode.Internet, source.Mode);
        Assert.Equal(url, source.Value);
    }

    [Fact]
    public void SmbUrl_FollowsThePlatformSupportOfTheInternetTab()
    {
        var source = DropSourceResolver.TryResolveUrl("smb://nas/photos");

        Assert.Equal(OperatingSystem.IsWindows(), source is not null);
    }

    [Theory]
    [InlineData("just some text")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("example.com/media")]
    [InlineData("")]
    [InlineData("   ")]
    public void UnsupportedText_IsIgnored(string text)
    {
        Assert.Empty(Resolve([], text));
    }

    [Fact]
    public void PlainFileDrop_IsIgnored()
    {
        Assert.Empty(Resolve([new DroppedPath("/media/disc/image.iso", false)]));
    }

    [Fact]
    public void EmptySelection_IsIgnored()
    {
        Assert.Empty(Resolve([]));
    }

    [Fact]
    public void MultipleFolders_AreAllReturnedInOrder()
    {
        var sources = Resolve(
        [
            new DroppedPath("/media/One", true),
            new DroppedPath("/media/Two", true),
            new DroppedPath("/media/Three", true)
        ]);

        Assert.Equal(["One", "Two", "Three"], sources.Select(source => source.MediaName));
    }

    [Fact]
    public void DuplicateFolders_AreCollapsed()
    {
        var sources = Resolve(
        [
            new DroppedPath("/media/One", true),
            new DroppedPath("/media/One/", true)
        ]);

        Assert.Single(sources);
    }

    [Fact]
    public void FileAlongsideFolder_OnlyTheFolderIsKept()
    {
        var sources = Resolve(
        [
            new DroppedPath("/media/One/image.iso", false),
            new DroppedPath("/media/One", true)
        ]);

        Assert.Equal(AddToListSourceMode.Folder, Assert.Single(sources).Mode);
    }

    [Fact]
    public void DroppedPaths_TakePrecedenceOverUrlText()
    {
        var source = Assert.Single(Resolve([new DroppedPath("/media/One", true)], "https://example.com"));

        Assert.Equal(AddToListSourceMode.Folder, source.Mode);
    }
}
