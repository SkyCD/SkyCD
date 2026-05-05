using System;
using System.IO;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class AboutDialogViewModelTests
{
    [Fact]
    public void Constructor_InitializesWithDefaultValues()
    {
        var vm = new AboutDialogViewModel();

        Assert.Equal("SkyCD", vm.ProductName);
        Assert.Equal("3.0.0", vm.Version);
        Assert.Equal("https://github.com/SkyCD/SkyCD", vm.Website);
    }

    [Fact]
    public void Constructor_InitializesWithCustomValues()
    {
        var vm = new AboutDialogViewModel(
            "MyApp",
            "1.2.3",
            "https://example.com",
            loadedAssemblies: [typeof(string).Assembly],
            baseDirectory: Path.GetTempPath());

        Assert.Equal("MyApp", vm.ProductName);
        Assert.Equal("1.2.3", vm.Version);
        Assert.Equal("https://example.com", vm.Website);
    }

    [Fact]
    public void DialogAccepted_DefaultsToFalse()
    {
        var vm = new AboutDialogViewModel();

        Assert.False(vm.DialogAccepted);
    }

    [Fact]
    public void ConfirmCommand_SetsDialogAcceptedTrue()
    {
        var vm = new AboutDialogViewModel(
            "SkyCD",
            "3.0.0",
            "https://github.com/SkyCD/SkyCD",
            loadedAssemblies: [typeof(string).Assembly],
            baseDirectory: Path.GetTempPath());

        vm.ConfirmCommand.Execute(null);

        Assert.True(vm.DialogAccepted);
    }

    [Fact]
    public void Constructor_UsesLicenseFallback_WhenLicenseFileIsMissing()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"skycd-about-{Guid.NewGuid():N}");
        var expectedMarkdownPath = Path.Combine(baseDirectory, "LICENSE.md");
        var expectedPlainPath = Path.Combine(baseDirectory, "LICENSE");

        var vm = new AboutDialogViewModel(
            "SkyCD",
            "3.0.0",
            "https://github.com/SkyCD/SkyCD",
            loadedAssemblies: [typeof(string).Assembly],
            baseDirectory: baseDirectory);

        Assert.Equal(expectedMarkdownPath, vm.LicensePath);
        Assert.Equal($"Not found. Expected at: {expectedMarkdownPath} or {expectedPlainPath}", vm.LicenseText);
    }

    [Fact]
    public void Constructor_LoadsLicense_FromParentDirectoryPlainLicenseFile()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"skycd-about-{Guid.NewGuid():N}");
        var childDirectory = Path.Combine(rootDirectory, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(childDirectory);

        var licensePath = Path.Combine(rootDirectory, "LICENSE");
        File.WriteAllText(licensePath, "Test License Content");

        var vm = new AboutDialogViewModel(
            "SkyCD",
            "3.0.0",
            "https://github.com/SkyCD/SkyCD",
            loadedAssemblies: [typeof(string).Assembly],
            baseDirectory: childDirectory);

        Assert.Equal(licensePath, vm.LicensePath);
        Assert.Equal("Test License Content", vm.LicenseText);
    }

    [Fact]
    public void Constructor_PopulatesLoadedAssembliesContract()
    {
        var assemblies = new[]
        {
            typeof(string).Assembly,
            typeof(AboutDialogViewModel).Assembly
        };

        var vm = new AboutDialogViewModel(
            "SkyCD",
            "3.0.0",
            "https://github.com/SkyCD/SkyCD",
            loadedAssemblies: assemblies,
            baseDirectory: Path.GetTempPath());

        Assert.Equal(2, vm.LoadedAssemblies.Count);
        Assert.All(vm.LoadedAssemblies, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Name));
            Assert.False(string.IsNullOrWhiteSpace(entry.Version));
        });
    }

    [Theory]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1048576, "1.0 MB")]
    public void FormatBytes_ReturnsExpectedText(long bytes, string expected)
    {
        Assert.Equal(expected, AboutDialogFormatting.FormatBytes(bytes));
    }

    [Theory]
    [InlineData(0, 0, 0, 3, "03s")]
    [InlineData(0, 0, 2, 5, "02m 05s")]
    [InlineData(0, 1, 4, 9, "01h 04m 09s")]
    [InlineData(2, 7, 30, 12, "2d 07h 30m")]
    public void FormatFriendlyTime_ReturnsExpectedText(int days, int hours, int minutes, int seconds, string expected)
    {
        var duration = TimeSpan.FromDays(days) + TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);

        Assert.Equal(expected, AboutDialogFormatting.FormatFriendlyTime(duration));
    }
}
