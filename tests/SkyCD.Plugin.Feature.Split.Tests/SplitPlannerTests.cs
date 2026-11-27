using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Plugin.FileFormat.LegacySkyCD.Models;
using SkyCD.Plugin.Feature.Split.Exceptions;
using SkyCD.Plugin.Feature.Split.Models;
using SkyCD.Plugin.Feature.Split.Parsing;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Feature.Split.Tests;

public sealed class SplitPlannerTests
{
    [Fact]
    public async Task Split_NamesPartsAndPreservesEveryEntryInOrder()
    {
        using var workspace = new TestWorkspace();
        var catalog = SampleCatalog();
        var output = workspace.CreateDirectory("parts");

        var result = await Planner(catalog).SplitAsync(catalog.Entries(),
            Request(output, "part #N.json", TestCatalog.JsonFormatId, maxEntries: 5),
            _ => Task.FromResult(true), progress: null, cancellationToken: CancellationToken.None);

        Assert.Equal(13, catalog.Documents.Count);
        Assert.Equal(["part #1.json", "part #2.json", "part #3.json"],
            result.Parts.Select(part => Path.GetFileName(part.Path)));
        Assert.Equal([5, 5, 3], result.Parts.Select(part => part.EntryCount));
        Assert.Equal(13, result.TotalEntries);
        Assert.Equal(
            catalog.Documents.Select(Describe),
            (await ReadJsonBackAsync(catalog, result.Parts)).Select(Describe));
    }

    [Fact]
    public async Task Split_LegacyScdKeepsEntryBoundariesAndReopens()
    {
        using var workspace = new TestWorkspace();
        var catalog = SampleCatalog();
        var output = workspace.CreateDirectory("parts");

        var result = await Planner(catalog).SplitAsync(catalog.Entries(),
            Request(output, "part #N.scd", TestCatalog.LegacyFormatId, maxEntries: 5),
            _ => Task.FromResult(true), progress: null, cancellationToken: CancellationToken.None);

        Assert.Equal(["part #1.scd", "part #2.scd", "part #3.scd"],
            result.Parts.Select(part => Path.GetFileName(part.Path)));
        Assert.Equal([5, 5, 3], result.Parts.Select(part => part.EntryCount));
        Assert.Equal(
            catalog.Documents.Select(static entry => entry.Name),
            (await ReadLegacyBackAsync(catalog, result.Parts))
                .SelectMany(static legacy => legacy.Entries)
                .Select(static entry => entry.Path));
    }

    [Fact]
    public async Task Split_HonoursTheSizeLimitAgainstTheWrittenFiles()
    {
        using var workspace = new TestWorkspace();
        var catalog = SampleCatalog();
        var output = workspace.CreateDirectory("parts");

        var result = await Planner(catalog).SplitAsync(catalog.Entries(),
            Request(output, "part #N.json", TestCatalog.JsonFormatId, maxEntries: 100, maxBytes: 900),
            _ => Task.FromResult(true), progress: null, cancellationToken: CancellationToken.None);

        Assert.True(result.Parts.Count > 1, "The size limit should force more than one part.");
        Assert.All(result.Parts, part =>
            Assert.True(new FileInfo(part.Path).Length <= 900, $"{part.Path} is over the size limit."));
        Assert.Equal(catalog.Documents.Select(Describe),
            (await ReadJsonBackAsync(catalog, result.Parts)).Select(Describe));
    }

    [Fact]
    public async Task Split_LeavesTheSourceCatalogUntouched()
    {
        using var workspace = new TestWorkspace();
        var catalog = SampleCatalog();
        var source = workspace.GetPath("source.json");
        await Planner(catalog).SplitAsync(catalog.Entries(),
            Request(workspace.GetPath("seed"), "seed #N.json", TestCatalog.JsonFormatId, maxEntries: 100),
            _ => Task.FromResult(true), progress: null, cancellationToken: CancellationToken.None);
        File.Move(Path.Combine(workspace.GetPath("seed"), "seed #1.json"), source);
        var before = File.ReadAllBytes(source);
        var written = File.GetLastWriteTimeUtc(source);

        await Planner(catalog).SplitAsync(catalog.Entries(),
            Request(workspace.CreateDirectory("parts"), "part #N.json", TestCatalog.JsonFormatId, maxEntries: 4),
            _ => Task.FromResult(true), progress: null, cancellationToken: CancellationToken.None);

        Assert.Equal(before, File.ReadAllBytes(source));
        Assert.Equal(written, File.GetLastWriteTimeUtc(source));
    }

