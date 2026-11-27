using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Feature.GenDesc.Localization;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.SDK.Capabilities.Ui;
using SkyCD.SDK.Localization;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.GenDesc.Tests;

/// <summary>Registers the plugin catalogs so notification assertions see real messages.</summary>
internal static class GenDescTestLocalization
{
    private static readonly object Gate = new();
    private static bool registered;

    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (registered) return;
            LocalizationService.Current.Register("plugins.skycd.ai-gendesc", new Provider());
            registered = true;
        }
    }

    private sealed class Provider : ITranslationProvider
    {
        public IReadOnlyDictionary<string, string> GetTranslations() => GenDescLocalization.Translations;
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

internal sealed class FakeDescriptionEntryFactsApi : IDescriptionEntryFactsApi
{
    private readonly Func<string, DescriptionEntryFacts?> resolve;

    public FakeDescriptionEntryFactsApi(DescriptionEntryFacts? context)
        : this(_ => context)
    {
    }

    public FakeDescriptionEntryFactsApi(Func<string, DescriptionEntryFacts?> resolve) => this.resolve = resolve;

    public string? LastEntryId { get; private set; }

    public Task<DescriptionEntryFacts?> GetEntryFactsAsync(string entryId,
        CancellationToken cancellationToken = default)
    {
        LastEntryId = entryId;
        return Task.FromResult(resolve(entryId));
    }
}

internal sealed class RecordingFileDialogApi : IHostFileDialogApi
{
    public List<OpenFileRequest> OpenRequests { get; } = [];

    public List<SaveFileRequest> SaveRequests { get; } = [];

    public string? OpenResult { get; set; }

    public string? SaveResult { get; set; }

    public Task<string?> ShowOpenAsync(OpenFileRequest request)
    {
        OpenRequests.Add(request);
        return Task.FromResult(OpenResult);
    }

    public Task<string?> ShowSaveAsync(SaveFileRequest request)
    {
        SaveRequests.Add(request);
        return Task.FromResult(SaveResult);
    }
}

internal sealed class RecordingDialogApi : IHostDialogApi
{
    public List<HostWindowRequest> Requests { get; } = [];

    public Task ShowAsync(HostWindowRequest request)
    {
        Requests.Add(request);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingClipboardApi : IClipboardApi
{
    public List<string> Texts { get; } = [];

    public Task SetTextAsync(string text)
    {
        Texts.Add(text);
        return Task.CompletedTask;
    }
}

internal static class GenDescTestData
{
    public static DescriptionEntryFacts Movie(string fileName = "Example.Movie.2024.1080p.mkv")
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "Example.Movie.2024.1080p",
            ["Title"] = "Example Movie",
            ["Year"] = "2024",
            ["Genre"] = "Action|Drama",
            ["Cast"] = "Actor One|Actor Two",
            ["Plot"] = "A short plot.",
            ["FileName"] = fileName,
            ["Size"] = "8.5 GB",
            ["SizeBytes"] = "9126805504",
            ["MimeType"] = "video/x-matroska"
        };
        return new DescriptionEntryFacts(fileName, ".mkv", "video/x-matroska", facts);
    }
}
