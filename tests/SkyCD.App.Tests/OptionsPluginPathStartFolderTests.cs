using System;
using System.IO;
using SkyCD.Presentation.ViewModels;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Start-folder decision behind Options → Plugins → Browse: the picker opens at the first folder in the
/// field's path-separator list that exists, and falls back to the platform default otherwise.
/// </summary>
public sealed class OptionsPluginPathStartFolderTests : IDisposable
{
    private readonly TestWorkspace workspace = new();
    private readonly string firstFolder;
    private readonly string secondFolder;
    private readonly string missingFolder;

    public OptionsPluginPathStartFolderTests()
    {
        firstFolder = Path.Combine(workspace.Root, "first");
        secondFolder = Path.Combine(workspace.Root, "second");
        missingFolder = Path.Combine(workspace.Root, "missing");
        Directory.CreateDirectory(firstFolder);
        Directory.CreateDirectory(secondFolder);
    }

    public void Dispose() => workspace.Dispose();

    [Fact]
    public void SingleExistingFolder_StartsThere()
    {
        Assert.Equal(firstFolder, OptionsDialogViewModel.ResolvePluginPathStartFolder(firstFolder));
    }

    [Fact]
    public void MultiFolderList_StartsAtTheFirstExistingEntry()
    {
        var field = string.Join(Path.PathSeparator, missingFolder, secondFolder, firstFolder);

        Assert.Equal(secondFolder, OptionsDialogViewModel.ResolvePluginPathStartFolder(field));
    }

    [Fact]
    public void MultiFolderList_StartsAtTheFirstEntryWhenItExists()
    {
        var field = string.Join(Path.PathSeparator, firstFolder, secondFolder);

        Assert.Equal(firstFolder, OptionsDialogViewModel.ResolvePluginPathStartFolder(field));
    }

    [Fact]
    public void WhitespaceAndEmptyEntries_AreIgnored()
    {
        var field = $"  {Path.PathSeparator}{Path.PathSeparator}   {firstFolder}  {Path.PathSeparator}   ";

        Assert.Equal(firstFolder, OptionsDialogViewModel.ResolvePluginPathStartFolder(field));
    }

    [Fact]
    public void AllMissingFolders_FallBackToTheDefaultLocation()
    {
        var field = string.Join(Path.PathSeparator, missingFolder, Path.Combine(workspace.Root, "also-missing"));

        Assert.Null(OptionsDialogViewModel.ResolvePluginPathStartFolder(field));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyField_FallsBackToTheDefaultLocation(string? field)
    {
        Assert.Null(OptionsDialogViewModel.ResolvePluginPathStartFolder(field));
    }
}