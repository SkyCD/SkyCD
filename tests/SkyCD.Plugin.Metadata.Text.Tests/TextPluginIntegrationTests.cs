using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Preview;
using Xunit;
using SkyCD.Plugin.Metadata.Text.Models;

namespace SkyCD.Plugin.Metadata.Text.Tests;

/// <summary>
/// End-to-end coverage for the text plugin inside the host pipeline, including the property that makes a
/// stored excerpt useful: it is searchable offline through the browser's generic field provider, and the
/// truncation flag tells a consumer when a miss may lie beyond the excerpt.
/// </summary>
public sealed class TextPluginIntegrationTests : IDisposable
{
    private const string PluginId = "SkyCD.Plugin.Text";
    private const string MediaRootId = "media-root";
    private const string ReadmeEntryId = "file-readme-1";

    private readonly string databaseDirectory =
        Path.Combine(Path.GetTempPath(), $"skycd-text-plugin-{Guid.NewGuid():N}");

    private readonly string? previousDatabaseDirectory =
        Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public TextPluginIntegrationTests()
    {
        Directory.CreateDirectory(databaseDirectory);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", databaseDirectory);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", previousDatabaseDirectory);

        try
        {
            if (Directory.Exists(databaseDirectory))
            {
                Directory.Delete(databaseDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Couchbase Lite can release file handles slightly after dispose on some systems.
        }
        catch (UnauthorizedAccessException)
        {
            // Files can stay locked briefly after the database is closed on Windows.
        }
    }

    [Fact]
    public async Task Host_RunsBothCapabilitiesForATextSource()
    {
        var text = TextFixtures.Markdown();
        var results = await CreateManager().ProcessAsync(
            new MediaItem(".md", "text/markdown", text.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(text)));

        var metadata = results.Single(result => result.Metadata is not null);
        var preview = results.Single(result => result.Preview is not null);

        Assert.False(metadata.Failed);
        Assert.False(preview.Failed);
        Assert.Equal("skycd.text.metadata", metadata.Schema!.Id);
        Assert.Equal("markdown", metadata.Metadata!.Values.Single(value => value.Key == TextSchema.TextKind).Text);

        var artifacts = preview.Preview!.Artifacts;
        Assert.Equal(2, artifacts.Count);
        Assert.Equal("text/plain", artifacts[0].ContentType);
        Assert.Equal("image/png", artifacts[1].ContentType);
    }

    [Fact]
    public void Host_ClaimsOnlyTheAllowListedTextTypes()
    {
        var manager = CreateManager();

        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".txt", null, 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".MD", "text/markdown", 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".csv", null, 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".cs", null, 10)));

        Assert.Empty(manager.ResolveProviders(new MediaItem(".docx", null, 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".xlsx", null, 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".pdf", "application/pdf", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".jpg", "image/jpeg", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".mp3", "audio/mpeg", 10)));
    }

    [Fact]
    public async Task BinaryContent_IsReportedAsAFailedProviderInsteadOfBreakingThePass()
    {
        var binary = TextFixtures.BinaryMasqueradingAsText();
        var results = await CreateManager().ProcessAsync(
            new MediaItem(".txt", "text/plain", binary.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(binary)));

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.True(result.Failed));
        Assert.All(results, result => Assert.Null(result.Metadata));
    }

    [Fact]
    public async Task HostSourceBudget_RejectsTextLargerThanTheBudget()
    {
        var text = TextFixtures.Utf8(new string('q', 4096));
        var manager = CreateManager(new MediaExecutionLimits(SourceBytes: 32));

        var results = await manager.ProcessAsync(
            new MediaItem(".txt", "text/plain", text.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(text)));

        Assert.All(results, result => Assert.True(result.Failed));
    }

    [Fact]
    public async Task CapturedExcerpt_IsBrowsableAsTextAndSearchable_WithoutThePluginOrTheSource()
    {
        var text = TextFixtures.Utf8("needle in the haystack\nsecond line\n");

        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            var entry = SeedCatalog(catalog, "notes.txt", text.Length);
            using var store = MediaCacheStore.Create(database);

            var outcome = await CreateScheduler(store).ProcessAsync(
                MediaExtractionRequest.ForCatalogEntry(
                    entry, _ => Task.FromResult<Stream>(new MemoryStream(text)), catalog.GetAll()));

            Assert.True(outcome.Published);
        }

        // Reopened with no plugin registered and no source reachable: only stored bytes remain.
        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            using var store = MediaCacheStore.Create(database);
            var entry = catalog.GetAll().Single(document => document.Id == ReadmeEntryId);

            var resolver = new MediaPreviewResolver(store);
            var snapshot = resolver.Resolve(entry);

            Assert.Equal(MediaPreviewState.Ready, snapshot.State);
            Assert.Equal(PreviewPayloadKind.Text, snapshot.PayloadKind);
            Assert.Equal(PluginId, snapshot.ProviderId);
            Assert.Contains("needle in the haystack", snapshot.TextPreview, StringComparison.Ordinal);
            Assert.Equal("text/plain", snapshot.Artifact!.ContentType);

            // The list thumbnail resolves the rendered page separately, so the text pane and the browser
            // thumbnail coexist without either displacing the other.
            var thumbnail = resolver.ResolveThumbnail(entry);
            Assert.NotNull(thumbnail);
            Assert.Equal(PreviewPayloadKind.Image, thumbnail!.PayloadKind);
            Assert.Equal("image/png", thumbnail.Artifact!.ContentType);

            Assert.Equal("utf-8", FieldText(snapshot, TextSchema.Encoding));
            Assert.Equal(2L, FieldInteger(snapshot, TextSchema.LineCount));
            Assert.Equal(true, Field(snapshot, TextSchema.ScanComplete).Value);
            Assert.Equal(false, Field(snapshot, TextSchema.ExcerptTruncated).Value);

            // The stored excerpt is reachable through the generic search field provider, with no
            // text-specific code anywhere in the search path.
            var documents = catalog.GetAll();
            var provider = new MediaCacheSearchFieldProvider(store);

            var hit = Assert.Single(CatalogFindService.Search(documents, "haystack", provider));
            Assert.Equal(ReadmeEntryId, hit.Document.Id);
            Assert.Equal(MediaMetadataValueFormatter.HumanizeKey(TextSchema.TextExcerpt), hit.MatchedField);

            Assert.Empty(CatalogFindService.Search(documents, "haystack"));
        }
    }

    [Fact]
    public async Task TruncatedExcerpt_FlagsThatSearchCannotSeeBeyondIt()
    {
        // The searchable term sits past the excerpt bound, so it is genuinely absent from the stored data.
        var text = TextFixtures.Utf8(new string('a', 4000) + "hidden-token");

        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var entry = SeedCatalog(catalog, "long.log", text.Length);
        using var store = MediaCacheStore.Create(database);

        var scheduler = new MediaExtractionScheduler(
            new MediaExtensionManager(Providers(new TextPluginOptions(maxExcerptCharacters: 500))), store);

        var outcome = await scheduler.ProcessAsync(
            MediaExtractionRequest.ForCatalogEntry(
                entry, _ => Task.FromResult<Stream>(new MemoryStream(text)), catalog.GetAll()));

        Assert.True(outcome.Published);

        var snapshot = new MediaPreviewResolver(store).Resolve(entry);

        Assert.Equal(true, Field(snapshot, TextSchema.ExcerptTruncated).Value);

        // The excerpt is bounded, while the character count still describes everything that was scanned.
        Assert.Equal(500, FieldText(snapshot, TextSchema.TextExcerpt)!.Length);
        Assert.Equal(4012L, FieldInteger(snapshot, TextSchema.CharacterCount));
        Assert.Equal(true, Field(snapshot, TextSchema.ScanComplete).Value);

        var provider = new MediaCacheSearchFieldProvider(store);
        Assert.Empty(CatalogFindService.Search(catalog.GetAll(), "hidden-token", provider));

        // The flag is what tells a consumer this miss means "outside the excerpt", not "not in the file".
        Assert.Equal(text.Length, FieldInteger(snapshot, TextSchema.ByteLength));
    }

    [Fact]
    public async Task CancelledExtraction_PublishesNothing()
    {
        var text = TextFixtures.Utf8();
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var entry = SeedCatalog(catalog, "notes.txt", text.Length);
        using var store = MediaCacheStore.Create(database);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateScheduler(store).ProcessAsync(
            MediaExtractionRequest.ForCatalogEntry(
                entry, _ => Task.FromResult<Stream>(new MemoryStream(text)), catalog.GetAll()),
            progress: null,
            cancelled.Token));

        Assert.Empty(store.GetRecordsForEntry(entry.Id));
    }

    [Fact]
    public void AssemblyDiscovery_FindsBothCapabilities_FromMetadataOnly()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(TextMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("Plain Text Previews and Searchable Excerpts", plugin.Name);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaMetadataCapability);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaPreviewCapability);
        Assert.Equal(2, plugin.Capabilities.Count);
    }

    [Fact]
    public void PluginProject_ShipsNoDiscoveryManifest()
    {
        var projectDirectory = FindPluginProjectDirectory();

        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
        Assert.Empty(Directory.EnumerateFiles(projectDirectory, "plugin.json", SearchOption.AllDirectories));
    }

    private static string FieldText(EntryPreviewSnapshot snapshot, string key) =>
        Field(snapshot, key).Value as string
        ?? throw new InvalidOperationException($"Field '{key}' is not text.");

    private static long FieldInteger(EntryPreviewSnapshot snapshot, string key) =>
        Field(snapshot, key).Value is long value
            ? value
            : throw new InvalidOperationException($"Field '{key}' is not an integer.");

    private static MediaMetadataFieldView Field(EntryPreviewSnapshot snapshot, string key) =>
        snapshot.Fields.Single(field => field.Key == key);

    private static MediaProvider[] Providers(TextPluginOptions? options = null)
    {
        var effective = options ?? TextPluginOptions.Default;
        return
        [
            new MediaProvider(PluginId, "1.0.0", new TextMetadataPlugin(effective)),
            new MediaProvider(PluginId, "1.0.0", new TextPreviewPlugin(effective))
        ];
    }

    private static MediaExtensionManager CreateManager(MediaExecutionLimits? limits = null) =>
        new(Providers(), limits);

    private static MediaExtractionScheduler CreateScheduler(IMediaCacheStore store) =>
        new(new MediaExtensionManager(Providers()), store);

    private Database OpenDatabase() =>
        new("text-plugin-tests", new DatabaseConfiguration { Directory = databaseDirectory });

    private static CatalogDocumentRepository CreateCatalogRepository(Database database)
    {
        var collection = database.GetCollection("catalog", Collection.DefaultScopeName)
                         ?? database.CreateCollection("catalog", Collection.DefaultScopeName);

        var repository = new CatalogDocumentRepository();
        repository.Initialize(typeof(CatalogDocument), "catalog", collection);
        return repository;
    }

    private static CatalogDocument SeedCatalog(CatalogDocumentRepository repository, string fileName, long entrySize)
    {
        var root = new CatalogDocument
        {
            Id = MediaRootId,
            Name = "Documents",
            ParentId = "library",
            Type = CatalogDocumentType.Media
        };

        var entry = new CatalogDocument
        {
            Id = ReadmeEntryId,
            Name = fileName,
            ParentId = root.Id,
            Type = CatalogDocumentType.File,

            // The host builds MediaItem.Length from the catalogued size and reads exactly that many bytes.
            Size = entrySize,
            Properties = new PropertiesCollection
            {
                ["FullName"] = Path.Combine(@"D:\Documents", fileName),
                ["LastWriteTime"] = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero).ToString("O")
            }
        };

        repository.Save(root.Id, root);
        repository.Save(entry.Id, entry);
        return entry;
    }

    private static string FindPluginProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.Metadata.Text");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the text plugin project directory.");
    }
}
