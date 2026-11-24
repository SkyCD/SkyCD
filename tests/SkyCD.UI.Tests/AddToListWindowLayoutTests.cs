using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Issues #823 and #826: the Add To List dialog keeps one fixed size while the source type is
/// switched. The source content flexes; the OK/Cancel row and the validation row stay anchored,
/// and the bordered source column stretches to the bottom of the content area.
/// </summary>
public sealed class AddToListWindowLayoutTests
{
    private static readonly AddToListSourceMode[] SourceModes =
    [
        AddToListSourceMode.Media,
        AddToListSourceMode.Folder,
        AddToListSourceMode.Internet,
        AddToListSourceMode.TitleOnly,
    ];

    private const double WindowWidth = 445.0;
    private const double WindowHeight = 480.0;

    // Width available to the right-hand column after the 8 px margin, the 89 px source column and the 8 px gap.
    private const double ContentWidth = WindowWidth - 16 - 89 - 8;

    private sealed class FakeDriveProvider : IMediaDriveProvider
    {
        public event EventHandler? DrivesChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<MediaDriveInfo> GetSupportedDrives() =>
        [
            new MediaDriveInfo(
                "D:",
                "DATA",
                "D:\\",
                DriveType.Fixed,
                IsReady: true,
                AvailableFreeSpace: 1024,
                TotalFreeSpace: 2048,
                TotalSize: 4096),
        ];

        public MediaDriveInfo? GetDrive(string drivePathOrName) => GetSupportedDrives()[0];
    }

    [AvaloniaFact]
    public void Window_UsesFixedSizeWithoutSizeToContent()
    {
        using var fixture = ShowWindow(AddToListSourceMode.Media);
        var window = fixture.Window;

        Assert.Equal(SizeToContent.Manual, window.SizeToContent);
        Assert.False(window.CanResize);
        Assert.Equal(WindowWidth, window.Width);
        Assert.Equal(WindowHeight, window.Height);
        Assert.Equal(WindowWidth, window.Bounds.Width);
        Assert.Equal(WindowHeight, window.Bounds.Height);
    }

    [AvaloniaFact]
    public void FixedHeight_FitsEverySourceLayoutWithSlack()
    {
        var naturalHeights = new Dictionary<AddToListSourceMode, double>();
        foreach (var mode in SourceModes)
        {
            naturalHeights[mode] = MeasureNaturalHeight(mode);
        }

        foreach (var (mode, height) in naturalHeights)
        {
            Assert.True(height <= WindowHeight,
                $"{mode}: natural content height {height} does not fit the fixed {WindowHeight} px window.");
        }

        Assert.Equal(naturalHeights.Values.Max(), naturalHeights[AddToListSourceMode.Internet]);
        Assert.True(WindowHeight - naturalHeights[AddToListSourceMode.Internet] >= 20,
            $"From Internet leaves only {WindowHeight - naturalHeights[AddToListSourceMode.Internet]} px of slack.");
    }

