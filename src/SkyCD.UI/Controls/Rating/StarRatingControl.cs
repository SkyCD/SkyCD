using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using SkyCD.UI.Localization;

namespace SkyCD.UI.Controls.Rating;

/// <summary>
/// Editable five-star rating. The value is kept in the range 0&#8211;5, where 0 means unrated, and a two-way
/// binding writes it back as soon as the user picks a star, so the view model owns the draft and the dialog
/// decides when it is persisted.
/// </summary>
/// <remarks>
/// Keyboard: Left/Down decrease by one, Right/Up increase by one, Home (or Backspace/Delete) clears, End
/// selects the maximum, and Space/Enter cycle through the stars and back to unrated. Pointer: clicking a
/// star selects it and clicking the star that equals the current rating clears the rating; hovering
/// previews the value under the pointer. Assistive technology reads the current value, the selected star
/// count and the rating label, and can set a value through the range value automation pattern.
/// </remarks>
public class StarRatingControl : Control, IRangeValueProvider
{
    /// <summary>Highest rating a user can select.</summary>
    public const int MaximumValue = 5;

    /// <summary>Outline of one star on a 24&#160;x&#160;24 grid used for vector rendering.</summary>
    public const string StarGeometryData = "M12,1 L14.94,8.25 L22.5,8.6 L16.65,13.45 L18.45,21 L12,16.8 L5.55,21 L7.35,13.45 L1.5,8.6 L9.06,8.25 Z";

    private const double Spacing = 2;
    private const double DefaultStarSize = 18;

    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<StarRatingControl, int>(
            nameof(Value),
            defaultValue: 0,
            defaultBindingMode: BindingMode.TwoWay,
            coerce: static (_, value) => Math.Clamp(value, 0, MaximumValue));

    /// <summary>Label announced for the control by assistive technology, for example "Rating".</summary>
    public static readonly StyledProperty<string> RatingNameProperty =
        AvaloniaProperty.Register<StarRatingControl, string>(
            nameof(RatingName),
            defaultValue: LocalizationService.Current.Get(UiKeys.RatingName));

    public static readonly StyledProperty<double> StarSizeProperty =
        AvaloniaProperty.Register<StarRatingControl, double>(nameof(StarSize), DefaultStarSize);

    /// <summary>Raised after a keyboard, pointer, clear-button or automation change to <see cref="Value"/>.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> ValueSubmittedEvent =
        RoutedEvent.Register<StarRatingControl, RoutedEventArgs>(nameof(ValueSubmitted), RoutingStrategies.Bubble);

    private readonly StarRatingCell[] cells = new StarRatingCell[MaximumValue];
    private int hoveredStar;

    static StarRatingControl()
    {
        FocusableProperty.OverrideDefaultValue<StarRatingControl>(true);
    }

    public StarRatingControl()
    {
        for (var index = 1; index <= MaximumValue; index++)
        {
            var cell = new StarRatingCell(this, index);
            cells[index - 1] = cell;
            VisualChildren.Add(cell);
            LogicalChildren.Add(cell);
        }

        UpdateAccessibility();
    }

    /// <summary>Raised after a keyboard, pointer, clear-button or automation change to <see cref="Value"/>.</summary>
    public event EventHandler<RoutedEventArgs>? ValueSubmitted
    {
        add => AddHandler(ValueSubmittedEvent, value);
        remove => RemoveHandler(ValueSubmittedEvent, value);
    }

    public int Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Label announced for the control, for example "Rating".</summary>
    public string RatingName
    {
        get => (string?)GetValue(RatingNameProperty) ?? string.Empty;
        set => SetValue(RatingNameProperty, value ?? string.Empty);
    }

    /// <summary>Side of a single star in device-independent pixels.</summary>
    public double StarSize
    {
        get => GetValue(StarSizeProperty);
        set => SetValue(StarSizeProperty, value);
    }

    /// <summary>Number of stars drawn filled right now, including any pointer hover preview.</summary>
    public int FillLevel => Math.Max(Value, hoveredStar);

    /// <summary>The five star cells, ordered left to right.</summary>
    public IReadOnlyList<StarRatingCell> Stars => cells;

    bool IRangeValueProvider.IsReadOnly => !IsEffectivelyEnabled;

    double IRangeValueProvider.Value => Value;

    double IRangeValueProvider.Minimum => 0;

    double IRangeValueProvider.Maximum => MaximumValue;

    double IRangeValueProvider.SmallChange => 1;

    double IRangeValueProvider.LargeChange => 1;

    /// <summary>Text describing a value, for example "3 of 5", or the unrated text for 0.</summary>
    public static string GetValueText(int value) =>
        value <= 0
            ? LocalizationService.Current.Get(UiKeys.RatingNotSet)
            : LocalizationService.Current.Format(UiKeys.RatingValue, value, MaximumValue);

