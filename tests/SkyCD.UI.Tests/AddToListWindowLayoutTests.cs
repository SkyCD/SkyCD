using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using IconPacks.Avalonia;
using SkyCD.App.Views;
using SkyCD.Presentation.Scanning;
using SkyCD.SDK.Capabilities.Capture;
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
    private static readonly CaptureSourceKind[] CaptureKinds =
    [
        new("file", "File", "Video file", UseFilePicker: true),
        new("camera", "Local device", "Camera", UseDevicePicker: true),
        new("url", "JPEG URL", "URL"),
    ];

    private static readonly CaptureDevice[] CaptureDevices =
    [
        new("0", "Integrated camera"),
    ];

    private static readonly AddToListSourceMode[] SourceModes =
    [
        AddToListSourceMode.Media,
        AddToListSourceMode.Folder,
        AddToListSourceMode.Internet,
        AddToListSourceMode.TitleOnly,
        AddToListSourceMode.FromAccount,
        AddToListSourceMode.CaptureSource,
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


    private sealed class FakeCaptureProvider : ICaptureSourcePluginCapability
    {
        public string ProviderId => "test-capture";

        public string DisplayName => "Motion detector";

        public event EventHandler? SourcesChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<CaptureSourceKind> GetSourceKinds() => CaptureKinds;

        public IReadOnlyList<CaptureDevice> GetDevices(string kindId) => CaptureDevices;

        public Task<CaptureSourceProbeResult> ProbeAsync(CaptureSourceConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CaptureSourceProbeResult.Available());

        public Task<CapturePreviewResult> CapturePreviewAsync(CaptureSourceConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CapturePreviewResult.Failed("No frame"));

        public IReadOnlyList<CaptureSourceConfiguration> GetSources() => [];

        public Task<CaptureSourceConfiguration> AddSourceAsync(CaptureSourceDraft draft,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CaptureSourceConfiguration("source", draft.KindId, draft.Location,
                draft.DestinationFolder, draft.CatalogFolder, draft.Label));

        public Task RemoveSourceAsync(string sourceId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public IReadOnlyList<CaptureDetectorDescriptor> GetDetectors() => [];

        public CaptureDetectorSettings GetDetectorSettings() => new(null, new Dictionary<string, double>());

        public Task SaveDetectorSettingsAsync(CaptureDetectorSettings settings,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
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

        var tallest = SourceModes.OrderByDescending(mode => naturalHeights[mode]).First();
        var measured = string.Join(", ", naturalHeights.Select(pair => $"{pair.Key}={pair.Value}"));
        Assert.True(naturalHeights.Values.Max() <= WindowHeight - 18,
            $"{tallest} leaves only {WindowHeight - naturalHeights.Values.Max()} px of slack ({measured}).");
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
            Assert.True(panelBounds.Height > 5 * 68,
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

        Assert.Equal(6, buttons.Count);
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
        Assert.True(panelBounds.Bottom - previousBottom >= 10,
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
            // The capture source carries its own physical destination, so the dialog destination row is
            // hidden for it and the panel follows the shorter content column.
            Assert.True(Math.Abs(columnBounds.Bottom - panelBounds.Bottom) <= 1,
                $"{mode}: panel bottom {panelBounds.Bottom} does not match the content bottom {columnBounds.Bottom}.");
        }
    }

    [AvaloniaTheory]
    [InlineData(AddToListSourceMode.Media)]
    [InlineData(AddToListSourceMode.Folder)]
    [InlineData(AddToListSourceMode.Internet)]
    [InlineData(AddToListSourceMode.TitleOnly)]
    [InlineData(AddToListSourceMode.FromAccount)]
    [InlineData(AddToListSourceMode.CaptureSource)]
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
            var tallestNatural = SourceModes.Max(MeasureNaturalHeight);
            var slack = WindowHeight - tallestNatural;

            // Six source types fill the fixed height, and the source fields scroll, so the guard is that the
            // tallest layout still leaves room for one hint line that wraps one line longer.
            var longestHint = LongestOf(UiKeys.TitleOnlyMediaHint, TextKeys.MediaNameIsRequiredForMediaSource, cultureName);
            var oneExtraLine = MeasureWrappedText(longestHint) > 24 ? 14 : 0;

            Assert.True(slack >= oneExtraLine,
                $"{cultureName}: {slack} px of slack cannot host a wrapped hint line ({oneExtraLine} px).");
        }
        finally
        {
            LocalizationService.Current.SetCulture(previousCulture);
        }
    }

    [AvaloniaFact]
    public void InternetProtocolDropdown_RowsShareTheFieldInsetAndAlignWithIt()
    {
        // Issue #1030: the popup rows inherited the theme's 2 px item padding while the closed field
        // used the ComboBox padding, so the icon column jumped between the field and the open list.
        using var fixture = ShowWindow(AddToListSourceMode.Internet);
        var picker = fixture.Window.FindControl<ComboBox>("InternetProtocolComboBox")!;
        picker.IsDropDownOpen = true;
        fixture.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.True(Application.Current!.TryGetResource("SkyComboBoxContentPadding", null, out var token));
        var padding = Assert.IsType<Thickness>(token);
        var rows = picker.GetRealizedContainers().OfType<ComboBoxItem>().ToList();
        // SMB is offered on Windows only, so the list holds five protocols elsewhere; every
        // available protocol has to be realized for the row checks below to cover all of them.
        Assert.Equal(picker.ItemCount, rows.Count);

        var closedIcon = picker.GetVisualDescendants().OfType<PackIconControl>().Single();
        var closedLabel = closedIcon.GetVisualParent()!.GetVisualChildren().OfType<TextBlock>().Single();
        var fieldPresenter = closedIcon.FindAncestorOfType<ContentControl>()!;
        var fieldColumnSpan = LabelInset(closedIcon, closedLabel, fieldPresenter);

        // The closed field insets its content by the same token the rows use.
        Assert.Equal(padding.Left, fieldPresenter.Bounds.X, 1);
        Assert.Equal(padding.Top, fieldPresenter.Bounds.Y, 1);

        double? firstLabelLeft = null;
        foreach (var row in rows)
        {
            var icon = row.GetVisualDescendants().OfType<PackIconControl>().Single();
            var label = row.GetVisualDescendants().OfType<TextBlock>().Single();
            var labelLeft = label.TranslatePoint(default, row)!.Value.X;

            // One item padding around the icon and label; no row hugs its content.
            Assert.Equal(padding, row.Padding);
            Assert.Equal(16 + padding.Top + padding.Bottom, row.Bounds.Height, 1);

            // The icon column and the label column stay put inside every row, whichever glyph or
            // label width a protocol has, and land where the closed field already draws them.
            Assert.Equal(padding.Left, IconInset(icon, row), 1);
            Assert.Equal(fieldColumnSpan, LabelInset(icon, label, row), 1);

            firstLabelLeft ??= labelLeft;
            Assert.Equal(firstLabelLeft.Value, labelLeft, 1);

            // The glyph is vertically centred inside its padded row.
            Assert.Equal((row.Bounds.Height - 16) / 2, icon.TranslatePoint(default, row)!.Value.Y, 1);

            // The longest label still fits inside the row inset.
            var labelRight = label.TranslatePoint(new Point(label.Bounds.Width, 0), row)!.Value.X;
            Assert.True(labelRight <= row.Bounds.Width - padding.Right + 0.5,
                $"{label.Text}: label ends at {labelRight} inside the {row.Bounds.Width} px row.");
        }
    }

    [AvaloniaTheory]
    [InlineData(AddToListSourceMode.Media)]
    [InlineData(AddToListSourceMode.Folder)]
    [InlineData(AddToListSourceMode.Internet)]
    [InlineData(AddToListSourceMode.TitleOnly)]
    public void NameBlock_LeavesGapAboveSourceFields(AddToListSourceMode mode)
    {
        // Issue #1189: the name/Location block used to end exactly where the source panel begins.
        using var fixture = ShowWindow(mode);
        var nameBlock = fixture.Window.FindControl<StackPanel>("NameLocationBlock")!;
        var viewer = fixture.Window.FindControl<ScrollViewer>("SourceFieldsScrollViewer")!;

        var nameBounds = BoundsRelativeTo(nameBlock, fixture.Root);
        var viewerBounds = BoundsRelativeTo(viewer, fixture.Root);

        Assert.True(viewerBounds.Top - nameBounds.Bottom >= 3,
            $"{mode}: source fields touch the name block (gap {viewerBounds.Top - nameBounds.Bottom}).");
    }

    [AvaloniaFact]
    public void InternetProtocolRow_HasLabelAndSharedFieldHeight()
    {
        // Issue #1189: the protocol row had no field label and the Paste URL button did not share
        // the 26 px field height of the ComboBox in the same row.
        using var fixture = ShowWindow(AddToListSourceMode.Internet);
        var combo = fixture.Window.FindControl<ComboBox>("InternetProtocolComboBox")!;
        var paste = fixture.Window.FindControl<Button>("PasteInternetUrlButton")!;

        Assert.Equal(26, combo.Bounds.Height, 1);
        Assert.Equal(26, paste.Bounds.Height, 1);

        var group = (StackPanel)((Grid)combo.Parent!).Parent!;
        var label = Assert.IsType<TextBlock>(group.Children[0]);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.InternetProtocol), label.Text);
    }

    private static double IconInset(PackIconControl icon, Control container) =>
        icon.TranslatePoint(default, container)!.Value.X;

    private static double LabelInset(PackIconControl icon, TextBlock label, Control container) =>
        label.TranslatePoint(default, container)!.Value.X - IconInset(icon, container);

    [AvaloniaTheory]
    [InlineData(AddToListSourceMode.Media)]
    [InlineData(AddToListSourceMode.Folder)]
    [InlineData(AddToListSourceMode.Internet)]
    [InlineData(AddToListSourceMode.TitleOnly)]
    [InlineData(AddToListSourceMode.FromAccount)]
    [InlineData(AddToListSourceMode.CaptureSource)]
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

    [AvaloniaFact]
    public void DestinationDescription_SitsInsideTheSelectedFolderOption()
    {
        // Issue #1314: the destination is now the selected-folder option's second line instead of a
        // detached caption under both radios, and it follows the captured destination name.
        using var fixture = ShowWindow(AddToListSourceMode.Media);
        var viewModel = fixture.ViewModel;
        viewModel.SetTargetParent("folder-1", "Concerts");
        Dispatcher.UIThread.RunJobs();
        fixture.Window.UpdateLayout();

        var caption = fixture.Window.FindControl<TextBlock>("TargetParentDescriptionText")!;
        var owner = fixture.Root.GetVisualDescendants().OfType<RadioButton>()
            .Single(radio => string.Equals(radio.CommandParameter as string, "SelectedFolder", StringComparison.Ordinal));

        Assert.True(caption.IsEffectivelyVisible);
        Assert.Contains("Concerts", caption.Text, StringComparison.Ordinal);
        Assert.Contains(caption, owner.GetVisualDescendants());

        var captionBounds = BoundsRelativeTo(caption, fixture.Root);
        var ownerBounds = BoundsRelativeTo(owner, fixture.Root);
        Assert.True(captionBounds.Top >= ownerBounds.Top - 0.5 && captionBounds.Bottom <= ownerBounds.Bottom + 0.5,
            $"destination caption {captionBounds} escapes its option {ownerBounds}.");
        Assert.True(ownerBounds.Bottom <= BoundsRelativeTo(FindButton(fixture.Root, UiKeys.Ok), fixture.Root).Top + 0.5,
            "the selected-folder option must end above the action row.");

        viewModel.SetTargetParent("folder-2", "Jazz");
        Dispatcher.UIThread.RunJobs();
        fixture.Window.UpdateLayout();
        Assert.Contains("Jazz", caption.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void NewMediaChoice_HidesTheDestinationWithoutLeavingALineUnderTheRadios()
    {
        // Issue #1314: choosing "As new Media" must remove the destination entirely, with no stray
        // text left between the option group and the OK/Cancel row.
        using var fixture = ShowWindow(AddToListSourceMode.Media);
        var viewModel = fixture.ViewModel;
        viewModel.SetTargetParent("folder-1", "Concerts");
        Dispatcher.UIThread.RunJobs();

        var caption = fixture.Window.FindControl<TextBlock>("TargetParentDescriptionText")!;
        Assert.True(caption.IsEffectivelyVisible);

        viewModel.SelectTargetCommand.Execute("NewMedia");
        Dispatcher.UIThread.RunJobs();
        fixture.Window.UpdateLayout();

        Assert.False(caption.IsEffectivelyVisible);

        var groupBounds = BoundsRelativeTo(fixture.Window.FindControl<StackPanel>("TargetPlacementGroup")!, fixture.Root);
        var actionBounds = BoundsRelativeTo(fixture.Window.FindControl<StackPanel>("DialogActions")!, fixture.Root);

        foreach (var text in fixture.Root.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible))
        {
            var bounds = BoundsRelativeTo(text, fixture.Root);
            Assert.False(bounds.Top >= groupBounds.Bottom - 0.5 && bounds.Bottom <= actionBounds.Top + 0.5,
                $"'{text.Text}' still floats between the radios and the actions at {bounds}.");
        }
    }

    private static double MeasureNaturalHeight(AddToListSourceMode mode)
    {
        var viewModel = new AddToListDialogViewModel(new FakeDriveProvider(), mode,
            captureProviders: [new FakeCaptureProvider()]);
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
        var viewModel = new AddToListDialogViewModel(new FakeDriveProvider(), initialMode,
            captureProviders: [new FakeCaptureProvider()]);
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