    [AvaloniaFact]
    public void SwitchingSources_KeepsSizeAndAnchorsBottomControls()
    {
        using var fixture = ShowWindow(AddToListSourceMode.Media);
        var window = fixture.Window;
        var initialBounds = window.Bounds;

        var sourceHost = fixture.Window.FindControl<Grid>("SourceDetailsPanel")!;
        var sourceViewer = fixture.Window.FindControl<ScrollViewer>("SourceFieldsScrollViewer")!;
        var contentColumn = fixture.Window.FindControl<Grid>("ContentColumn")!;
        var ok = FindButton(fixture.Root, UiKeys.Ok);
        var cancel = FindButton(fixture.Root, UiKeys.Cancel);
        var message = fixture.Window.FindControl<TextBlock>("ValidationMessageText")!;
        foreach (var mode in SourceModes.Concat(SourceModes))
        {
            fixture.ViewModel.SelectSourceCommand.Execute(mode.ToString());
            Dispatcher.UIThread.RunJobs();

            var visibleContent = sourceHost.Children.OfType<Control>().Where(control => control.IsVisible).ToList();
            var okBounds = BoundsRelativeTo(ok, fixture.Root);
            var cancelBounds = BoundsRelativeTo(cancel, fixture.Root);
            var messageBounds = BoundsRelativeTo(message, fixture.Root);
            // Issue #942: the source fields scroll inside their viewport, so the viewport is what has
            // to stay between the name block and the actions.
            var sourceBounds = BoundsRelativeTo(sourceViewer, fixture.Root);
            var columnBounds = BoundsRelativeTo(contentColumn, fixture.Root);

            // Switching only swaps content: the window neither moves nor resizes, and the content fits.
            Assert.Equal(initialBounds, window.Bounds);
            Assert.NotEmpty(visibleContent);

            // Everything remains inside the window content area.
            Assert.True(messageBounds.Bottom <= fixture.Root.Bounds.Height + 1,
                $"{mode}: validation row is clipped at {messageBounds.Bottom}.");
            Assert.True(okBounds.Bottom <= fixture.Root.Bounds.Height + 1,
                $"{mode}: OK row is outside the content at {okBounds.Bottom}.");
            Assert.True(cancelBounds.Bottom <= fixture.Root.Bounds.Height + 1,
                $"{mode}: Cancel row is outside the content at {cancelBounds.Bottom}.");
            Assert.True(sourceBounds.Bottom <= okBounds.Top,
                $"{mode}: source content overlaps the actions ({sourceBounds.Bottom} > {okBounds.Top}).");

            // The validation row keeps its slot below the actions and ends at the content bottom.
            Assert.True(message.Bounds.Height >= 14, $"{mode}: validation row collapsed to {message.Bounds.Height}.");
            Assert.True(messageBounds.Top >= okBounds.Bottom, $"{mode}: validation row overlaps the actions.");
            Assert.Equal(fixture.Root.Bounds.Height, messageBounds.Bottom, 1);
            Assert.Equal(columnBounds.Bottom - 2, okBounds.Bottom, 1);
            Assert.Equal(columnBounds.Bottom - 2, cancelBounds.Bottom, 1);
            Assert.Equal(columnBounds.Bottom, messageBounds.Top, 1);
        }
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("lt")]
    public void SourcePanel_StretchesFromContentTopToContentBottom(string cultureName)
    {
        var previousCulture = LocalizationService.Current.CurrentCulture;
        try
        {
            LocalizationService.Current.SetCulture(CultureInfo.GetCultureInfo(cultureName));
            using var fixture = ShowWindow(AddToListSourceMode.Media);
            var panel = fixture.Window.FindControl<Border>("SourcePanel")!;
            var contentColumn = fixture.Window.FindControl<Grid>("ContentColumn")!;

            var panelBounds = BoundsRelativeTo(panel, fixture.Root);
            var columnBounds = BoundsRelativeTo(contentColumn, fixture.Root);

            Assert.Equal(0, panelBounds.Top, 1);
            Assert.Equal(columnBounds.Bottom, panelBounds.Bottom, 1);
            Assert.True(panelBounds.Height > 4 * 68,
                $"{cultureName}: source panel height {panelBounds.Height} does not extend past its buttons.");
        }
        finally
        {
            LocalizationService.Current.SetCulture(previousCulture);
        }
    }

    [AvaloniaFact]
    public void SourceButtons_KeepNaturalSizeAtTopOfStretchedPanel()
    {
        using var fixture = ShowWindow(AddToListSourceMode.Media);
        var panel = fixture.Window.FindControl<Border>("SourcePanel")!;
        var panelBounds = BoundsRelativeTo(panel, fixture.Root);
        var buttons = fixture.Root.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("source"))
            .ToList();

        Assert.Equal(4, buttons.Count);
        var previousBottom = panelBounds.Top;
        foreach (var button in buttons)
        {
            var buttonBounds = BoundsRelativeTo(button, fixture.Root);
            Assert.Equal(68, buttonBounds.Height, 1);
            Assert.Equal(panelBounds.Left + 3, buttonBounds.Left, 1);
            Assert.Equal(panelBounds.Right - 3, buttonBounds.Right, 1);
            Assert.True(buttonBounds.Top >= previousBottom - 0.5,
                "source buttons must stay stacked top to bottom.");
            previousBottom = buttonBounds.Bottom;
        }

