using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Feature.Split.Models;
using SkyCD.Plugin.Feature.Split.Parsing;
using SkyCD.Plugin.Feature.Split.Views;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Feature.Split.Tests;

public class SplitDialogViewModelTests
{
    [Fact]
    public void TheDefaultsPointAtTheSourceFolderAndTheLegacyPartNames()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);

        var viewModel = new SplitDialogViewModel(source, ReadCatalog(source));

        Assert.Equal(Path.GetDirectoryName(source), viewModel.OutputFolder);
        Assert.Equal("part #N.scd", viewModel.FileNamePattern);
        Assert.Equal("1000", viewModel.EntriesPerPart);
        Assert.Equal("MB", viewModel.PartSizeUnit);
        Assert.Equal("13 entries", viewModel.EntrySummary);
        Assert.False(viewModel.IsBusy);
        Assert.Null(viewModel.OutcomeMessage);
        Assert.True(viewModel.SplitCommand.CanExecute(null));
        Assert.False(viewModel.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task AnInvalidEntryLimitIsReportedWithoutWriting()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);
        var output = workspace.CreateDirectory("parts");
        var viewModel = new SplitDialogViewModel(source, ReadCatalog(source))
        {
            OutputFolder = output,
            EntriesPerPart = "many"
        };

        await viewModel.SplitCommand.ExecuteAsync(null);

        Assert.Contains("positive whole number", viewModel.ErrorText);
        Assert.Empty(Directory.GetFileSystemEntries(output));
        Assert.Null(viewModel.OutcomeMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task WithoutAnyLimitTheUserIsAskedToSetOne()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);
        var output = workspace.CreateDirectory("parts");
        var viewModel = new SplitDialogViewModel(source, ReadCatalog(source))
        {
            OutputFolder = output,
            EntriesPerPart = string.Empty,
            PartSize = string.Empty
        };

        await viewModel.SplitCommand.ExecuteAsync(null);

        Assert.Contains("maximum entry count", viewModel.ErrorText);
        Assert.Empty(Directory.GetFileSystemEntries(output));
    }

    [Fact]
    public async Task AnUnsafePatternIsReportedWithoutWritingOutsideTheFolder()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);
        var output = workspace.CreateDirectory("parts");
        var viewModel = new SplitDialogViewModel(source, ReadCatalog(source))
        {
            OutputFolder = output,
            FileNamePattern = @"..\escape #N.scd"
        };

        await viewModel.SplitCommand.ExecuteAsync(null);

        Assert.Contains("outside the output folder", viewModel.ErrorText);
        Assert.Empty(Directory.GetFileSystemEntries(output));
        Assert.False(File.Exists(workspace.GetPath("escape #1.scd")));
    }

    [Fact]
    public async Task ASizeLimitInBytesSplitsAndAnnouncesTheParts()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);
        var output = workspace.CreateDirectory("parts");
        var closed = 0;
        var viewModel = new SplitDialogViewModel(source, ReadCatalog(source))
        {
            OutputFolder = output,
            EntriesPerPart = string.Empty,
            PartSize = "120",
            PartSizeUnit = "Bytes"
        };
        viewModel.CloseRequested += () => closed++;

        await viewModel.SplitCommand.ExecuteAsync(null);

        var parts = Directory.GetFiles(output, "*.scd").OrderBy(path => path).ToArray();
        Assert.NotEmpty(parts);
        Assert.All(parts, path => Assert.True(new FileInfo(path).Length <= 120, $"{path} is over the limit."));
        Assert.Equal(13, parts.Sum(path => ReadCatalog(path).EntryCount));
        Assert.Equal(string.Empty, viewModel.ErrorText);
        Assert.Contains("into", viewModel.OutcomeMessage);
        Assert.Contains("part file(s)", viewModel.OutcomeMessage);
        Assert.Equal(1, closed);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task AnExistingPartFileFailsTheSplitAndKeepsTheOriginalFile()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);
        var output = workspace.CreateDirectory("parts");
        var sentinel = workspace.CreateFile("parts/part #1.scd", "[Disk]\nkept\n");
        var viewModel = new SplitDialogViewModel(source, ReadCatalog(source))
        {
            OutputFolder = output,
            EntriesPerPart = "5"
        };

        await viewModel.SplitCommand.ExecuteAsync(null);

        Assert.Contains("already exists", viewModel.ErrorText);
        Assert.Equal("[Disk]\nkept\n", File.ReadAllText(sentinel));
        Assert.Equal(["part #1.scd"], Directory.GetFiles(output).Select(Path.GetFileName));
        Assert.Null(viewModel.OutcomeMessage);
    }

    [Fact]
    public async Task AnEntryLimitSplitsInsideTheSourceFolderByDefault()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);
        var folder = Path.GetDirectoryName(source)!;
        var viewModel = new SplitDialogViewModel(source, ReadCatalog(source)) { EntriesPerPart = "5" };

        await viewModel.SplitCommand.ExecuteAsync(null);

        Assert.Contains("3 part file(s)", viewModel.OutcomeMessage);
        Assert.Equal(
            ["gamez.scd", "part #1.scd", "part #2.scd", "part #3.scd"],
            Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    private static string StageCatalog(TestWorkspace workspace) => workspace.CreateFile("source/gamez.scd", Fixture());

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Legacy", "catalog-sample.scd"));

    private static LegacyTextCatalog ReadCatalog(string path) =>
        LegacyTextCatalogReader.ReadAsync(path, CancellationToken.None).GetAwaiter().GetResult();
}
