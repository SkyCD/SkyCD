using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using SkyCD.Documents;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.MediaCache.Exceptions;
using SkyCD.UI.Controls.Preview;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Drives the catalog browser's details and preview surface from stored extraction results. Stored results
/// are read synchronously on the thread that owns the store and only decoding is offloaded, so no query can
/// outlive a catalog database that is closed right after a selection change. Selection changes are guarded
/// by a generation counter, so walking quickly through a catalog cannot apply an older entry's preview to a
/// newer selection and cannot block navigation while a payload is decoded.
/// </summary>
public partial class EntryPreviewViewModel : ObservableObject, IDisposable
{
    private readonly MediaPreviewResolver resolver;
    private readonly MediaPreviewImageService? images;
    private readonly IStringLocalizer valueLocalizer;
    private CancellationTokenSource? loadCancellation;
    private int generation;
    private bool isDisposed;

    public EntryPreviewViewModel(
        MediaPreviewResolver resolver,
        MediaPreviewImageService? images = null,
        IStringLocalizer? valueLocalizer = null)
    {
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        this.images = images;
        this.valueLocalizer = valueLocalizer ?? new PropertyValueLocalizer();

        StatusText = LocalizationService.Current.Get(PreviewKeys.StateNoSelection);
        StateMessage = LocalizationService.Current.Get(PreviewKeys.SelectItemToPreview);
    }

    [ObservableProperty] private MediaPreviewState state = MediaPreviewState.NoSelection;

    [ObservableProperty] private string? entryName;

    /// <summary>Short availability label for the pane header.</summary>
    [ObservableProperty] private string statusText = string.Empty;

    /// <summary>Explanation shown in place of a preview when there is nothing to display.</summary>
    [ObservableProperty] private string? stateMessage;

    [ObservableProperty] private IImage? previewImage;

    [ObservableProperty] private string? previewText;

    /// <summary>Screen-reader name of the preview image, including the entry it belongs to.</summary>
    [ObservableProperty] private string? previewAutomationName;

    [ObservableProperty] private IReadOnlyList<MediaPreviewFieldRow> fields = [];

    /// <summary>The snapshot currently on display; exposed for diagnostics and tests.</summary>
    public EntryPreviewSnapshot? CurrentSnapshot { get; private set; }

