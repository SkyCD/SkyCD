using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.MediaCache.Exceptions;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #944: the adding progress window shows the newest thumbnail of the running operation, decoded off
/// the reporting thread, always replacing an older one and ignoring a late result of a superseded decode.
/// </summary>
public sealed class AddingProgressDialogThumbnailTests
{
    [Fact]
    public async Task ReportThumbnail_DecodesAndShowsTheLatestThumbnail()
    {
        var decoder = new ControlledDecoder();
        var vm = new AddingProgressDialogViewModel(TimeProvider.System, decoder);
        Assert.False(vm.HasLatestThumbnail);

        var first = vm.ReportThumbnail(new MediaThumbnail("a.png", [1, 2, 3], "image/png"));
        var firstImage = CreateImage();
        decoder.Complete(0, firstImage);
        await first;

        Assert.Same(firstImage, vm.LatestThumbnail);
        Assert.True(vm.HasLatestThumbnail);

        var second = vm.ReportThumbnail(new MediaThumbnail("b.png", [4, 5, 6], "image/png"));
        var secondImage = CreateImage();
        decoder.Complete(1, secondImage);
        await second;

        Assert.Same(secondImage, vm.LatestThumbnail);
        Assert.Equal(["image/png", "image/png"], decoder.ContentTypes);
    }

    [Fact]
    public async Task ReportThumbnail_LateResultOfAnOlderDecodeIsIgnored()
    {
        var decoder = new ControlledDecoder();
        var vm = new AddingProgressDialogViewModel(TimeProvider.System, decoder);

        var older = vm.ReportThumbnail(new MediaThumbnail("old.png", [1], "image/png"));
        var newer = vm.ReportThumbnail(new MediaThumbnail("new.png", [2], "image/png"));

        var newerImage = CreateImage();
        decoder.Complete(1, newerImage);
        await newer;

        var olderImage = CreateImage();
        decoder.Complete(0, olderImage);
        await older;

        Assert.Same(newerImage, vm.LatestThumbnail);
        Assert.True(decoder.WasCancelled(0));
    }

    [Fact]
    public async Task ReportThumbnail_DecodeFailureIsSwallowedAndLeavesTheSectionHidden()
    {
        var decoder = new FailingDecoder();
        var vm = new AddingProgressDialogViewModel(TimeProvider.System, decoder);

        await vm.ReportThumbnail(new MediaThumbnail("broken.png", [1, 2], "image/png"));

        Assert.Null(vm.LatestThumbnail);
        Assert.False(vm.HasLatestThumbnail);
        Assert.Equal(1, decoder.Calls);
    }

    [Fact]
    public async Task ReportThumbnail_AfterCancelIsNotShown()
    {
        var decoder = new ControlledDecoder();
        var vm = new AddingProgressDialogViewModel(TimeProvider.System, decoder) { CanCancel = true };

        var running = vm.ReportThumbnail(new MediaThumbnail("a.png", [1], "image/png"));
        vm.CancelCommand.Execute(null);

        decoder.Complete(0, CreateImage());
        await running;

        Assert.Null(vm.LatestThumbnail);
        Assert.False(vm.HasLatestThumbnail);
    }

    [Fact]
    public async Task ReportThumbnail_AfterFinishCancellationIsIgnored()
    {
        var decoder = new ControlledDecoder();
        var vm = new AddingProgressDialogViewModel(TimeProvider.System, decoder);
        vm.FinishCancellation();

        await vm.ReportThumbnail(new MediaThumbnail("late.png", [1], "image/png"));

        Assert.False(vm.HasLatestThumbnail);
        Assert.Equal(0, decoder.StartedCount);
    }

    private static IImage CreateImage() => new StubImage();

    private sealed class StubImage : IImage
    {
        public Size Size => new(2, 2);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect) =>
            throw new NotSupportedException();
    }

    private sealed class ControlledDecoder : IPreviewImageDecoder
    {
        private readonly object gate = new();
        private readonly System.Collections.Generic.List<(TaskCompletionSource<IImage?> Source, CancellationToken Token)>
            pending = [];

        public System.Collections.Generic.List<string> ContentTypes { get; } = [];

        public int StartedCount
        {
            get
            {
                lock (gate)
                {
                    return pending.Count;
                }
            }
        }

        public Task<IImage?> DecodeAsync(byte[] data, string contentType, long declaredLength,
            CancellationToken cancellationToken)
        {
            var source = new TaskCompletionSource<IImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate)
            {
                ContentTypes.Add(contentType);
                pending.Add((source, cancellationToken));
            }

            return source.Task;
        }

        public bool WasCancelled(int index)
        {
            lock (gate)
            {
                return pending[index].Token.IsCancellationRequested;
            }
        }

        public void Complete(int index, IImage? image)
        {
            TaskCompletionSource<IImage?> source;
            lock (gate)
            {
                source = pending[index].Source;
            }

            source.SetResult(image);
        }
    }

    private sealed class FailingDecoder : IPreviewImageDecoder
    {
        public int Calls { get; private set; }

        public async Task<IImage?> DecodeAsync(byte[] data, string contentType, long declaredLength,
            CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Yield();
            throw new MediaPreviewDecodeException(contentType, declaredLength,
                new InvalidDataException("no pixels"));
        }
    }
}