        // The extra height is absorbed by the panel background below the buttons.
        Assert.True(panelBounds.Bottom - previousBottom >= 50,
            $"only {panelBounds.Bottom - previousBottom} px of background left below the source buttons.");
    }

    [AvaloniaFact]
    public void SwitchingSources_KeepsSourcePanelStretchedToTheBottom()
    {
        using var fixture = ShowWindow(AddToListSourceMode.Media);
        var viewModel = fixture.ViewModel;
        // Keep every source valid so the validation row cannot change the content height.
        viewModel.MediaName = "Music";
        viewModel.SourceValue = @"C:\Music";
        viewModel.InternetHost = "example.com";
        viewModel.InternetPath = "/";
        viewModel.InternetCredentials.UseAnonymous = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Null(viewModel.ValidationMessage);

        var panel = fixture.Window.FindControl<Border>("SourcePanel")!;
        var contentColumn = fixture.Window.FindControl<Grid>("ContentColumn")!;
        var initialBounds = BoundsRelativeTo(panel, fixture.Root);

        foreach (var mode in SourceModes)
        {
            viewModel.SelectSourceCommand.Execute(mode.ToString());
            Dispatcher.UIThread.RunJobs();

            var panelBounds = BoundsRelativeTo(panel, fixture.Root);
            var columnBounds = BoundsRelativeTo(contentColumn, fixture.Root);

            Assert.True(Math.Abs(initialBounds.Top - panelBounds.Top) <= 1,
                $"{mode}: panel top moved from {initialBounds.Top} to {panelBounds.Top}.");
            Assert.True(Math.Abs(initialBounds.Height - panelBounds.Height) <= 1,
                $"{mode}: panel height changed from {initialBounds.Height} to {panelBounds.Height}.");
            Assert.True(Math.Abs(columnBounds.Bottom - panelBounds.Bottom) <= 1,
                $"{mode}: panel bottom {panelBounds.Bottom} does not match the content bottom {columnBounds.Bottom}.");
        }
    }

    [AvaloniaTheory]
    [InlineData(AddToListSourceMode.Media)]
    [InlineData(AddToListSourceMode.Folder)]
    [InlineData(AddToListSourceMode.Internet)]
    [InlineData(AddToListSourceMode.TitleOnly)]
    public void ValidationRow_SpansBothColumnsBelowTheStretchedPanel(AddToListSourceMode mode)
    {
        using var fixture = ShowWindow(mode);
        var panel = fixture.Window.FindControl<Border>("SourcePanel")!;
        var message = fixture.Window.FindControl<TextBlock>("ValidationMessageText")!;

        var viewer = fixture.Window.FindControl<ScrollViewer>("SourceFieldsScrollViewer")!;

        var panelBounds = BoundsRelativeTo(panel, fixture.Root);
        var sourceBounds = BoundsRelativeTo(viewer, fixture.Root);
        var messageBounds = BoundsRelativeTo(message, fixture.Root);

        Assert.Equal(0, messageBounds.Left, 1);
        Assert.Equal(fixture.Root.Bounds.Width, messageBounds.Right, 1);
        Assert.Equal(panelBounds.Bottom, messageBounds.Top, 1);
        Assert.True(messageBounds.Top >= sourceBounds.Bottom,
            "the validation row must stay below the right-hand source content.");
        Assert.Equal(fixture.Root.Bounds.Height, messageBounds.Bottom, 1);
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("lt")]
    public void TallestContent_LeavesRoomForLongLocalizedText(string cultureName)
    {
        var previousCulture = LocalizationService.Current.CurrentCulture;
        try
        {
            LocalizationService.Current.SetCulture(CultureInfo.GetCultureInfo(cultureName));
            var internetNatural = MeasureNaturalHeight(AddToListSourceMode.Internet);
            var slack = WindowHeight - internetNatural;

            // Text that may grow when it wraps: the longest source hint or validation message plus an
            // extra line of growth. The tallest layout must keep room for that inside the fixed height.
            var longestHint = LongestOf(UiKeys.TitleOnlyMediaHint, TextKeys.MediaNameIsRequiredForMediaSource, cultureName);
            var extraLines = Math.Max(
                MeasureWrappedText(longestHint) - 24,
                MeasureWrappedText("A validation message that wraps onto three lines at this width.") - 24);

            Assert.True(slack >= extraLines - 1,
                $"{cultureName}: {slack} px of slack cannot host {extraLines} px of extra wrapped text.");
        }
        finally
        {
            LocalizationService.Current.SetCulture(previousCulture);
        }
    }

    [AvaloniaTheory]
    [InlineData(AddToListSourceMode.Media)]
    [InlineData(AddToListSourceMode.Folder)]
    [InlineData(AddToListSourceMode.Internet)]
    [InlineData(AddToListSourceMode.TitleOnly)]
    public void SourceContent_StaysBetweenTheLocationPickerAndTheActionBar(AddToListSourceMode mode)
    {
        // Issue #942: the source panel used to sit in a fixed 6 px row and drew over the name/Location block.
        using var fixture = ShowWindow(mode);
        var sourceHost = fixture.Window.FindControl<Grid>("SourceDetailsPanel")!;
        var nameBlock = fixture.Window.FindControl<StackPanel>("NameLocationBlock")!;
        var viewer = fixture.Window.FindControl<ScrollViewer>("SourceFieldsScrollViewer")!;
        var visibleContent = sourceHost.Children.OfType<Control>().Single(control => control.IsVisible);
        var message = fixture.Window.FindControl<TextBlock>("ValidationMessageText")!;
        var ok = FindButton(fixture.Root, UiKeys.Ok);

        var contentBounds = BoundsRelativeTo(visibleContent, fixture.Root);
        var nameBounds = BoundsRelativeTo(nameBlock, fixture.Root);
        var viewerBounds = BoundsRelativeTo(viewer, fixture.Root);
        var messageBounds = BoundsRelativeTo(message, fixture.Root);
        var okBounds = BoundsRelativeTo(ok, fixture.Root);

        // The name/Location block keeps the top of the form; the source fields start below it and
        // scroll inside their viewport instead of drawing over the block or the actions.
        Assert.True(contentBounds.Top >= nameBounds.Bottom - 0.5,
            $"{mode}: source content starts above the name/Location block ({contentBounds.Top} < {nameBounds.Bottom}).");
        Assert.True(viewerBounds.Bottom <= okBounds.Top + 0.5,
            $"{mode}: source content overlaps the action bar ({viewerBounds.Bottom} > {okBounds.Top}).");
        Assert.True(okBounds.Bottom <= messageBounds.Top + 0.5,
            $"{mode}: the validation line overlaps the action bar.");
        Assert.True(messageBounds.Bottom <= fixture.Root.Bounds.Height + 1,
            $"{mode}: validation line is clipped at {messageBounds.Bottom}.");
        Assert.True(message.Bounds.Height >= 14, $"{mode}: validation line collapsed to {message.Bounds.Height}.");
    }

    private static double MeasureNaturalHeight(AddToListSourceMode mode)
    {
        var viewModel = new AddToListDialogViewModel(new FakeDriveProvider(), mode);
        var window = new AddToListWindow
        {
            DataContext = viewModel,
            Height = double.NaN,
            SizeToContent = SizeToContent.Height,
        };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window.Bounds.Height;
        }
        finally
        {
            window.Close();
            viewModel.Dispose();
        }
    }

    private static AddToListFixture ShowWindow(AddToListSourceMode initialMode)
    {
        var viewModel = new AddToListDialogViewModel(new FakeDriveProvider(), initialMode);
        var window = new AddToListWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return new AddToListFixture(window, viewModel, (Control)window.Content!);
    }

    private static Button FindButton(Visual root, string key)
    {
        var label = LocalizationService.Current.Get(key);
        return root.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, label));
    }

    private static Rect BoundsRelativeTo(Visual visual, Visual ancestor)
    {
        var origin = visual.TranslatePoint(default, ancestor)!.Value;
        return new Rect(origin, visual.Bounds.Size);
    }

    private static string LongestOf(string firstKey, string secondKey, string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var first = LocalizationService.Current.Get(firstKey, culture);
        var second = LocalizationService.Current.Get(secondKey, culture);
        return first.Length >= second.Length ? first : second;
    }

    private static double MeasureWrappedText(string text)
    {
        var layout = new TextLayout(
            text,
            new Typeface("Segoe UI"),
            11,
            Brushes.Black,
            TextAlignment.Left,
            TextWrapping.Wrap,
            TextTrimming.None,
            flowDirection: FlowDirection.LeftToRight,
            maxWidth: ContentWidth,
            maxHeight: double.PositiveInfinity);
        return layout.Height;
    }

    private sealed class AddToListFixture(AddToListWindow window, AddToListDialogViewModel viewModel, Control root) : IDisposable
    {
        public AddToListWindow Window { get; } = window;

        public AddToListDialogViewModel ViewModel { get; } = viewModel;

        public Control Root { get; } = root;

        public void Dispose()
        {
            Window.Close();
            ViewModel.Dispose();
        }
    }
}
