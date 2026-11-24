using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.App.Views;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class CatalogSavePickerTests
{
    [Fact]
    public void Create_BuildsThePickerFromTheManagerReturnedByTheReadinessGuard()
    {
        // Regression #1067: before any plugin sync the window holds the empty host-container manager.
        var hostContainerManager = new FileFormatManager([]);
        var syncedManager = new FileFormatManager(
        [
            new FakeWritablePlugin("zeta", "Zeta", ".z"),
            new FakeWritablePlugin("alpha", "Alpha", ".a", ".a2")
        ]);

        var readinessGuardInvoked = false;
        var options = CatalogSavePicker.Create(
            ensureFileFormatManager: () =>
            {
                readinessGuardInvoked = true;
                return syncedManager;
            },
            saveAs: true,
            title: "Save catalog as",
            allFilesLabel: "All files");

        Assert.True(readinessGuardInvoked);
        Assert.Empty(hostContainerManager.GetSaveFormats());
        Assert.NotNull(options);

        var fileTypes = options!.FileTypeChoices!.ToArray();
        Assert.Equal(["Alpha", "Zeta", "All files"], fileTypes.Select(type => type.Name));
        Assert.Equal(["*.a", "*.a2"], fileTypes[0].Patterns);
        Assert.Equal(["*.z"], fileTypes[1].Patterns);
        Assert.Equal(["*.*"], fileTypes[2].Patterns);
        Assert.Equal("catalog.a", options.SuggestedFileName);
        Assert.Equal("a", options.DefaultExtension);
    }

    [Fact]
    public void Create_ReturnsNull_WhenNoWritableFormatIsAvailable()
    {
        var options = CatalogSavePicker.Create(
            ensureFileFormatManager: () => new FileFormatManager([]),
            saveAs: true,
            title: "Save catalog as",
            allFilesLabel: "All files");

        Assert.Null(options);
    }

    [Fact]
    public void Create_OmitsTheAllFilesEntry_WhenNoLabelIsGiven()
    {
        var options = CatalogSavePicker.Create(
            ensureFileFormatManager: () => new FileFormatManager([new FakeWritablePlugin("alpha", "Alpha", ".a")]),
            saveAs: false,
            title: "Save catalog",
            allFilesLabel: null);

        var fileTypes = Assert.Single(options!.FileTypeChoices!);
        Assert.Equal("Alpha", fileTypes.Name);
    }

    private sealed class FakeWritablePlugin(string formatId, string displayName, params string[] extensions)
        : IFileFormatPluginCapability
    {
        public FileFormatDescriptor SupportedFormat => new(formatId, displayName, extensions, [], false, true);

        public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileFormatReadResult { Success = false });

        public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileFormatWriteResult { Success = true });
    }
}
