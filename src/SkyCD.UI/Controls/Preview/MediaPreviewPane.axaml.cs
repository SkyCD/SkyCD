using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace SkyCD.UI.Controls.Preview;

/// <summary>
/// Generic details and preview surface for the catalog browser. It renders whatever the host resolved
/// from stored extraction results, so the same control shows posters, covers, document pages and bounded
/// text without knowing anything about the media type that produced them.
/// </summary>
public partial class MediaPreviewPane : UserControl
{
    public static readonly StyledProperty<MediaPreviewState> StateProperty =
        AvaloniaProperty.Register<MediaPreviewPane, MediaPreviewState>(nameof(State));

    public static readonly StyledProperty<string?> EntryNameProperty =
        AvaloniaProperty.Register<MediaPreviewPane, string?>(nameof(EntryName));

    /// <summary>Short availability label shown in the header, for example "Cached" or "Out of date".</summary>
    public static readonly StyledProperty<string?> StatusTextProperty =
        AvaloniaProperty.Register<MediaPreviewPane, string?>(nameof(StatusText));

    /// <summary>Explanation shown in place of a preview when there is nothing to display.</summary>
    public static readonly StyledProperty<string?> StateMessageProperty =
        AvaloniaProperty.Register<MediaPreviewPane, string?>(nameof(StateMessage));

    public static readonly StyledProperty<IImage?> PreviewImageProperty =
        AvaloniaProperty.Register<MediaPreviewPane, IImage?>(nameof(PreviewImage));

    public static readonly StyledProperty<string?> PreviewTextProperty =
        AvaloniaProperty.Register<MediaPreviewPane, string?>(nameof(PreviewText));

    public static readonly StyledProperty<IEnumerable?> FieldsProperty =
        AvaloniaProperty.Register<MediaPreviewPane, IEnumerable?>(nameof(Fields));

    /// <summary>Screen-reader name of the preview image, including the entry it belongs to.</summary>
    public static readonly StyledProperty<string?> PreviewAutomationNameProperty =
        AvaloniaProperty.Register<MediaPreviewPane, string?>(nameof(PreviewAutomationName));

    private readonly Image? previewImageControl;
    private readonly TextBox? previewTextControl;
    private readonly ProgressBar? loadingIndicator;
    private readonly TextBlock? stateMessage;
    private readonly Control? fieldsSection;

    public MediaPreviewPane()
    {
        AvaloniaXamlLoader.Load(this);

        previewImageControl = this.FindControl<Image>("PreviewImageControl");
        previewTextControl = this.FindControl<TextBox>("PreviewTextControl");
        loadingIndicator = this.FindControl<ProgressBar>("LoadingIndicator");
        stateMessage = this.FindControl<TextBlock>("StateMessageText");
        fieldsSection = this.FindControl<Control>("FieldsSection");

        UpdateVisualState();
    }

    public MediaPreviewState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string? EntryName
    {
        get => GetValue(EntryNameProperty);
        set => SetValue(EntryNameProperty, value);
    }

    public string? StatusText
    {
        get => GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public string? StateMessage
    {
        get => GetValue(StateMessageProperty);
        set => SetValue(StateMessageProperty, value);
    }

    public IImage? PreviewImage
    {
        get => GetValue(PreviewImageProperty);
        set => SetValue(PreviewImageProperty, value);
    }

    public string? PreviewText
    {
        get => GetValue(PreviewTextProperty);
        set => SetValue(PreviewTextProperty, value);
    }

    public IEnumerable? Fields
    {
        get => GetValue(FieldsProperty);
        set => SetValue(FieldsProperty, value);
    }

    public string? PreviewAutomationName
    {
        get => GetValue(PreviewAutomationNameProperty);
        set => SetValue(PreviewAutomationNameProperty, value);
    }

    /// <summary>True while stored results are still being read or decoded.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>True when a decoded image is being shown.</summary>
    public bool IsPreviewImageShown { get; private set; }

    /// <summary>True when a bounded text preview is being shown.</summary>
    public bool IsPreviewTextShown { get; private set; }

    /// <summary>True when the explanatory message is shown instead of a preview.</summary>
    public bool IsStateMessageShown { get; private set; }

    /// <summary>True when the typed-field list is shown.</summary>
    public bool AreFieldsShown { get; private set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty ||
            change.Property == PreviewImageProperty ||
            change.Property == PreviewTextProperty ||
            change.Property == FieldsProperty ||
            change.Property == StateMessageProperty)
        {
            UpdateVisualState();
        }
    }

    private void UpdateVisualState()
    {
        if (previewImageControl is null ||
            previewTextControl is null ||
            loadingIndicator is null ||
            stateMessage is null ||
            fieldsSection is null)
        {
            return;
        }

        var hasPayload = State is MediaPreviewState.Ready or MediaPreviewState.Stale;
        IsPreviewImageShown = hasPayload && PreviewImage is not null;
        IsPreviewTextShown = hasPayload && !IsPreviewImageShown && !string.IsNullOrEmpty(PreviewText);
        IsBusy = State == MediaPreviewState.Loading;
        IsStateMessageShown = !IsBusy && !IsPreviewImageShown && !IsPreviewTextShown;

        loadingIndicator.IsVisible = IsBusy;
        previewImageControl.IsVisible = IsPreviewImageShown;
        previewTextControl.IsVisible = IsPreviewTextShown;
        stateMessage.IsVisible = IsStateMessageShown;

        AreFieldsShown = Fields is not null && HasAnyField(Fields);
        fieldsSection.IsVisible = AreFieldsShown;
    }

    private static bool HasAnyField(IEnumerable fields) => fields.GetEnumerator().MoveNext();
}
