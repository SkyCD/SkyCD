using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.ViewModels;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #462: AI descriptions must be readable and searchable offline, must never generate as a side
/// effect of reading, must stay separate from user comments, and explicit passes must be cancellable.
/// </summary>
public sealed class AiDescriptionTests : IDisposable
{
    private readonly AppDataSandbox sandbox = new();

    public void Dispose() => sandbox.Dispose();

    private sealed class Stack : IDisposable
    {
        public Stack()
        {
            Container = new Container();
            new CouchbaseServiceRegistrator().RegisterServices(Container);
            var repositories = Container.Resolve<RepositoryManager>();
            Catalog = (CatalogDocumentRepository)repositories.For<CatalogDocument>();
        }

        public Container Container { get; }

        public CatalogDocumentRepository Catalog { get; }

        public void Dispose() => Container.Dispose();
    }

    private static CatalogDocument Entry(string id, string? name = null, string? comment = null)
    {
        var entry = new CatalogDocument
        {
            Id = id,
            Name = name ?? id,
            Type = CatalogDocumentType.File,
            Size = 128
        };
        if (comment is not null)
        {
            entry.Properties["Comment"] = comment;
        }

        return entry;
    }

    private static void SaveDescription(Stack stack, IMediaCacheStore store, string entryId, string text,
        string destination = "local-inference")
    {
        var descriptions = new CatalogDescriptionService(stack.Catalog, store);
        var context = descriptions.Build(entryId, new(Filename: true, Metadata: true, Text: true));
        descriptions.Save(context, new GeneratedDescription(
            text, "fake-agent", "fake-model", "1.0.0", "prompt-v1", "English",
            "content-based AI description", destination, DateTimeOffset.UtcNow));
    }

    private sealed class FakeCapability : IGeneratedDescriptionCapability, IDescriptionGenerationPlanner
    {
        private readonly Func<DescriptionContext, CancellationToken, Task<GeneratedDescription>>? generate;
        private readonly DescriptionGenerationPlan plan;
        private int calls;

        public FakeCapability(
            Func<DescriptionContext, CancellationToken, Task<GeneratedDescription>>? generate = null,
            DescriptionGenerationPlan? plan = null)
        {
            this.generate = generate;
            this.plan = plan ?? new DescriptionGenerationPlan(
                true, "fake-provider", "2.0.0", "fake-model", "cloud",
                new DescriptionContextOptions(Filename: true, Metadata: true));
        }

        public int Calls => Volatile.Read(ref calls);

        public MediaMetadataSchema Schema => new("test.description.schema", 1, []);

        public Task<GeneratedDescription> GenerateAsync(DescriptionContext context, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref calls);
            return generate is null
                ? Task.FromResult(Describe($"generated-{call}"))
                : generate(context, cancellationToken);
        }

        public DescriptionGenerationPlan GetPlan() => plan;