    [Fact]
    public async Task Split_WritesOnlyInsideTheChosenOutputFolder()
    {
        using var workspace = new TestWorkspace();
        var catalog = SampleCatalog();
        var output = workspace.CreateDirectory("parts");

        await Assert.ThrowsAsync<SplitPatternException>(() => Planner(catalog).SplitAsync(catalog.Entries(),
            Request(output, @"..\escape #N.json", TestCatalog.JsonFormatId, maxEntries: 4),
            _ => Task.FromResult(true), progress: null, cancellationToken: CancellationToken.None));

        Assert.Empty(Directory.GetFiles(output));
        Assert.False(File.Exists(workspace.GetPath("escape #1.json")));
    }

    [Fact]
    public async Task Split_RefusesToReplaceAnExistingPartAndWritesNothing()
    {
        using var workspace = new TestWorkspace();
        var catalog = SampleCatalog();
        var output = workspace.CreateDirectory("parts");
        var occupied = workspace.CreateFile(Path.Combine("parts", "part #1.json"), "already here");

        await Assert.ThrowsAsync<SplitOutputConflictException>(() => Planner(catalog).SplitAsync(catalog.Entries(),
            Request(output, "part #N.json", TestCatalog.JsonFormatId, maxEntries: 11),
            _ => Task.FromResult(true), progress: null, cancellationToken: CancellationToken.None));

        Assert.Equal("already here", File.ReadAllText(occupied));
        Assert.Equal(["part #1.json"], Directory.GetFiles(output).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Split_CancellingLeavesNoPartBehind()
    {
        using var workspace = new TestWorkspace();
        var catalog = SampleCatalog();
        var output = workspace.CreateDirectory("parts");
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Planner(catalog).SplitAsync(catalog.Entries(),
            Request(output, "part #N.json", TestCatalog.JsonFormatId, maxEntries: 4),
            _ => Task.FromResult(true), new CancelAfterFirstPart(cancellation), cancellation.Token));

        Assert.Empty(Directory.GetFileSystemEntries(output));
    }

    [Fact]
    public async Task Split_FailureRollsBackThePartsAlreadyWritten()
    {
        using var workspace = new TestWorkspace();
        var catalog = SampleCatalog();
        var output = workspace.CreateDirectory("parts");
        var collision = workspace.CreateFile(Path.Combine("parts", "part #2.json"), "taken");

        await Assert.ThrowsAsync<SplitOutputConflictException>(() => Planner(catalog).SplitAsync(catalog.Entries(),
            Request(output, "part #N.json", TestCatalog.JsonFormatId, maxEntries: 4),
            _ => Task.FromResult(true), progress: null, cancellationToken: CancellationToken.None));

        Assert.Equal("taken", File.ReadAllText(collision));
        Assert.Equal(["part #2.json"], Directory.GetFiles(output).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Split_CreatesAMissingOutputFolderAndReportsProgress()
    {
        using var workspace = new TestWorkspace();
        var catalog = SampleCatalog();
        var output = workspace.GetPath(Path.Combine("deep", "parts", "dir"));
        var progress = new List<SplitProgress>();

        var result = await Planner(catalog).SplitAsync(catalog.Entries(),
            Request(output, "{source}-{n}-of-{total}.json", TestCatalog.JsonFormatId, maxEntries: 8),
            _ => Task.FromResult(true), new CollectProgress(progress), CancellationToken.None);

        Assert.True(Directory.Exists(output));
        Assert.Equal(["gamez-1-of-2.json", "gamez-2-of-2.json"],
            result.Parts.Select(part => Path.GetFileName(part.Path)));
        Assert.Equal([1, 2], progress.Select(value => value.PartsWritten));
        Assert.Equal(50, progress[0].Percent);
        Assert.Equal(100, progress[^1].Percent);
        Assert.Equal(13, progress[^1].EntriesWritten);
    }

    [Fact]
    public async Task Split_ConfirmsLossyFormatOnceForTheWholeRun()
    {
        using var workspace = new TestWorkspace();
        var catalog = SampleCatalog();
        catalog.ScopeItems = LossyScopeItems();
        var output = workspace.CreateDirectory("parts");
        var confirmations = 0;

        var result = await Planner(catalog).SplitAsync(catalog.Entries(),
            Request(output, "part #N.scd", TestCatalog.LegacyFormatId, maxEntries: 5),
            preview =>
            {
                confirmations++;
                Assert.NotEmpty(preview.Items);
                return Task.FromResult(true);
            }, progress: null, cancellationToken: CancellationToken.None);

        Assert.Equal(1, confirmations);
        Assert.Equal(3, result.Parts.Count);
    }

    [Fact]
    public async Task Split_ScopePreservingFormatNeedsNoConfirmation()
    {
        using var workspace = new TestWorkspace();
        var catalog = SampleCatalog();
        catalog.ScopeItems = LossyScopeItems();
        var output = workspace.CreateDirectory("parts");
        var confirmations = 0;

        var result = await Planner(catalog).SplitAsync(catalog.Entries(),
            Request(output, "part #N.json", TestCatalog.JsonFormatId, maxEntries: 5),
            _ =>
            {
                confirmations++;
                return Task.FromResult(true);
            }, progress: null, cancellationToken: CancellationToken.None);

        Assert.Equal(0, confirmations);
        Assert.Equal(3, result.Parts.Count);
    }

    [Fact]
    public async Task Split_DecliningTheLossConfirmationWritesNothing()
    {
        using var workspace = new TestWorkspace();
        var catalog = SampleCatalog();
        catalog.ScopeItems = LossyScopeItems();
        var output = workspace.CreateDirectory("parts");

        await Assert.ThrowsAsync<OperationCanceledException>(() => Planner(catalog).SplitAsync(catalog.Entries(),
            Request(output, "part #N.scd", TestCatalog.LegacyFormatId, maxEntries: 5),
            _ => Task.FromResult(false), progress: null, cancellationToken: CancellationToken.None));

        Assert.Empty(Directory.GetFileSystemEntries(output));
    }

    [Fact]
    public async Task Plan_RespectsBothLimitsAtEntryBoundaries()
    {
        var catalog = new TestCatalog(UniformEntries(100));
        var planner = new SplitPlanner(catalog.SaveHost);
        Task<long> Measure(string name, IReadOnlyList<CatalogEntry> slice, CancellationToken token) =>
            Task.FromResult(slice.Sum(static entry => entry.Size));

        var plans = await planner.PlanAsync(catalog.Entries(),
            BoundRequest("output", "part #N.json", maxEntries: 5, maxBytes: 450),
            Measure, CancellationToken.None);

        Assert.Equal([(0, 2), (2, 3), (5, 2), (7, 3), (10, 3)],
            plans.Select(plan => (plan.FirstEntry, plan.EntryCount)));
        Assert.Equal(
            ["part #1.json", "part #2.json", "part #3.json", "part #4.json", "part #5.json"],
            plans.Select(plan => plan.FileName));
        Assert.Equal([200L, 300L, 200L, 300L, 300L], plans.Select(plan => plan.Bytes));
    }

    [Fact]
    public async Task Plan_KeepsASingleOversizedEntryAlone()
    {
        var catalog = new TestCatalog(SkewedEntries());
        var planner = new SplitPlanner(catalog.SaveHost);
        Task<long> Measure(string name, IReadOnlyList<CatalogEntry> slice, CancellationToken token) =>
            Task.FromResult(slice.Sum(static entry => entry.Size));

        var plans = await planner.PlanAsync(catalog.Entries(),
            BoundRequest("output", "part #N.json", maxEntries: 13, maxBytes: 450),
            Measure, CancellationToken.None);

        Assert.Equal(13, plans.Sum(plan => plan.EntryCount));
        Assert.Equal(Enumerable.Range(0, 13),
            plans.SelectMany(plan => Enumerable.Range(plan.FirstEntry, plan.EntryCount)).OrderBy(index => index));
        Assert.All(plans, plan => Assert.True(plan.Bytes <= 450 || plan.EntryCount == 1));
        Assert.Contains(plans, plan => plan.EntryCount == 1 && plan.Bytes == 0);
    }

    [Fact]
    public async Task Plan_RejectsAMissingEntryLimit()
    {
        var catalog = SampleCatalog();
        var planner = new SplitPlanner(catalog.SaveHost);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => planner.PlanAsync(catalog.Entries(),
            BoundRequest("output", "part #N.json", maxEntries: 0, maxBytes: 0),
            null, CancellationToken.None));
    }

    private static SplitPlanner Planner(TestCatalog catalog) => new(catalog.SaveHost);

    private static SplitRequest Request(string output, string pattern, string formatId, int maxEntries,
        long maxBytes = 0) => new()
        {
            SourceName = "gamez",
            OutputFolder = output,
            FileNamePattern = pattern,
            FormatId = formatId,
            MaxEntriesPerPart = maxEntries,
            MaxPartSizeBytes = maxBytes
        };

    private static SplitRequest BoundRequest(string output, string pattern, int maxEntries, long maxBytes) => new()
    {
        SourceName = "gamez",
        OutputFolder = output,
        FileNamePattern = pattern,
        FormatId = TestCatalog.JsonFormatId,
        MaxEntriesPerPart = maxEntries,
        MaxPartSizeBytes = maxBytes
    };

    private static IReadOnlyList<FileFormatDataLossItem> LossyScopeItems() =>
        [new FileFormatDataLossItem("collections", "favorites", 2, "Fav")];

    private static TestCatalog SampleCatalog() => new(SampleEntries());

    private static IReadOnlyList<CatalogDocument> UniformEntries(long size) => Enumerable.Range(1, 13)
        .Select(index => new CatalogDocument
        {
            Id = $"file-{index}",
            Name = $"File{index:00}.dat",
            ParentId = null,
            Type = CatalogDocumentType.File,
            Size = size,
            ChildrenCount = 0
        }).ToArray();

    private static IReadOnlyList<CatalogDocument> SkewedEntries() => Enumerable.Range(1, 13)
        .Select(index => new CatalogDocument
        {
            Id = $"file-{index}",
            Name = $"File{index:00}.dat",
            ParentId = null,
            Type = CatalogDocumentType.File,
            Size = index == 6 ? 2000 : 100,
            ChildrenCount = 0
        }).ToArray();

    private static IReadOnlyList<CatalogDocument> SampleEntries() => Enumerable.Range(1, 13)
        .Select(index => new CatalogDocument
        {
            Id = $"file-{index}",
            Name = $"File{index:00}.dat",
            ParentId = null,
            Type = CatalogDocumentType.File,
            Size = index * 100,
            ChildrenCount = 0,
            Status = index % 4 == 0 ? "damaged" : "ok",
            Properties = new PropertiesCollection(new Dictionary<string, object?>
            {
                ["Tags"] = index % 3 == 0 ? new List<string> { "keep", $"tag-{index}" } : new List<string>(),
                ["Note"] = $"note-{index}"
            })
        }).ToArray();

    private static async Task<IReadOnlyList<JsonElement>> ReadJsonBackAsync(
        TestCatalog catalog, IReadOnlyList<SplitPart> parts)
    {
        var elements = new List<JsonElement>();
        foreach (var part in parts)
        {
            await using var stream = File.OpenRead(part.Path);
            var result = await catalog.Manager.ReadAsync(new FileFormatReadRequest
            {
                FormatId = TestCatalog.JsonFormatId,
                FileName = Path.GetFileName(part.Path),
                Source = stream
            });
            Assert.True(result.Success, result.Error);
            elements.AddRange(((JsonElement)result.Payload!).EnumerateArray());
        }

        return elements;
    }

    private static async Task<IReadOnlyList<LegacyScdCatalog>> ReadLegacyBackAsync(
        TestCatalog catalog, IReadOnlyList<SplitPart> parts)
    {
        var catalogs = new List<LegacyScdCatalog>();
        foreach (var part in parts)
        {
            await using var stream = File.OpenRead(part.Path);
            var result = await catalog.Manager.ReadAsync(new FileFormatReadRequest
            {
                FormatId = TestCatalog.LegacyFormatId,
                FileName = Path.GetFileName(part.Path),
                Source = stream
            });
            Assert.True(result.Success, result.Error);
            catalogs.Add((LegacyScdCatalog)result.Payload!);
        }

        return catalogs;
    }

    private static string Describe(CatalogDocument entry) =>
        $"{entry.Name}|{entry.Status}|{string.Join(",", TagIds(entry))}|{entry.Properties["Note"]}";

    private static string Describe(JsonElement element)
    {
        var tags = element.GetProperty("properties").TryGetProperty("Tags", out var tagElement) &&
            tagElement.ValueKind == JsonValueKind.Array
            ? string.Join(",", tagElement.EnumerateArray().Select(static tag => tag.GetString()))
            : string.Empty;
        var status = element.TryGetProperty("status", out var statusElement) &&
            statusElement.ValueKind == JsonValueKind.String
            ? statusElement.GetString()
            : null;
        var note = element.GetProperty("properties").TryGetProperty("Note", out var noteElement)
            ? noteElement.GetString()
            : null;
        return $"{element.GetProperty("name").GetString()}|{status}|{tags}|{note}";
    }

    private static IEnumerable<string> TagIds(CatalogDocument entry) =>
        entry.Properties.TryGetValue("Tags", out var raw) && raw is IEnumerable<string> ids ? ids : [];

    private sealed class CancelAfterFirstPart(CancellationTokenSource cancellation) : IProgress<SplitProgress>
    {
        public void Report(SplitProgress value)
        {
            if (value.PartsWritten == 1)
            {
                cancellation.Cancel();
            }
        }
    }

    private sealed class CollectProgress(List<SplitProgress> sink) : IProgress<SplitProgress>
    {
        public void Report(SplitProgress value) => sink.Add(value);
    }
}
