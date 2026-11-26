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
using SkyCD.Plugin.FileFormat.Json;
using SkyCD.Plugin.FileFormat.LegacySkyCD;
using SkyCD.Plugin.FileFormat.LegacySkyCD.Models;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Presentation.Catalog.Split;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class CatalogSplitServiceTests
{
    private const string JsonFormatId = "skycd-json";
    private const string LegacyFormatId = "legacy-scd";

    [Fact]
    public async Task Split_NamesPartsAndPreservesEveryEntryInOrder()
    {
        using var workspace = new TestWorkspace();
        var entries = SampleEntries();
        var output = workspace.CreateDirectory("parts");
        var service = CreateService();

        var result = await service.SplitAsync(entries, [],
            Request(output, "part #N.json", JsonFormatId, maxEntries: 5),
            _ => Task.FromResult(true), progress: null, CancellationToken.None);

        Assert.Equal(13, entries.Count);
        Assert.Equal(["part #1.json", "part #2.json", "part #3.json"],
            result.Parts.Select(part => Path.GetFileName(part.Path)));
        Assert.Equal([5, 5, 3], result.Parts.Select(part => part.EntryCount));
        Assert.Equal(13, result.TotalEntries);
        Assert.Equal(
            entries.Select(Describe),
            (await ReadJsonBackAsync(CreateManager(), result.Parts)).Select(Describe));
    }

    [Fact]
    public async Task Split_LegacyScdKeepsEntryBoundariesAndReopens()
    {
        using var workspace = new TestWorkspace();
        var entries = SampleEntries();
        var output = workspace.CreateDirectory("parts");
        var service = CreateService();

        var result = await service.SplitAsync(entries, [],
            Request(output, "part #N.scd", LegacyFormatId, maxEntries: 5),
            _ => Task.FromResult(true), progress: null, CancellationToken.None);

        Assert.Equal(["part #1.scd", "part #2.scd", "part #3.scd"],
            result.Parts.Select(part => Path.GetFileName(part.Path)));
        Assert.Equal([5, 5, 3], result.Parts.Select(part => part.EntryCount));
        Assert.Equal(
            entries.Select(entry => entry.Name),
            (await ReadLegacyBackAsync(CreateManager(), result.Parts)).SelectMany(catalog => catalog.Entries).Select(entry => entry.Path));
    }

    [Fact]
    public async Task Split_HonoursTheSizeLimitAgainstTheWrittenFiles()
    {
        using var workspace = new TestWorkspace();
        var entries = SampleEntries();
        var output = workspace.CreateDirectory("parts");
        var service = CreateService();

        var result = await service.SplitAsync(entries, [],
            Request(output, "part #N.json", JsonFormatId, maxEntries: 100, maxBytes: 900),
            _ => Task.FromResult(true), progress: null, CancellationToken.None);

        Assert.True(result.Parts.Count > 1, "The size limit should force more than one part.");
        Assert.All(result.Parts, part =>
            Assert.True(new FileInfo(part.Path).Length <= 900, $"{part.Path} is over the size limit."));
        Assert.Equal(entries.Select(Describe),
            (await ReadJsonBackAsync(CreateManager(), result.Parts)).Select(Describe));
    }

    [Fact]
    public async Task Split_LeavesTheSourceCatalogUntouched()
    {
        using var workspace = new TestWorkspace();
        var entries = SampleEntries();
        var service = CreateService();
        var source = workspace.GetPath("source.json");
        await service.SplitAsync(entries, [],
            Request(workspace.GetPath("seed"), "seed.json", JsonFormatId, maxEntries: 100),
            _ => Task.FromResult(true), progress: null, CancellationToken.None);
        File.Move(Path.Combine(workspace.GetPath("seed"), "seed.json"), source);
        var before = File.ReadAllBytes(source);
        var written = File.GetLastWriteTimeUtc(source);

        await service.SplitAsync(entries, [],
            Request(workspace.CreateDirectory("parts"), "part #N.json", JsonFormatId, maxEntries: 4),
            _ => Task.FromResult(true), progress: null, CancellationToken.None);

        Assert.Equal(before, File.ReadAllBytes(source));
        Assert.Equal(written, File.GetLastWriteTimeUtc(source));
    }

    [Fact]
    public async Task Split_WritesOnlyInsideTheChosenOutputFolder()
    {
        using var workspace = new TestWorkspace();
        var entries = SampleEntries();
        var output = workspace.CreateDirectory("parts");
        var service = CreateService();

        await Assert.ThrowsAsync<SplitPatternException>(() => service.SplitAsync(entries, [],
            Request(output, @"..\escape #N.json", JsonFormatId, maxEntries: 4),
            _ => Task.FromResult(true), progress: null, CancellationToken.None));

        Assert.Empty(Directory.GetFiles(output));
        Assert.False(File.Exists(workspace.GetPath("escape #1.json")));
    }

    [Fact]
    public async Task Split_RefusesToReplaceAnExistingPartAndWritesNothing()
    {
        using var workspace = new TestWorkspace();
        var entries = SampleEntries();
        var output = workspace.CreateDirectory("parts");
        var occupied = workspace.CreateFile(Path.Combine("parts", "part #1.json"), "already here");
        var service = CreateService();

        await Assert.ThrowsAsync<SplitOutputConflictException>(() => service.SplitAsync(entries, [],
            Request(output, "part #N.json", JsonFormatId, maxEntries: 11),
            _ => Task.FromResult(true), progress: null, CancellationToken.None));

        Assert.Equal("already here", File.ReadAllText(occupied));
        Assert.Equal(["part #1.json"], Directory.GetFiles(output).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Split_CancellingLeavesNoPartBehind()
    {
        using var workspace = new TestWorkspace();
        var entries = SampleEntries();
        var output = workspace.CreateDirectory("parts");
        var service = CreateService();
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SplitAsync(entries, [],
            Request(output, "part #N.json", JsonFormatId, maxEntries: 4),
            _ => Task.FromResult(true), new CancelAfterFirstPart(cancellation), cancellation.Token));

        Assert.Empty(Directory.GetFileSystemEntries(output));
    }

    [Fact]
    public async Task Split_FailureRollsBackThePartsAlreadyWritten()
    {
        using var workspace = new TestWorkspace();
        var entries = SampleEntries();
        var output = workspace.CreateDirectory("parts");
        var collision = workspace.CreateFile(Path.Combine("parts", "part #2.json"), "taken");
        var service = CreateService();

        await Assert.ThrowsAsync<SplitOutputConflictException>(() => service.SplitAsync(entries, [],
            Request(output, "part #N.json", JsonFormatId, maxEntries: 4),
            _ => Task.FromResult(true), progress: null, CancellationToken.None));

        Assert.Equal("taken", File.ReadAllText(collision));
        Assert.Equal(["part #2.json"], Directory.GetFiles(output).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Split_CreatesAMissingOutputFolderAndReportsProgress()
    {
        using var workspace = new TestWorkspace();
        var entries = SampleEntries();
        var output = workspace.GetPath(Path.Combine("deep", "parts", "dir"));
        var service = CreateService();
        var progress = new List<SplitProgress>();

        var result = await service.SplitAsync(entries, [],
            Request(output, "{source}-{n}-of-{total}.json", JsonFormatId, maxEntries: 8),
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
        var entries = SampleEntries();
        var output = workspace.CreateDirectory("parts");
        var service = CreateService();
        var confirmations = 0;

        var result = await service.SplitAsync(entries, LossyScopeItems(),
            Request(output, "part #N.scd", LegacyFormatId, maxEntries: 5),
            preview =>
            {
                confirmations++;
                Assert.NotEmpty(preview.Items);
                return Task.FromResult(true);
            }, progress: null, CancellationToken.None);

        Assert.Equal(1, confirmations);
        Assert.Equal(3, result.Parts.Count);
    }

    [Fact]
    public async Task Split_ScopePreservingFormatNeedsNoConfirmation()
    {
        using var workspace = new TestWorkspace();
        var entries = SampleEntries();
        var output = workspace.CreateDirectory("parts");
        var service = CreateService();
        var confirmations = 0;

        var result = await service.SplitAsync(entries, LossyScopeItems(),
            Request(output, "part #N.json", JsonFormatId, maxEntries: 5),
            _ =>
            {
                confirmations++;
                return Task.FromResult(true);
            }, progress: null, CancellationToken.None);

        Assert.Equal(0, confirmations);
        Assert.Equal(3, result.Parts.Count);
    }

    [Fact]
    public async Task Split_DecliningTheLossConfirmationWritesNothing()
    {
        using var workspace = new TestWorkspace();
        var entries = SampleEntries();
        var output = workspace.CreateDirectory("parts");
        var service = CreateService();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SplitAsync(entries, LossyScopeItems(),
            Request(output, "part #N.scd", LegacyFormatId, maxEntries: 5),
            _ => Task.FromResult(false), progress: null, CancellationToken.None));

        Assert.Empty(Directory.GetFileSystemEntries(output));
    }

    [Fact]
    public async Task Plan_RespectsBothLimitsAtEntryBoundaries()
    {
        var entries = UniformEntries(100);
        var service = CreateService();
        Task<long> Measure(string name, IReadOnlyList<CatalogDocument> slice, CancellationToken token) =>
            Task.FromResult(slice.Sum(entry => entry.Size));

        var plans = await service.PlanAsync(entries,
            Request("output", "part #N.json", JsonFormatId, maxEntries: 5, maxBytes: 450),
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
        var entries = SkewedEntries();
        var service = CreateService();
        Task<long> Measure(string name, IReadOnlyList<CatalogDocument> slice, CancellationToken token) =>
            Task.FromResult(slice.Sum(entry => entry.Size));

        var plans = await service.PlanAsync(entries,
            Request("output", "part #N.json", JsonFormatId, maxEntries: 13, maxBytes: 450),
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
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.PlanAsync(SampleEntries(),
            Request("output", "part #N.json", JsonFormatId, maxEntries: 0),
            null, CancellationToken.None));
    }

    private static CatalogSplitService CreateService() =>
        new(CreateManager());

    private static FileFormatManager CreateManager() =>
        new([new LegacyScdPlugin(), new JsonCatalogPlugin()]);

    private static SplitRequest Request(string output, string pattern, string formatId, int maxEntries, long maxBytes = 0) => new()
    {
        SourceName = "gamez",
        OutputFolder = output,
        FileNamePattern = pattern,
        FormatId = formatId,
        MaxEntriesPerPart = maxEntries,
        MaxPartSizeBytes = maxBytes
    };

    private static IReadOnlyList<FileFormatDataLossItem> LossyScopeItems() =>
        [new FileFormatDataLossItem("collections", "favorites", 2, "Fav")];

    private static IReadOnlyList<CatalogDocument> UniformEntries(long size) => Enumerable.Range(1, 13).Select(index => new CatalogDocument
    {
        Id = $"file-{index}",
        Name = $"File{index:00}.dat",
        ParentId = null,
        Type = CatalogDocumentType.File,
        Size = size,
        ChildrenCount = 0
    }).ToArray();

    private static IReadOnlyList<CatalogDocument> SkewedEntries() => Enumerable.Range(1, 13).Select(index => new CatalogDocument
    {
        Id = $"file-{index}",
        Name = $"File{index:00}.dat",
        ParentId = null,
        Type = CatalogDocumentType.File,
        Size = index == 6 ? 2000 : 100,
        ChildrenCount = 0
    }).ToArray();

    private static IReadOnlyList<CatalogDocument> SampleEntries() => Enumerable.Range(1, 13).Select(index => new CatalogDocument
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
        FileFormatManager manager, IReadOnlyList<SplitPart> parts)
    {
        var elements = new List<JsonElement>();
        foreach (var part in parts)
        {
            await using var stream = File.OpenRead(part.Path);
            var result = await manager.ReadAsync(new FileFormatReadRequest
            {
                FormatId = JsonFormatId,
                FileName = Path.GetFileName(part.Path),
                Source = stream
            });
            Assert.True(result.Success, result.Error);
            elements.AddRange(((JsonElement)result.Payload!).EnumerateArray());
        }

        return elements;
    }

    private static async Task<IReadOnlyList<LegacyScdCatalog>> ReadLegacyBackAsync(
        FileFormatManager manager, IReadOnlyList<SplitPart> parts)
    {
        var catalogs = new List<LegacyScdCatalog>();
        foreach (var part in parts)
        {
            await using var stream = File.OpenRead(part.Path);
            var result = await manager.ReadAsync(new FileFormatReadRequest
            {
                FormatId = LegacyFormatId,
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
            ? string.Join(",", tagElement.EnumerateArray().Select(tag => tag.GetString()))
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