    /// <summary>The resolve started by the most recent selection change, so callers can await the pane settling.</summary>
    public Task CurrentLoad { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Re-renders labels and field values for the current culture without reading the store again, so a
    /// language switch does not discard an already decoded preview.
    /// </summary>
    public void RefreshLocalization()
    {
        var snapshot = CurrentSnapshot;
        if (snapshot is null)
        {
            StatusText = ResolveStatusText(State);
            return;
        }

        Apply(snapshot, PreviewImage, EntryName);
    }

    /// <summary>Selection changes start a background resolve; this is the awaitable form.</summary>
    public Task ShowAsync(CatalogDocument? entry)
    {
        var load = LoadAsync(entry);
        CurrentLoad = load;
        return load;
    }

    /// <summary>
    /// Queues <paramref name="entry"/> for display and returns immediately. Any resolve still running for a
    /// previous selection is cancelled and its result discarded.
    /// </summary>
    public void Show(CatalogDocument? entry)
    {
        CurrentLoad = LoadAsync(entry);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        CancelLoad();
        GC.SuppressFinalize(this);
    }

    private async Task LoadAsync(CatalogDocument? entry)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var current = Interlocked.Increment(ref generation);

        var cancellation = loadCancellation;
        loadCancellation = new CancellationTokenSource();
        cancellation?.Cancel();
        cancellation?.Dispose();
        var cancellationToken = loadCancellation.Token;

        if (entry is null || string.IsNullOrWhiteSpace(entry.Id))
        {
            Apply(EntryPreviewSnapshot.NoSelection, image: null, name: null);
            return;
        }

        EntryName = entry.Name;
        State = MediaPreviewState.Loading;
        StatusText = LocalizationService.Current.Get(PreviewKeys.StateLoading);
        PreviewImage = null;
        PreviewText = null;
        PreviewAutomationName = null;

        EntryPreviewSnapshot snapshot;
        IImage? image = null;
        try
        {
            // Resolved synchronously on the calling thread: the store read must complete before this method
            // yields, so no background query can outlive a catalog database that is closed right after a
            // selection change. Only the decode runs asynchronously.
            snapshot = resolver.Resolve(entry);

            if (snapshot.PayloadKind == PreviewPayloadKind.Image && images is not null)
            {
                image = await images.GetImageAsync(snapshot, cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (MediaPreviewDecodeException)
        {
            if (IsSuperseded(current))
            {
                return;
            }

            ApplyFailed(entry.Name);
            return;
        }
        catch (Exception)
        {
            // A store or decoder fault must never surface as an unhandled error on the UI thread.
            if (IsSuperseded(current))
            {
                return;
            }

            ApplyFailed(entry.Name);
            return;
        }

        if (IsSuperseded(current))
        {
            return;
        }

        Apply(snapshot, image, entry.Name);
    }

    private bool IsSuperseded(int captured) => Volatile.Read(ref generation) != captured || isDisposed;

    private void ApplyFailed(string? name)
    {
        CurrentSnapshot = null;
        State = MediaPreviewState.Failed;
        StatusText = LocalizationService.Current.Get(PreviewKeys.StateFailed);
        StateMessage = LocalizationService.Current.Get(PreviewKeys.PreviewFailed);
        PreviewImage = null;
        PreviewText = null;
        PreviewAutomationName = null;
        Fields = [];
        EntryName = name;
    }

    private void Apply(EntryPreviewSnapshot snapshot, IImage? image, string? name)
    {
        CurrentSnapshot = snapshot;
        EntryName = name;
        State = snapshot.State;
        StatusText = ResolveStatusText(snapshot.State);
        Fields = BuildFieldRows(snapshot.Fields);

        PreviewImage = snapshot.PayloadKind == PreviewPayloadKind.Image ? image : null;
        PreviewText = snapshot.PayloadKind == PreviewPayloadKind.Text ? snapshot.TextPreview : null;
        PreviewAutomationName = image is not null
            ? LocalizationService.Current.Format(PreviewKeys.PreviewImageAutomation, name ?? string.Empty)
            : null;
        StateMessage = ResolveStateMessage(snapshot, PreviewImage, PreviewText);
    }

    private IReadOnlyList<MediaPreviewFieldRow> BuildFieldRows(IReadOnlyList<MediaMetadataFieldView> views)
    {
        if (views.Count == 0)
        {
            return [];
        }

        var rows = new List<MediaPreviewFieldRow>(views.Count);
        foreach (var view in views)
        {
            rows.Add(new MediaPreviewFieldRow(
                MediaMetadataValueFormatter.ResolveLabel(view.Key),
                MediaMetadataValueFormatter.Format(view.Value, view.Type, view.Unit, valueLocalizer),
                ResolveHint(view),
                view.IsFromStaleRecord,
                view.IsUserEdited));
        }

        return rows;
    }

    private string? ResolveHint(MediaMetadataFieldView view)
    {
        if (view.IsUserEdited)
        {
            return view.CapturedValue is null
                ? LocalizationService.Current.Get(PreviewKeys.OriginUserEdited)
                : LocalizationService.Current.Format(
                    PreviewKeys.CapturedValueHint,
                    MediaMetadataValueFormatter.Format(view.CapturedValue, view.Type, view.Unit, valueLocalizer));
        }

        return view.IsFromStaleRecord ? LocalizationService.Current.Get(PreviewKeys.StaleValueHint) : null;
    }

    private static string ResolveStatusText(MediaPreviewState state) => LocalizationService.Current.Get(state switch
    {
        MediaPreviewState.Loading => PreviewKeys.StateLoading,
        MediaPreviewState.Ready => PreviewKeys.StateReady,
        MediaPreviewState.Stale => PreviewKeys.StateStale,
        MediaPreviewState.Unsupported => PreviewKeys.StateUnsupported,
        MediaPreviewState.Failed => PreviewKeys.StateFailed,
        MediaPreviewState.MissingArtifact => PreviewKeys.StateMissingArtifact,
        MediaPreviewState.TooLarge => PreviewKeys.StateTooLarge,
        MediaPreviewState.NoSelection => PreviewKeys.StateNoSelection,
        _ => PreviewKeys.StateUnavailable
    });

    private static string? ResolveStateMessage(EntryPreviewSnapshot snapshot, IImage? image, string? text)
    {
        if (snapshot.PayloadKind == PreviewPayloadKind.Image && image is not null)
        {
            return null;
        }

        if (snapshot.PayloadKind == PreviewPayloadKind.Text && !string.IsNullOrEmpty(text))
        {
            return null;
        }

        return snapshot.State switch
        {
            MediaPreviewState.NoSelection => LocalizationService.Current.Get(PreviewKeys.SelectItemToPreview),
            MediaPreviewState.Unsupported => LocalizationService.Current.Get(PreviewKeys.PreviewNotSupported),
            MediaPreviewState.Failed => LocalizationService.Current.Get(PreviewKeys.PreviewFailed),
            MediaPreviewState.MissingArtifact => LocalizationService.Current.Get(PreviewKeys.PreviewArtifactMissing),
            MediaPreviewState.TooLarge => LocalizationService.Current.Get(PreviewKeys.PreviewTooLarge),
            _ => LocalizationService.Current.Get(PreviewKeys.NoPreviewStored)
        };
    }

    private void CancelLoad()
    {
        var cancellation = loadCancellation;
        loadCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}
