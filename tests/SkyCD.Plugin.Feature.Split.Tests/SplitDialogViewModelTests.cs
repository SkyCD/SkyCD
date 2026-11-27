using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Feature.Split.Exceptions;
using SkyCD.Plugin.Feature.Split.Models;
using SkyCD.Plugin.Feature.Split.ViewModels;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Feature.Split.Tests;

public class SplitDialogViewModelTests
{
    private static readonly SplitFormatOption Scd = new("legacy-scd", "Legacy SkyCD", "scd", [".scd"]);
    private static readonly SplitFormatOption Json = new("skycd-json", "SkyCD JSON", "json", [".json"]);

    static SplitDialogViewModelTests() => SplitTestLocalization.EnsureRegistered();

    [Fact]
    public void TheDefaultsPointAtTheSourceFolderAndTheDefaultFormatExtension()
    {
        using var workspace = new TestWorkspace();
        var output = workspace.CreateDirectory("parts");

        var viewModel = new SplitDialogViewModel("gamez", 13, output, [Scd, Json], "legacy-scd", UnusedRunner);

        Assert.Equal(output, viewModel.OutputFolder);
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
    public async Task AnInvalidEntryLimitIsReportedWithoutRunning()
    {
        using var workspace = new TestWorkspace();
        var output = workspace.CreateDirectory("parts");
        var calls = 0;
        var viewModel = new SplitDialogViewModel("gamez", 13, output, [Scd, Json], "legacy-scd",
            (_, _, _) =>
            {
                calls++;
                return Task.FromResult(new SplitResult([], 0));
            })
        {
            EntriesPerPart = "many"
        };

        await viewModel.SplitCommand.ExecuteAsync(null);

        Assert.Contains("positive whole number", viewModel.ErrorText);
        Assert.Equal(0, calls);
        Assert.Null(viewModel.OutcomeMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task AnEmptyEntryLimitIsReportedWithoutRunning()
    {
        var calls = 0;
        var viewModel = new SplitDialogViewModel("gamez", 13, "output", [Scd, Json], "legacy-scd",
            (_, _, _) =>
            {
                calls++;
                return Task.FromResult(new SplitResult([], 0));
            })
        {
            EntriesPerPart = string.Empty
        };

        await viewModel.SplitCommand.ExecuteAsync(null);

        Assert.Contains("positive whole number", viewModel.ErrorText);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task AnUnsafePatternIsReportedWithoutRunning()
    {
        var calls = 0;
        var viewModel = new SplitDialogViewModel("gamez", 13, "output", [Scd, Json], "legacy-scd",
            (_, _, _) =>
            {
                calls++;
                return Task.FromResult(new SplitResult([], 0));
            })
        {
            FileNamePattern = @"..\escape #N.scd"
        };

        await viewModel.SplitCommand.ExecuteAsync(null);

        Assert.Contains("outside the output folder", viewModel.ErrorText);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task APatternExtensionMustMatchTheChosenFormat()
    {
        SplitRequest? seen = null;
        var viewModel = new SplitDialogViewModel("gamez", 13, "output", [Scd, Json], "legacy-scd",
            (request, _, _) =>
            {
                seen = request;
                return Task.FromResult(new SplitResult([], 0));
            })
        {
            FileNamePattern = "part #N.json"
        };

        await viewModel.SplitCommand.ExecuteAsync(null);

        Assert.Contains("does not match", viewModel.ErrorText);
        Assert.Null(seen);
    }

    [Fact]
    public void SwitchingFormatSwapsThePatternExtensionButKeepsTheStem()
    {
        var viewModel = new SplitDialogViewModel("gamez", 13, "output", [Scd, Json], "legacy-scd", UnusedRunner);

        viewModel.SelectedSplitFormat = Json;
        Assert.Equal("part #N.json", viewModel.FileNamePattern);

        viewModel.FileNamePattern = "backup-{n}.json";
        viewModel.SelectedSplitFormat = Scd;
        Assert.Equal("backup-{n}.scd", viewModel.FileNamePattern);
    }

    [Fact]
    public async Task ASizeLimitInBytesSplitsAndAnnouncesTheParts()
    {
        using var workspace = new TestWorkspace();
        var output = workspace.CreateDirectory("parts");
        var closed = 0;
        SplitRequest? seen = null;
        var viewModel = new SplitDialogViewModel("gamez", 13, output, [Scd, Json], "legacy-scd",
            (request, progress, _) =>
            {
                seen = request;
                progress.Report(new SplitProgress(1, 2, 6, 13));
                progress.Report(new SplitProgress(2, 2, 13, 13));
                return Task.FromResult(new SplitResult(
                    [new SplitPart("a", 6, 60), new SplitPart("b", 7, 60)], 13));
            })
        {
            EntriesPerPart = "1000",
            PartSize = "120",
            PartSizeUnit = "Bytes"
        };
        viewModel.CloseRequested += () => closed++;

        await viewModel.SplitCommand.ExecuteAsync(null);

        Assert.Equal(120L, seen!.MaxPartSizeBytes);
        Assert.Equal(100, viewModel.ProgressPercent);
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
        var output = workspace.CreateDirectory("parts");
        var sentinel = workspace.CreateFile("parts/part #1.scd", "[Disk]\nkept\n");
        var viewModel = new SplitDialogViewModel("gamez", 13, output, [Scd, Json], "legacy-scd",
            (_, _, _) => Task.FromException<SplitResult>(new SplitOutputConflictException(sentinel)))
        {
            EntriesPerPart = "5"
        };

        await viewModel.SplitCommand.ExecuteAsync(null);

        Assert.Contains("already exists", viewModel.ErrorText);
        Assert.Equal("[Disk]\nkept\n", File.ReadAllText(sentinel));
        Assert.Equal(["part #1.scd"], Directory.GetFiles(output).Select(Path.GetFileName));
        Assert.Null(viewModel.OutcomeMessage);
    }

    [Fact]
    public async Task CancellingTheRunReportsThatNoPartsWereKept()
    {
        var viewModel = new SplitDialogViewModel("gamez", 13, "output", [Scd, Json], "legacy-scd",
            (_, _, _) => Task.FromException<SplitResult>(new OperationCanceledException()));

        await viewModel.SplitCommand.ExecuteAsync(null);

        Assert.Contains("cancelled", viewModel.StatusText);
        Assert.Null(viewModel.OutcomeMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task AFailingRunReportsTheError()
    {
        var viewModel = new SplitDialogViewModel("gamez", 13, "output", [Scd, Json], "legacy-scd",
            (_, _, _) => Task.FromException<SplitResult>(new InvalidOperationException("boom")));

        await viewModel.SplitCommand.ExecuteAsync(null);

        Assert.Equal("boom", viewModel.ErrorText);
        Assert.Contains("failed", viewModel.StatusText);
        Assert.Null(viewModel.OutcomeMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public void AnEmptyCatalogDisablesTheSplitCommand()
    {
        var viewModel = new SplitDialogViewModel("gamez", 0, "output", [Scd, Json], "legacy-scd", UnusedRunner);

        Assert.False(viewModel.SplitCommand.CanExecute(null));
    }

    [Fact]
    public async Task WithoutAFormatTheSplitIsRefused()
    {
        var calls = 0;
        var viewModel = new SplitDialogViewModel("gamez", 13, "output", [], string.Empty,
            (_, _, _) =>
            {
                calls++;
                return Task.FromResult(new SplitResult([], 0));
            });

        await viewModel.SplitCommand.ExecuteAsync(null);

        Assert.Contains("written and reopened", viewModel.ErrorText);
        Assert.Equal(0, calls);
    }

    private static Task<SplitResult> UnusedRunner(
        SplitRequest request, IProgress<SplitProgress> progress, CancellationToken cancellationToken) =>
        Task.FromResult(new SplitResult([], 0));
}
