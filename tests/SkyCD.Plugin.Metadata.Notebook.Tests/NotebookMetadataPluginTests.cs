using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Notebook.Exceptions;
using SkyCD.Plugin.Metadata.Notebook.Models;
using SkyCD.SDK.Capabilities.Media;
using Xunit;

namespace SkyCD.Plugin.Metadata.Notebook.Tests;

public sealed class NotebookMetadataPluginTests
{
    [Fact]
    public async Task ValidNotebook_PublishesFormatVersionLanguageAndCellCounts()
    {
        var values = await ExtractAsync(NotebookFixtures.FullNotebook);

        Assert.Equal("ipynb", Text(values, NotebookSchema.NotebookFormat));
        Assert.True(Bool(values, NotebookSchema.Valid));
        Assert.Equal("4.5", Text(values, NotebookSchema.NbFormatVersion));
        Assert.Equal("python", Text(values, NotebookSchema.Language));
        Assert.Equal(3, Integer(values, NotebookSchema.CellCount));
        Assert.Equal(3, Integer(values, NotebookSchema.IncludedCellCount));
        Assert.Equal("Revenue check", Text(values, NotebookSchema.CellTitles));
        Assert.DoesNotContain(values, value => value.Key == NotebookSchema.ErrorCode);
    }

    [Fact]
    public async Task Language_FallsBackToLanguageInfo_WhenKernelSpecIsMissing()
    {
        var values = await ExtractAsync(NotebookFixtures.FencedHeadingNotebook);

        Assert.Equal("julia", Text(values, NotebookSchema.Language));
        Assert.Equal("4.4", Text(values, NotebookSchema.NbFormatVersion));
    }

    [Fact]
    public async Task CellTitles_IgnoreFencedCodeAndCellsWithoutHeadings()
    {
        var values = await ExtractAsync(NotebookFixtures.FencedHeadingNotebook);

        Assert.Equal("Real Title", Text(values, NotebookSchema.CellTitles));
    }

    [Fact]
    public async Task EmptyCells_AreCountedButNotIncluded()
    {
        var values = await ExtractAsync(NotebookFixtures.NotebookWithEmptyCell);

        Assert.Equal(3, Integer(values, NotebookSchema.CellCount));
        Assert.Equal(2, Integer(values, NotebookSchema.IncludedCellCount));
        Assert.Equal("Kept", Text(values, NotebookSchema.CellTitles));
    }

    [Fact]
    public async Task NotebookWithoutKernelMetadata_OmitsLanguage()
    {
        var values = await ExtractAsync(NotebookFixtures.NotebookWithoutKernel);

        Assert.True(Bool(values, NotebookSchema.Valid));
        Assert.DoesNotContain(values, value => value.Key == NotebookSchema.Language);
        Assert.Equal("Bare", Text(values, NotebookSchema.CellTitles));
    }

