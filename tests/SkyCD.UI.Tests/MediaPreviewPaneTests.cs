using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkyCD.UI.Controls.Preview;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.UI.Tests;

public class MediaPreviewPaneTests
{
    [AvaloniaFact]
    public void NoSelection_ShowsTheLocalizedHintAndNoPayload()
    {
        var pane = new MediaPreviewPane
        {
            State = MediaPreviewState.NoSelection,
            StateMessage = LocalizationService.Current.Get(PreviewKeys.SelectItemToPreview)
        };
        Dispatcher.UIThread.RunJobs();

        Assert.False(pane.IsBusy);
        Assert.False(pane.IsPreviewImageShown);
        Assert.False(pane.IsPreviewTextShown);
        Assert.True(pane.IsStateMessageShown);
        Assert.False(pane.AreFieldsShown);
        Assert.Equal(LocalizationService.Current.Get(PreviewKeys.SelectItemToPreview), Message(pane).Text);
    }

    [AvaloniaFact]
    public void Loading_ShowsTheBusyIndicatorAndNoPayload()
    {
        var pane = new MediaPreviewPane
        {
            State = MediaPreviewState.Loading,
            PreviewImage = CreateImage(),
            PreviewText = "cached body"
        };
        Dispatcher.UIThread.RunJobs();

        Assert.True(pane.IsBusy);
        Assert.True(Busy(pane).IsVisible);
        Assert.False(pane.IsPreviewImageShown);
        Assert.False(pane.IsPreviewTextShown);
        Assert.False(pane.IsStateMessageShown);
    }

    [AvaloniaFact]
    public void ReadyWithImage_ShowsTheImageAndHidesTheMessage()
    {
        var pane = new MediaPreviewPane
        {
            State = MediaPreviewState.Ready,
            EntryName = "cover.jpg",
            PreviewImage = CreateImage(),
            PreviewAutomationName = "Cached preview image of cover.jpg"
        };
        Dispatcher.UIThread.RunJobs();

        Assert.True(pane.IsPreviewImageShown);
        Assert.False(pane.IsPreviewTextShown);
        Assert.False(pane.IsStateMessageShown);
        Assert.True(Image(pane).IsVisible);
        Assert.Equal("Cached preview image of cover.jpg", AutomationProperties.GetName(Image(pane)));
    }

    [AvaloniaFact]
    public void ReadyWithText_ShowsTheTextBodyInsteadOfAnImage()
    {
        var pane = new MediaPreviewPane
        {
            State = MediaPreviewState.Ready,
            PreviewText = "line one\nline two"
        };
        Dispatcher.UIThread.RunJobs();

        Assert.False(pane.IsPreviewImageShown);
        Assert.True(pane.IsPreviewTextShown);
        Assert.False(pane.IsStateMessageShown);
        Assert.Equal("line one\nline two", Text(pane).Text);
    }

    [AvaloniaTheory]
    [InlineData(MediaPreviewState.Unavailable)]
    [InlineData(MediaPreviewState.Unsupported)]
    [InlineData(MediaPreviewState.Failed)]
    [InlineData(MediaPreviewState.MissingArtifact)]
    [InlineData(MediaPreviewState.TooLarge)]
    public void FailureStates_ShowTheExplanationAndNoPayload(MediaPreviewState state)
    {
        var pane = new MediaPreviewPane
        {
            State = state,
            StateMessage = "state message for " + state
        };
        Dispatcher.UIThread.RunJobs();

        Assert.False(pane.IsBusy);
        Assert.True(pane.IsStateMessageShown);
        Assert.False(pane.IsPreviewImageShown);
        Assert.False(pane.IsPreviewTextShown);
        Assert.Equal("state message for " + state, Message(pane).Text);
    }

    [AvaloniaFact]
    public void StalePayload_StaysVisibleSoBrowsingWorksOffline()
    {
        var pane = new MediaPreviewPane
        {
            State = MediaPreviewState.Stale,
            PreviewImage = CreateImage()
        };
        Dispatcher.UIThread.RunJobs();

        Assert.True(pane.IsPreviewImageShown);
        Assert.False(pane.IsStateMessageShown);
    }

    [AvaloniaFact]
    public void Fields_AreListedInAVirtualizedPanel()
    {
        var pane = new MediaPreviewPane
        {
            State = MediaPreviewState.Ready,
            Fields = new[]
            {
                new MediaPreviewFieldRow("Comments", "captured comment", null, false, false),
                new MediaPreviewFieldRow("Bitrate", "320 kbps", "Out of date", true, false)
            }
        };
        Dispatcher.UIThread.RunJobs();

        Assert.True(pane.AreFieldsShown);
        var list = FieldsList(pane);
        Assert.True(list.IsVisible);
        Assert.IsType<VirtualizingStackPanel>(list.ItemsPanel.Build());
        Assert.Equal(2, list.ItemsSource!.Cast<object>().Count());
    }

    [AvaloniaFact]
    public void NoFields_HidesTheFieldsSection()
    {
        var pane = new MediaPreviewPane
        {
            State = MediaPreviewState.Ready,
            Fields = System.Array.Empty<MediaPreviewFieldRow>()
        };
        Dispatcher.UIThread.RunJobs();

        Assert.False(pane.AreFieldsShown);
        Assert.False(FieldsSection(pane).IsVisible);
    }

    [AvaloniaFact]
    public void FieldRow_ExposesTheHintOnlyWhenPresent()
    {
        Assert.False(new MediaPreviewFieldRow("Key", "Value", null, false, false).HasHint);
        Assert.False(new MediaPreviewFieldRow("Key", "Value", string.Empty, false, false).HasHint);
        Assert.True(new MediaPreviewFieldRow("Key", "Value", "Out of date", true, false).HasHint);
    }

    [AvaloniaFact]
    public void HeaderAndFieldList_CarryLocalizedAutomationNames()
    {
        var pane = new MediaPreviewPane { State = MediaPreviewState.Ready };
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(LocalizationService.Current.Get(PreviewKeys.EntryNameAutomation),
            AutomationProperties.GetName(pane.FindControl<TextBlock>("EntryNameText")!));
        Assert.Equal(LocalizationService.Current.Get(PreviewKeys.StateAutomation),
            AutomationProperties.GetName(pane.FindControl<TextBlock>("StateText")!));
        Assert.Equal(LocalizationService.Current.Get(PreviewKeys.FieldsAutomation),
            AutomationProperties.GetName(FieldsList(pane)));
    }

    private static ListBox FieldsList(MediaPreviewPane pane) => pane.FindControl<ListBox>("FieldsList")!;

    private static Control FieldsSection(MediaPreviewPane pane) => pane.FindControl<Control>("FieldsSection")!;

    private static Image Image(MediaPreviewPane pane) => pane.FindControl<Image>("PreviewImageControl")!;

    private static TextBox Text(MediaPreviewPane pane) => pane.FindControl<TextBox>("PreviewTextControl")!;

    private static ProgressBar Busy(MediaPreviewPane pane) => pane.FindControl<ProgressBar>("LoadingIndicator")!;

    private static TextBlock Message(MediaPreviewPane pane) => pane.FindControl<TextBlock>("StateMessageText")!;

    private static IImage CreateImage() =>
        new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
}