        public static GeneratedDescription Describe(string text) => new(
            text, "fake-agent", "fake-model", "1.0.0", "prompt-v1", "English",
            "content-based AI description", "cloud", DateTimeOffset.UtcNow);
    }

    [Fact]
    public void StoredDescription_ReadsOffline_WithProvenanceSeparateFromComments()
    {
        using var stack = new Stack();
        using var store = new CouchbaseMediaCacheStore(stack.Catalog.Collection.Database);
        var entry = Entry("offline", comment: "User comment");
        stack.Catalog.Save(entry.Id, entry);
        SaveDescription(stack, store, entry.Id, "Stored generated description");

        var service = new AiDescriptionService(stack.Catalog, store, capability: null);
        var details = service.Read(entry.Id);

        Assert.Equal(AiDescriptionState.Current, details.State);
        Assert.Equal("Stored generated description", details.Text);
        Assert.Equal("fake-agent", details.Agent);
        Assert.Equal("fake-model", details.Model);
        Assert.Equal("1.0.0", details.PluginVersion);
        Assert.Equal("prompt-v1", details.PromptVersion);
        Assert.Equal("English", details.Language);
        Assert.Equal("local-inference", details.ProcessingDestination);
        Assert.NotNull(details.GeneratedAtUtc);
        Assert.False(service.GetProviderInfo().IsPluginAvailable);

        var projection = MediaEntryMetadataProjection.Project(
            stack.Catalog.Get(entry.Id), store.GetRecordsForEntry(entry.Id));
        var description = Assert.Single(projection, field => field.Key == CatalogDescriptionService.DescriptionFieldKey);
        Assert.Equal("Stored generated description", description.Value);
        Assert.Equal(MediaMetadataOrigin.Captured, description.Origin);
        var comment = Assert.Single(projection, field => field.Key == "Comment");
        Assert.Equal("User comment", comment.Value);
        Assert.Equal(MediaMetadataOrigin.UserEdited, comment.Origin);

        var searchFields = new MediaCacheSearchFieldProvider(store).OpenLookup()(entry).ToArray();
        Assert.Contains(searchFields, field => field.Value == "Stored generated description" && field.Key != "Comment");
        Assert.Contains(searchFields, field => field.Key == "Comment" && field.Value == "User comment");
        Assert.Equal("User comment", stack.Catalog.Get(entry.Id)!.Properties["Comment"]);
    }

    [Fact]
    public void ReadingAndSearching_NeverInvokesTheProvider()
    {
        using var stack = new Stack();
        using var store = new CouchbaseMediaCacheStore(stack.Catalog.Collection.Database);
        stack.Catalog.Save("read-only", Entry("read-only"));
        SaveDescription(stack, store, "read-only", "Already captured");

        var capability = new FakeCapability((_, _) => throw new InvalidOperationException("provider must not run"));
        var service = new AiDescriptionService(stack.Catalog, store, capability);
        var details = service.Read("read-only");
        _ = service.GetProviderInfo();
        _ = MediaEntryMetadataProjection.Project(stack.Catalog.Get("read-only"), store.GetRecordsForEntry("read-only"));
        _ = new MediaCacheSearchFieldProvider(store).OpenLookup()(stack.Catalog.Get("read-only")!).ToArray();

        Assert.Equal("Already captured", details.Text);
        Assert.Equal(0, capability.Calls);
    }

    [Fact]
    public async Task ExplicitGeneration_SkipsUnchanged_AndRegeneratesOnlyWhenAsked()
    {
        using var stack = new Stack();
        using var store = new CouchbaseMediaCacheStore(stack.Catalog.Collection.Database);
        stack.Catalog.Save("entry", Entry("entry"));
        var capability = new FakeCapability();
        var service = new AiDescriptionService(stack.Catalog, store, capability);

        var first = await service.GenerateAsync("entry", regenerate: false, CancellationToken.None);
        Assert.False(first.Skipped);
        Assert.Equal("generated-1", first.Details.Text);
        Assert.Equal(1, capability.Calls);

        var second = await service.GenerateAsync("entry", regenerate: false, CancellationToken.None);
        Assert.True(second.Skipped);
        Assert.Equal("generated-1", second.Details.Text);
        Assert.Equal(1, capability.Calls);

        var third = await service.GenerateAsync("entry", regenerate: true, CancellationToken.None);
        Assert.False(third.Skipped);
        Assert.Equal("generated-2", third.Details.Text);
        Assert.Equal(2, capability.Calls);
        Assert.Equal(AiDescriptionState.Current, third.Details.State);
    }

    [Fact]
    public void ChangedEntry_MarksDescriptionStale_ButKeepsItReadable()
    {
        using var stack = new Stack();
        using var store = new CouchbaseMediaCacheStore(stack.Catalog.Collection.Database);
        stack.Catalog.Save("entry", Entry("entry", name: "Original"));
        SaveDescription(stack, store, "entry", "Description of the original");

        var changed = Entry("entry", name: "Renamed");
        stack.Catalog.Save("entry", changed);

        var details = new AiDescriptionService(stack.Catalog, store).Read("entry");
        Assert.Equal(AiDescriptionState.Stale, details.State);
        Assert.True(details.HasText);
        Assert.Equal("Description of the original", details.Text);
    }

    [Fact]
    public async Task FailedGeneration_SurfacesErrorAndKeepsEarlierDescription()
    {
        using var stack = new Stack();
        using var store = new CouchbaseMediaCacheStore(stack.Catalog.Collection.Database);
        stack.Catalog.Save("entry", Entry("entry", comment: "User comment"));
        SaveDescription(stack, store, "entry", "Earlier accepted");
        var capability = new FakeCapability((_, _) =>
            Task.FromException<GeneratedDescription>(new InvalidOperationException("agent crashed")));
        var service = new AiDescriptionService(stack.Catalog, store, capability);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateAsync("entry", regenerate: true, CancellationToken.None));
        Assert.Equal("Earlier accepted", service.Read("entry").Text);
        Assert.Equal("User comment", stack.Catalog.Get("entry")!.Properties["Comment"]);

        var viewModel = new DescriptionViewModel(catalogDescription: null, service: service, entryId: "entry");
        await viewModel.GenerateCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasError);
        Assert.Contains("agent crashed", viewModel.ErrorText, StringComparison.Ordinal);
        Assert.Equal("Earlier accepted", viewModel.DescriptionText);
        Assert.Equal("User comment", stack.Catalog.Get("entry")!.Properties["Comment"]);
    }

    [Fact]
    public async Task CatalogDescription_WinsOverStoredAiText_AndIsNotRegenerated()
    {
        using var stack = new Stack();
        using var store = new CouchbaseMediaCacheStore(stack.Catalog.Collection.Database);
        var entry = Entry("entry", comment: "Existing comment");
        entry.Properties["Description"] = "Imported description";
        stack.Catalog.Save("entry", entry);
        SaveDescription(stack, store, "entry", "Generated text");
        var capability = new FakeCapability((_, _) =>
            Task.FromException<GeneratedDescription>(new InvalidOperationException("generation must not run")));
        var service = new AiDescriptionService(stack.Catalog, store, capability);

        var viewModel = new DescriptionViewModel("Imported description", service, "entry");

        Assert.True(viewModel.HasDescription);
        Assert.False(viewModel.IsAiSourced);
        Assert.Equal("Imported description", viewModel.DescriptionText);
        Assert.False(viewModel.CanGenerate);
        Assert.False(viewModel.CanClear);

        await viewModel.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(0, capability.Calls);
        Assert.Equal("Imported description", stack.Catalog.Get("entry")!.Properties["Description"]);
        Assert.Equal("Generated text", service.Read("entry").Text);
        Assert.Equal("Existing comment", stack.Catalog.Get("entry")!.Properties["Comment"]);
    }

    [Fact]
    public void AiDescription_FillsEmptyDescription_AndCopyOrClearLeaveCommentsAlone()
    {
        using var stack = new Stack();
        using var store = new CouchbaseMediaCacheStore(stack.Catalog.Collection.Database);
        stack.Catalog.Save("entry", Entry("entry", comment: "Existing comment"));
        SaveDescription(stack, store, "entry", "Generated text");
        var service = new AiDescriptionService(stack.Catalog, store);

        var viewModel = new DescriptionViewModel(catalogDescription: null, service: service, entryId: "entry");
        string? copied = null;
        viewModel.CopyRequested += text => copied = text;

        Assert.True(viewModel.IsAiSourced);
        Assert.Equal("Generated text", viewModel.DescriptionText);
        Assert.Contains("fake-agent", viewModel.SourceText, StringComparison.Ordinal);

        viewModel.CopyCommand.Execute(null);
        Assert.Equal("Generated text", copied);
        Assert.Equal("Existing comment", stack.Catalog.Get("entry")!.Properties["Comment"]);

        viewModel.ClearCommand.Execute(null);
        Assert.False(viewModel.HasDescription);
        Assert.Null(service.Read("entry").Text);
        Assert.Equal("Existing comment", stack.Catalog.Get("entry")!.Properties["Comment"]);
    }

    [Fact]
    public async Task Batch_ReportsPerItemOutcomes_WithBoundedConcurrency()
    {
        using var stack = new Stack();
        using var store = new CouchbaseMediaCacheStore(stack.Catalog.Collection.Database);
        foreach (var id in new[] { "a", "b", "c", "d" })
        {
            stack.Catalog.Save(id, Entry(id));
        }

        var running = 0;
        var observed = 0;
        var capability = new FakeCapability(async (context, cancellationToken) =>
        {
            var current = Interlocked.Increment(ref running);
            InterlockedMax(ref observed, current);
            await Task.Delay(80, cancellationToken);
            Interlocked.Decrement(ref running);
            if (context.EntryId == "b")
            {
                throw new InvalidOperationException("per-item failure");
            }

            return FakeCapability.Describe("generated for " + context.EntryId);
        });
        var service = new AiDescriptionService(stack.Catalog, store, capability);

        var result = await service.GenerateBatchAsync(["a", "b", "c", "d"], regenerate: false, progress: null, CancellationToken.None);

        Assert.Equal(4, result.Items.Count);
        Assert.Equal(3, result.GeneratedCount);
        Assert.Equal(1, result.FailedCount);
        var failure = Assert.Single(result.Items, item => item.Outcome == AiDescriptionBatchItemOutcome.Failed);
        Assert.Equal("b", failure.EntryId);
        Assert.Contains("per-item failure", failure.Error, StringComparison.Ordinal);
        Assert.True(observed <= AiDescriptionService.MaxBatchConcurrency, $"observed {observed} concurrent calls");
        Assert.Equal("generated for a", service.Read("a").Text);
        Assert.Equal(AiDescriptionState.Unavailable, service.Read("b").State);
    }

    [Fact]
    public async Task Batch_SkipsUnchangedEntries_UnlessRegenerationIsRequested()
    {
        using var stack = new Stack();
        using var store = new CouchbaseMediaCacheStore(stack.Catalog.Collection.Database);
        stack.Catalog.Save("a", Entry("a"));
        stack.Catalog.Save("b", Entry("b"));
        SaveDescription(stack, store, "a", "Existing description");
        var capability = new FakeCapability();
        var service = new AiDescriptionService(stack.Catalog, store, capability);

        var result = await service.GenerateBatchAsync(["a", "b"], regenerate: false, progress: null, CancellationToken.None);

        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(1, result.GeneratedCount);
        Assert.Equal("Existing description", service.Read("a").Text);
        Assert.Equal(1, capability.Calls);
    }

    [Fact]
    public async Task CancelledBatch_MarksUnfinishedItemsCancelled_AndLeavesEarlierDescriptions()
    {
        using var stack = new Stack();
        using var store = new CouchbaseMediaCacheStore(stack.Catalog.Collection.Database);
        foreach (var id in new[] { "a", "b", "c", "d" })
        {
            stack.Catalog.Save(id, Entry(id));
        }

        stack.Catalog.Save("a", Entry("a", comment: "User comment"));
        SaveDescription(stack, store, "a", "Earlier accepted");
        var capability = new FakeCapability(async (context, cancellationToken) =>
        {
            await Task.Delay(400, cancellationToken);
            return FakeCapability.Describe("generated for " + context.EntryId);
        });
        var service = new AiDescriptionService(stack.Catalog, store, capability);
        using var cancellation = new CancellationTokenSource();

        var run = service.GenerateBatchAsync(["a", "b", "c", "d"], regenerate: false, progress: null,
            cancellation.Token);
        await Task.Delay(60);
        cancellation.Cancel();
        var result = await run;

        Assert.Contains(result.Items, item => item.Outcome == AiDescriptionBatchItemOutcome.Cancelled);
        Assert.DoesNotContain(result.Items, item => item.Outcome == AiDescriptionBatchItemOutcome.Failed);
        Assert.Equal("Earlier accepted", service.Read("a").Text);
        Assert.Equal("User comment", stack.Catalog.Get("a")!.Properties["Comment"]);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var previous = Interlocked.CompareExchange(ref target, value, current);
            if (previous == current)
            {
                return;
            }

            current = previous;
        }
    }
}
