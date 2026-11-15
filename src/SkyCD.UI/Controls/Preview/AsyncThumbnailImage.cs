using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SkyCD.UI.Controls.Preview;

/// <summary>
/// An <see cref="Image"/> that resolves its source asynchronously from an
/// <see cref="IPreviewThumbnailProvider"/>. Only realized containers request a thumbnail, so a
/// virtualized list never loads more than what is on screen, and a request is cancelled as soon as the
/// key changes or the control leaves the visual tree. While no thumbnail is available the control stays
/// collapsed, which lets a fallback icon underneath remain visible.
/// </summary>
public sealed class AsyncThumbnailImage : Image
{
    public static readonly StyledProperty<object?> ThumbnailKeyProperty =
        AvaloniaProperty.Register<AsyncThumbnailImage, object?>(nameof(ThumbnailKey));

    public static readonly StyledProperty<IPreviewThumbnailProvider?> ThumbnailProviderProperty =
        AvaloniaProperty.Register<AsyncThumbnailImage, IPreviewThumbnailProvider?>(nameof(ThumbnailProvider));

    private CancellationTokenSource? loadCancellation;
    private object? requestedKey;

    public AsyncThumbnailImage()
    {
        IsVisible = false;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// The item the thumbnail belongs to. When unset the control uses its own data context, which is how
    /// an item template addresses the item it was realized for.
    /// </summary>
    public object? ThumbnailKey
    {
        get => GetValue(ThumbnailKeyProperty);
        set => SetValue(ThumbnailKeyProperty, value);
    }

    public IPreviewThumbnailProvider? ThumbnailProvider
    {
        get => GetValue(ThumbnailProviderProperty);
        set => SetValue(ThumbnailProviderProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ThumbnailKeyProperty || change.Property == ThumbnailProviderProperty)
        {
            RequestThumbnail();
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e) => RequestThumbnail();

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => CancelLoad();

    private void RequestThumbnail()
    {
        CancelLoad();

        var key = ThumbnailKey ?? DataContext;
        var provider = ThumbnailProvider;
        if (key is null || provider is null)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        loadCancellation = cancellation;
        requestedKey = key;
        _ = LoadAsync(provider, key, cancellation.Token);
    }

    private async Task LoadAsync(IPreviewThumbnailProvider provider, object key, CancellationToken cancellationToken)
    {
        IImage? image;
        try
        {
            image = await provider.GetThumbnailAsync(key, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // An undecodable or evicted thumbnail keeps the fallback icon; it must never surface as an error.
            return;
        }

        if (cancellationToken.IsCancellationRequested || image is null || !ReferenceEquals(requestedKey, key))
        {
            return;
        }

        Source = image;
        IsVisible = true;
    }

    private void CancelLoad()
    {
        requestedKey = null;
        IsVisible = false;
        Source = null;

        var cancellation = loadCancellation;
        loadCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }
}