    [Fact]
    public async Task StoredOutputs_AreNotPublishedOrInterpreted()
    {
        var values = await ExtractAsync(NotebookFixtures.FullNotebook);

        Assert.All(values, value => Assert.DoesNotContain("SENTINEL", value.Text ?? string.Empty,
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task CellBudget_StopsInspectionButReportsTheTotal()
    {
        var values = await ExtractAsync(
            NotebookFixtures.ManyCells(12),
            new NotebookPluginOptions(maxCells: 5));

        Assert.Equal(12, Integer(values, NotebookSchema.CellCount));
        Assert.Equal(5, Integer(values, NotebookSchema.IncludedCellCount));
        Assert.Equal("Cell 0, Cell 1, Cell 2, Cell 3, Cell 4", Text(values, NotebookSchema.CellTitles));
    }

    [Fact]
    public async Task CorruptJson_ReportsCodeAndPositionWithoutInventedValues()
    {
        var values = await ExtractAsync(NotebookFixtures.CorruptNotebook);

        Assert.False(Bool(values, NotebookSchema.Valid));
        Assert.Equal("invalid-json", Text(values, NotebookSchema.ErrorCode));
        Assert.True(Integer(values, NotebookSchema.ErrorLine) >= 1);
        Assert.True(Integer(values, NotebookSchema.ErrorColumn) >= 1);
        AssertDoesNotContainSuccessValues(values);
    }

    [Fact]
    public async Task JsonWithoutCellsArray_IsNotANotebook()
    {
        var values = await ExtractAsync(NotebookFixtures.NotANotebook);

        Assert.False(Bool(values, NotebookSchema.Valid));
        Assert.Equal("not-a-notebook", Text(values, NotebookSchema.ErrorCode));
        AssertDoesNotContainSuccessValues(values);
    }

    [Fact]
    public async Task LegacyWorksheets_AreReportedAsUnsupported()
    {
        var values = await ExtractAsync(NotebookFixtures.LegacyNotebook);

        Assert.False(Bool(values, NotebookSchema.Valid));
        Assert.Equal("legacy-nbformat-unsupported", Text(values, NotebookSchema.ErrorCode));
        AssertDoesNotContainSuccessValues(values);
    }

    [Fact]
    public async Task EmptySource_IsReportedAsEmpty()
    {
        var result = await new NotebookMetadataPlugin().ExtractAsync(
            new MediaItem(".ipynb", "application/x-ipynb+json", 0),
            new ByteArrayMediaSource([]),
            progress: null,
            CancellationToken.None);

        Assert.Equal("empty", Text(result.Values, NotebookSchema.ErrorCode));
        Assert.False(Bool(result.Values, NotebookSchema.Valid));
        Assert.Equal(0, Integer(result.Values, NotebookSchema.ByteLength));
    }

    [Fact]
    public async Task Source_IsReadOnceAndOversizedInputIsRejected()
    {
        var bytes = NotebookFixtures.Bytes(NotebookFixtures.FullNotebook);
        var source = new ByteArrayMediaSource(bytes);
        await new NotebookMetadataPlugin().ExtractAsync(
            new MediaItem(".ipynb", null, bytes.Length), source, null, CancellationToken.None);
        Assert.Equal(1, source.ReadCount);

        var limited = new NotebookMetadataPlugin(new NotebookPluginOptions(maxSourceBytes: 4));
        await Assert.ThrowsAsync<NotebookSourceTooLargeException>(() => limited.ExtractAsync(
            new MediaItem(".ipynb", null, bytes.Length),
            new ByteArrayMediaSource(bytes),
            null,
            CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_IsHonoured()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new NotebookMetadataPlugin().ExtractAsync(
            new MediaItem(".ipynb", null, 4),
            new ByteArrayMediaSource(Encoding.UTF8.GetBytes("{}")),
            null,
            source.Token));
    }

    private static void AssertDoesNotContainSuccessValues(IReadOnlyList<MediaMetadataValue> values)
    {
        Assert.DoesNotContain(values, value => value.Key is NotebookSchema.NbFormatVersion
            or NotebookSchema.Language
            or NotebookSchema.CellTitles);
    }

    private static async Task<IReadOnlyList<MediaMetadataValue>> ExtractAsync(
        string notebook,
        NotebookPluginOptions? options = null)
    {
        var bytes = NotebookFixtures.Bytes(notebook);
        var result = await new NotebookMetadataPlugin(options ?? NotebookPluginOptions.Default).ExtractAsync(
            new MediaItem(".ipynb", null, bytes.Length),
            new ByteArrayMediaSource(bytes),
            progress: null,
            CancellationToken.None);
        return result.Values;
    }

    private static string Text(IReadOnlyList<MediaMetadataValue> values, string key) =>
        values.Single(value => value.Key == key).Text!;

    private static bool Bool(IReadOnlyList<MediaMetadataValue> values, string key) =>
        values.Single(value => value.Key == key).Boolean!.Value;

    private static long Integer(IReadOnlyList<MediaMetadataValue> values, string key) =>
        values.Single(value => value.Key == key).Integer!.Value;
}
