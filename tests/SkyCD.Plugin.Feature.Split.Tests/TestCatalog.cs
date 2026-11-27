using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Plugin.FileFormat.Json;
using SkyCD.Plugin.FileFormat.LegacySkyCD;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Feature.Split.Localization;
using SkyCD.Presentation.Catalog;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.SDK.Capabilities.Ui;
using SkyCD.SDK.Localization;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.Split.Tests;

/// <summary>
/// Stands in for the host: exposes the catalog through <see cref="ICatalogApi"/> and writes through
/// <see cref="ICatalogSaveHostApi"/> using the real JSON and legacy SCD format plugins, so every split writes
/// files a loaded format reads back.
/// </summary>
internal sealed class TestCatalog
{
    public const string JsonFormatId = "skycd-json";
    public const string LegacyFormatId = "legacy-scd";

    private readonly Dictionary<string, CatalogDocument> byId;

    public TestCatalog(IReadOnlyList<CatalogDocument> documents)
    {
        Documents = documents;
        byId = documents.ToDictionary(static document => document.Id, StringComparer.Ordinal);
        Manager = new FileFormatManager([new LegacyScdPlugin(), new JsonCatalogPlugin()]);
        Api = new TestCatalogApi(this);
        SaveHost = new TestSaveHost(this);
    }

    public IReadOnlyList<CatalogDocument> Documents { get; }

    public FileFormatManager Manager { get; }

    public ICatalogApi Api { get; }

    public ICatalogSaveHostApi SaveHost { get; }

    public string? OpenedCatalogPath
    {
        get => ((TestCatalogApi)Api).OpenedCatalogPath;
        set => ((TestCatalogApi)Api).OpenedCatalogPath = value;
    }

    public IReadOnlyList<FileFormatDataLossItem> ScopeItems
    {
        get => ((TestSaveHost)SaveHost).ScopeItems;
        set => ((TestSaveHost)SaveHost).ScopeItems = value;
    }

    public IReadOnlyList<CatalogEntry> Entries() => Api.GetEntriesAsync().GetAwaiter().GetResult();

    private CatalogEntry Project(CatalogDocument document) => CatalogApiAdapter.Project(document, byId);

    private IReadOnlyList<CatalogDocument> Resolve(IReadOnlyList<CatalogEntry> entries)
    {
        var documents = new List<CatalogDocument>(entries.Count);
        foreach (var entry in entries)
        {
            if (!byId.TryGetValue(entry.Id, out var document))
            {
                throw new InvalidOperationException($"Entry '{entry.Id}' is not in the test catalog.");
            }

            documents.Add(document);
        }

        return documents;
    }

    private sealed class TestCatalogApi(TestCatalog catalog) : ICatalogApi
    {
        public string? OpenedCatalogPath { get; set; } = "catalog.json";

        public Task<IReadOnlyList<CatalogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<CatalogEntry>>(
                catalog.Documents.Select(catalog.Project).ToArray());
        }

        public Task<CatalogEntry?> GetEntryAsync(string id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(catalog.byId.TryGetValue(id, out var document)
                ? catalog.Project(document)
                : null);
        }
    }

    private sealed class TestSaveHost(TestCatalog catalog) : ICatalogSaveHostApi
    {
        public IReadOnlyList<FileFormatDataLossItem> ScopeItems { get; set; } = [];

        public int MeasureCalls { get; private set; }

        public IReadOnlyList<FileFormatDescriptor> GetRewritableFormats() =>
            catalog.Manager.GetSaveFormats().Where(static format => format.CanRead).ToList();

        public string ResolveRewritableFormatId(string? path)
        {
            var rewritable = GetRewritableFormats();
            if (rewritable.Count == 0)
            {
                return string.Empty;
            }

            var extension = Path.GetExtension(path ?? string.Empty);
            return rewritable.FirstOrDefault(format => format.Extensions.Any(candidate =>
                    ("." + candidate.Trim().TrimStart('.')).Equals(extension, StringComparison.OrdinalIgnoreCase)))
                ?.FormatId ?? rewritable[0].FormatId;
        }

        public IReadOnlyList<FileFormatDataLossItem> CollectCatalogScopeItems() => ScopeItems;

        public IReadOnlyList<FileFormatDataLossItem> AssessDataLoss(
            string formatId,
            string fileName,
            IReadOnlyList<CatalogEntry> entries,
            IReadOnlyList<FileFormatDataLossItem> catalogScopeItems)
        {
            return catalog.Manager.AssessDataLoss(formatId, fileName,
                CatalogSaveSnapshot.BuildEntries(catalog.Resolve(entries)), catalogScopeItems);
        }

        public async Task WriteAsync(
            string formatId,
            string fileName,
            Stream target,
            IReadOnlyList<CatalogEntry> entries,
            IReadOnlyList<FileFormatDataLossItem> catalogScopeItems,
            CancellationToken cancellationToken = default)
        {
            MeasureCalls++;
            await catalog.Manager.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = formatId,
                FileName = fileName,
                Target = target,
                Payload = CatalogSaveSnapshot.BuildRows(catalog.Resolve(entries)),
                CatalogScopeItems = catalogScopeItems
            }, cancellationToken);
        }

        public Task WriteFileAsync(
            string formatId,
            string path,
            IReadOnlyList<CatalogEntry> entries,
            IReadOnlyList<FileFormatDataLossItem> catalogScopeItems,
            CancellationToken cancellationToken = default)
        {
            return catalog.Manager.WriteFileAsync(
                formatId,
                path,
                CatalogSaveSnapshot.BuildRows(catalog.Resolve(entries)),
                cancellationToken: cancellationToken,
                catalogScopeItems: catalogScopeItems);
        }
    }
}

/// <summary>Registers the plugin catalogs so dialog and notification assertions see real messages.</summary>
internal static class SplitTestLocalization
{
    public const string Namespace = "plugins.SkyCD.Plugin.Feature.Split";

    private static readonly object Gate = new();
    private static bool registered;

    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (registered)
            {
                return;
            }

            LocalizationService.Current.Register(Namespace, new Provider());
            registered = true;
        }
    }

    private sealed class Provider : ITranslationProvider
    {
        public IReadOnlyDictionary<string, string> GetTranslations() => new SplitPlugin().GetTranslations();
    }
}

internal sealed class RecordingHostCommandApi : IHostCommandApi
{
    public List<string> Messages { get; } = [];

    public Task NavigateToNodeAsync(long nodeId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task NotifyAsync(string message, CancellationToken cancellationToken = default)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingDialogApi : IHostDialogApi
{
    public List<HostWindowRequest> Requests { get; } = [];

    public Queue<QuestionResult> Answers { get; } = new();

    public List<QuestionRequest> Questions { get; } = [];

    public Task ShowAsync(HostWindowRequest request)
    {
        Requests.Add(request);
        return Task.CompletedTask;
    }

    public Task<QuestionResult> AskAsync(QuestionRequest request)
    {
        Questions.Add(request);
        return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : QuestionResult.Cancel);
    }
}
