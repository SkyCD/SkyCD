using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.UI.Controls.Lists;
using SkyCD.UI.Controls.Preview;
using Xunit;

namespace SkyCD.UI.Tests;

public class AsyncThumbnailImageTests
{
    [AvaloniaFact]
    public async Task WithoutAProvider_StaysCollapsedAndRequestsNothing()
    {
        var provider = new StubThumbnailProvider();
        var image = new AsyncThumbnailImage { DataContext = new Item("a") };
        Dispatcher.UIThread.RunJobs();

        Assert.False(image.IsVisible);
        Assert.Null(image.Source);
        Assert.Empty(provider.Requests);

        image.ThumbnailProvider = provider;
        Dispatcher.UIThread.RunJobs();
        await provider.CompleteCurrentAsync(CreateImage());

        Assert.True(image.IsVisible);
        Assert.NotNull(image.Source);
    }

    [AvaloniaFact]
    public async Task NullResult_KeepsTheFallbackIconVisible()
    {
        var provider = new StubThumbnailProvider();
        var image = new AsyncThumbnailImage { DataContext = new Item("a"), ThumbnailProvider = provider };
        Dispatcher.UIThread.RunJobs();

        await provider.CompleteCurrentAsync(null);

        Assert.False(image.IsVisible);
        Assert.Null(image.Source);
    }

    [AvaloniaFact]
    public async Task ProviderFault_KeepsTheFallbackIconAndDoesNotThrow()
    {
        var provider = new StubThumbnailProvider();
        var image = new AsyncThumbnailImage { DataContext = new Item("a"), ThumbnailProvider = provider };
        Dispatcher.UIThread.RunJobs();

        provider.FailCurrent(new InvalidOperationException("undecodable artifact"));
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();

        Assert.False(image.IsVisible);
        Assert.Null(image.Source);
    }

    [AvaloniaFact]
    public async Task RapidKeyChanges_ApplyOnlyTheLatestThumbnailAndCancelTheRest()
    {
        var provider = new StubThumbnailProvider();
        var image = new AsyncThumbnailImage { DataContext = new Item("first"), ThumbnailProvider = provider };
        Dispatcher.UIThread.RunJobs();

        var firstRequest = provider.Requests.Single();

        image.DataContext = new Item("second");
        Dispatcher.UIThread.RunJobs();

        Assert.True(firstRequest.Cancellation.IsCancellationRequested);
        Assert.Equal(2, provider.Requests.Count);

        var latest = CreateImage();
        await provider.CompleteCurrentAsync(latest);

        Assert.Same(latest, image.Source);
        Assert.True(image.IsVisible);
    }

    [AvaloniaFact]
    public async Task StaleCompletion_IsIgnoredAfterTheKeyChanged()
    {
        var provider = new StubThumbnailProvider();
        var image = new AsyncThumbnailImage { DataContext = new Item("first"), ThumbnailProvider = provider };
        Dispatcher.UIThread.RunJobs();

        var firstRequest = provider.Requests.Single();

        image.DataContext = new Item("second");
        Dispatcher.UIThread.RunJobs();

        // The superseded load finishes late; its image must not be applied to the new item.
        firstRequest.Completion.TrySetResult(CreateImage());
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();

        Assert.False(image.IsVisible);
        Assert.Null(image.Source);
    }

    [AvaloniaFact]
    public async Task DetachingFromTheVisualTree_CancelsThePendingRequest()
    {
        var provider = new StubThumbnailProvider();
        var host = new Decorator();
        var image = new AsyncThumbnailImage { DataContext = new Item("a"), ThumbnailProvider = provider };
        host.Child = image;

        var window = new Window { Content = host };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var request = provider.Requests.Single();

        host.Child = null;
        Dispatcher.UIThread.RunJobs();

        Assert.True(request.Cancellation.IsCancellationRequested);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(BrowserViewMode.Details)]
    [InlineData(BrowserViewMode.List)]
    [InlineData(BrowserViewMode.SmallIcons)]
    [InlineData(BrowserViewMode.LargeIcons)]
    [InlineData(BrowserViewMode.Tiles)]
    public void BrowserList_OverlaysAnAsyncThumbnailOnlyWhenAProviderIsConfigured(BrowserViewMode mode)
    {
        var withoutProvider = BuildRow(mode, provider: null);
        Assert.DoesNotContain(withoutProvider, control => control is AsyncThumbnailImage);

        var withProvider = BuildRow(mode, new StubThumbnailProvider());
        var thumbnail = withProvider.OfType<AsyncThumbnailImage>().Single();
        Assert.False(thumbnail.IsVisible);
    }

    private static IEnumerable<Control> BuildRow(BrowserViewMode mode, IPreviewThumbnailProvider? provider)
    {
        var view = new BrowserItemsView { ViewMode = mode, ThumbnailProvider = provider };
        var template = mode == BrowserViewMode.Details
            ? view.FindControl<DetailsListView>("DetailsListView")!.RowTemplate!
            : view.FindControl<ListBox>(mode == BrowserViewMode.List ? "ListModeListBox" : "IconGridListBox")!.ItemTemplate!;

        var row = template.Build(new Item("a"))!;
        row.DataContext = new Item("a");
        Dispatcher.UIThread.RunJobs();

        return row.GetSelfAndVisualDescendants().OfType<Control>().ToList();
    }

    private static IImage CreateImage() =>
        new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    private sealed record Item(string Id);

    private sealed class StubThumbnailProvider : IPreviewThumbnailProvider
    {
        public List<Request> Requests { get; } = [];

        public Request Current => Requests[^1];

        public Task<IImage?> GetThumbnailAsync(object item, CancellationToken cancellationToken)
        {
            var request = new Request(item, cancellationToken, new TaskCompletionSource<IImage?>(
                TaskCreationOptions.RunContinuationsAsynchronously));
            Requests.Add(request);
            return request.Completion.Task;
        }

        /// <summary>
        /// Completes the in-flight request and lets the control apply it.
        /// </summary>
        /// <remarks>
        /// The pump has to run on the test's UI thread. <c>Dispatcher.UIThread.RunJobs</c> verifies thread
        /// affinity, so continuing on <c>TaskScheduler.Default</c> throws "the calling thread cannot access
        /// this object" whenever the pool thread wins the race — which is why this intermittently failed on
        /// Windows CI while passing elsewhere. Awaiting without ConfigureAwait resumes on the Avalonia
        /// synchronization context the headless test host installs.
        /// </remarks>
        public async Task CompleteCurrentAsync(IImage? image)
        {
            Current.Completion.TrySetResult(image);
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }

        public void FailCurrent(Exception exception) => Current.Completion.TrySetException(exception);
    }

    private sealed record Request(
        object Item,
        CancellationToken Cancellation,
        TaskCompletionSource<IImage?> Completion);
}