    /// <summary>Accessible name of the star at <paramref name="starIndex"/> (1-based).</summary>
    public static string GetStarText(int starIndex) => LocalizationService.Current.Format(UiKeys.RatingStar, starIndex);

    /// <summary>
    /// Returns the value produced by <paramref name="key"/>, or <see langword="null"/> when the key is not a
    /// rating command. Pure, so the keyboard map is testable without a window.
    /// </summary>
    public static int? GetValueForKey(int currentValue, Key key, KeyModifiers modifiers)
    {
        if ((modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != KeyModifiers.None)
        {
            return null;
        }

        var current = Math.Clamp(currentValue, 0, MaximumValue);
        return key switch
        {
            Key.Left or Key.Down => Math.Max(0, current - 1),
            Key.Right or Key.Up => Math.Min(MaximumValue, current + 1),
            Key.Home or Key.Back or Key.Delete => 0,
            Key.End => MaximumValue,
            Key.Space or Key.Enter => current >= MaximumValue ? 0 : current + 1,
            _ => null
        };
    }

    /// <summary>Value produced by clicking <paramref name="starIndex"/> (1-based); clicking the selected star clears.</summary>
    public static int GetValueForStarClick(int currentValue, int starIndex)
    {
        var current = Math.Clamp(currentValue, 0, MaximumValue);
        return starIndex == current ? 0 : Math.Clamp(starIndex, 0, MaximumValue);
    }

    /// <summary>Builds the vector outline of one star inside <paramref name="bounds"/>.</summary>
    public static Geometry CreateStarGeometry(Rect bounds)
    {
        var scale = Math.Min(bounds.Width, bounds.Height) / 24d;
        var offsetX = bounds.X + ((bounds.Width - (24 * scale)) / 2);
        var offsetY = bounds.Y + ((bounds.Height - (24 * scale)) / 2);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            // StarGeometryData is SVG path syntax: each whitespace-separated token starts with a
            // command letter followed by a comma-separated coordinate pair ("M12,1", "L14.94,8.25",
            // "Z"). A leading letter is the command for the coordinates that follow it; a token
            // without one continues the previous command.
            var figureOpen = false;
            var command = 'M';
            foreach (var token in StarGeometryData.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var coordinates = token;
                if (coordinates.Length > 0 && char.IsAsciiLetter(coordinates[0]))
                {
                    command = coordinates[0];
                    coordinates = coordinates.Substring(1);
                    if ((command == 'Z' || command == 'z') && figureOpen)
                    {
                        context.EndFigure(true);
                        figureOpen = false;
                    }
                }

                if (coordinates.Length == 0)
                {
                    continue;
                }

                var pair = coordinates.Split(',', StringSplitOptions.RemoveEmptyEntries);
                for (var index = 0; index + 1 < pair.Length; index += 2)
                {
                    var point = new Point(
                        offsetX + (double.Parse(pair[index], CultureInfo.InvariantCulture) * scale),
                        offsetY + (double.Parse(pair[index + 1], CultureInfo.InvariantCulture) * scale));
                    if (!figureOpen)
                    {
                        context.BeginFigure(point, isFilled: true);
                        figureOpen = true;
                    }
                    else
                    {
                        context.LineTo(point);
                    }
                }
            }

            if (figureOpen)
            {
                context.EndFigure(true);
            }
        }

        return geometry;
    }

    /// <summary>Applies a keyboard command and reports whether it was a rating command.</summary>
    public bool ApplyKey(Key key, KeyModifiers modifiers)
    {
        if (!IsEffectivelyEnabled || GetValueForKey(Value, key, modifiers) is not { } next)
        {
            return false;
        }

        ApplyValue(next);
        return true;
    }

    /// <summary>Applies a click on <paramref name="starIndex"/> (1-based) and reports whether it was applied.</summary>
    public bool ApplyStarClick(int starIndex)
    {
        if (!IsEffectivelyEnabled || starIndex is < 1 or > MaximumValue)
        {
            return false;
        }

        ApplyValue(GetValueForStarClick(Value, starIndex));
        return true;
    }

    /// <summary>Previews <paramref name="starIndex"/> (1-based) on hover; 0 ends the preview.</summary>
    public void ApplyHover(int starIndex)
    {
        var next = IsEffectivelyEnabled ? Math.Clamp(starIndex, 0, MaximumValue) : 0;
        if (next == hoveredStar)
        {
            return;
        }

        hoveredStar = next;
        foreach (var cell in cells)
        {
            cell.InvalidateVisual();
        }
    }

    /// <summary>Sets the value and announces the change, exactly as keyboard, pointer and the automation peer do.</summary>
    public void ApplyValue(int value)
    {
        var next = Math.Clamp(value, 0, MaximumValue);
        // SetCurrentValue keeps a value binding intact, and because the property is TwoWay the
        // binding pushes the new value into the dialog draft immediately.
        SetCurrentValue(ValueProperty, next);
        if (Value == next)
        {
            RaiseEvent(new RoutedEventArgs(ValueSubmittedEvent, this));
        }
    }

    void IRangeValueProvider.SetValue(double value)
    {
        if (!IsEffectivelyEnabled)
        {
            throw new InvalidOperationException(
                "The star rating control is disabled and cannot accept a new value.");
        }

        ApplyValue((int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, MaximumValue));
    }

    /// <summary>Hit-tests <paramref name="point"/> (control coordinates) to a star; returns 0 outside the stars.</summary>
    public int GetStarAt(Point point)
    {
        foreach (var cell in cells)
        {
            var bounds = cell.Bounds;
            if (point.X >= bounds.X && point.X < bounds.Right && point.Y >= bounds.Y && point.Y < bounds.Bottom)
            {
                return cell.StarIndex;
            }
        }

        return 0;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new StarRatingAutomationPeer(this);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && ApplyKey(e.Key, e.KeyModifiers))
        {
            e.Handled = true;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            hoveredStar = 0;
            UpdateAccessibility();
            foreach (var cell in cells)
            {
                cell.InvalidateVisual();
            }
        }
        else if (change.Property == RatingNameProperty)
        {
            UpdateAccessibility();
        }
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new((StarSize * MaximumValue) + (Spacing * (MaximumValue - 1)), StarSize);

    protected override Size ArrangeOverride(Size finalSize)
    {
        var starSize = Math.Min(StarSize, finalSize.Height);
        for (var index = 0; index < cells.Length; index++)
        {
            cells[index].Arrange(new Rect(index * (starSize + Spacing), (finalSize.Height - starSize) / 2, starSize, starSize));
        }

        return finalSize;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var cell = GetStarAt(e.GetPosition(this));
        if (cell == 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus(NavigationMethod.Pointer);
        ApplyStarClick(cell);
        e.Handled = true;
    }

    internal void OnCellPointerPressed(StarRatingCell cell, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus(NavigationMethod.Pointer);
        ApplyStarClick(cell.StarIndex);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // Draw the transparent background that makes the whole control area hit-testable.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
    }

    private void UpdateAccessibility()
    {
        AutomationProperties.SetName(this, RatingName);
        AutomationProperties.SetHelpText(this, GetValueText(Value));
        AutomationProperties.SetAutomationId(this, "StarRating");
        for (var index = 0; index < cells.Length; index++)
        {
            AutomationProperties.SetName(cells[index], GetStarText(index + 1));
            AutomationProperties.SetHelpText(cells[index], GetValueText(index + 1));
        }
    }

    /// <summary>Brushes used by the star cells; resources let the application theme override the palette.</summary>
    internal IBrush FilledBrush => FindBrush("StarRatingFilledBrush", FilledColor);

    internal IBrush HoverBrush => FindBrush("StarRatingHoverBrush", HoverColor);

    internal IBrush EmptyBrush => FindBrush("StarRatingEmptyBrush", EmptyColor);

    internal static readonly Color FilledColor = Color.Parse("#E5B045");

    internal static readonly Color HoverColor = Color.Parse("#F3CB70");

    internal static readonly Color EmptyColor = Color.Parse("#8A8F98");

    private IBrush FindBrush(string resourceKey, Color fallback) =>
        this.FindResource(resourceKey) as IBrush ?? new SolidColorBrush(fallback);
}

/// <summary>
/// One star of a <see cref="StarRatingControl"/>. It is a real child element so pointer input, automation
/// names and visual states (filled, hover preview) work per star without a glyph font.
/// </summary>
public sealed class StarRatingCell : Control
{
    private readonly StarRatingControl owner;

    internal StarRatingCell(StarRatingControl owner, int starIndex)
    {
        this.owner = owner;
        StarIndex = starIndex;
        Name = $"PART_Star{starIndex}";
        Focusable = false;
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerEntered += (_, _) => owner.ApplyHover(StarIndex);
        PointerExited += (_, _) => owner.ApplyHover(0);
        PointerPressed += (_, e) => owner.OnCellPointerPressed(this, e);
    }

    /// <summary>1-based position of the star.</summary>
    public int StarIndex { get; }

    /// <summary>True when the star is part of the current selection or hover preview.</summary>
    public bool IsFilled => StarIndex <= owner.FillLevel;

    /// <summary>True while the pointer previews this star.</summary>
    public bool IsHovered => owner.FillLevel == StarIndex && owner.Value != StarIndex;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var brush = IsFilled
            ? (IsHovered ? owner.HoverBrush : owner.FilledBrush)
            : (owner.IsPointerOver ? owner.HoverBrush : owner.EmptyBrush);
        context.DrawGeometry(brush, null, StarRatingControl.CreateStarGeometry(new Rect(Bounds.Size)));
    }
}
