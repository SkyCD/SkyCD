using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SkyCD.UI.Controls.Selectors.ColorPaletteDropdown;

public partial class ColorPaletteDropdown : UserControl
{
    public static readonly StyledProperty<IEnumerable<string>?> ItemsSourceProperty =
        AvaloniaProperty.Register<ColorPaletteDropdown, IEnumerable<string>?>(nameof(ItemsSource));

    public static readonly StyledProperty<string> SelectedColorProperty =
        AvaloniaProperty.Register<ColorPaletteDropdown, string>(nameof(SelectedColor), "#FFFFFF", defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly DirectProperty<ColorPaletteDropdown, IBrush> SelectedBrushProperty =
        AvaloniaProperty.RegisterDirect<ColorPaletteDropdown, IBrush>(nameof(SelectedBrush), control => control.SelectedBrush);

    private IBrush selectedBrush = Brushes.White;
    private bool isPointerDown;
    private bool isHuePointerDown;
    private bool suppressEvents;

    private double hue;
    private double saturation;
    private double value = 1;

    public ColorPaletteDropdown()
    {
        InitializeComponent();
        SvSurface.SizeChanged += (_, _) => RefreshPickerVisuals();
        HueBarSurface.SizeChanged += (_, _) => RefreshPickerVisuals();
        RefreshFromSelectedColor();
    }

    public IEnumerable<string>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string SelectedColor
    {
        get => GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public IBrush SelectedBrush
    {
        get => selectedBrush;
        private set => SetAndRaise(SelectedBrushProperty, ref selectedBrush, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedColorProperty && !suppressEvents)
        {
            RefreshFromSelectedColor();
        }
    }

    private void OnToggleButtonClick(object? sender, RoutedEventArgs e)
    {
        PalettePopup.IsOpen = !PalettePopup.IsOpen;
        if (PalettePopup.IsOpen)
        {
            Dispatcher.UIThread.Post(RefreshFromSelectedColor, DispatcherPriority.Render);
        }
    }

    private void OnHexTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (suppressEvents)
        {
            return;
        }

        var text = HexTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!text.StartsWith("#", StringComparison.Ordinal))
        {
            text = $"#{text}";
        }

        if (!Color.TryParse(text, out _))
        {
            return;
        }

        suppressEvents = true;
        SelectedColor = text.ToUpperInvariant();
        suppressEvents = false;
        RefreshFromSelectedColor();
    }

    private void OnSvSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        isPointerDown = true;
        e.Pointer.Capture(SvSurface);
        UpdateSvFromPoint(e.GetPosition(SvSurface));
    }

    private void OnSvSurfacePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!isPointerDown)
        {
            return;
        }

        UpdateSvFromPoint(e.GetPosition(SvSurface));
    }

    private void OnSvSurfacePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        isPointerDown = false;
        if (ReferenceEquals(e.Pointer.Captured, SvSurface))
        {
            e.Pointer.Capture(null);
        }
    }

    private void OnHueBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        isHuePointerDown = true;
        e.Pointer.Capture(HueBarSurface);
        UpdateHueFromPoint(e.GetPosition(HueBarSurface));
    }

    private void OnHueBarPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!isHuePointerDown)
        {
            return;
        }

        UpdateHueFromPoint(e.GetPosition(HueBarSurface));
    }

    private void OnHueBarPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        isHuePointerDown = false;
        if (ReferenceEquals(e.Pointer.Captured, HueBarSurface))
        {
            e.Pointer.Capture(null);
        }
    }

    private void UpdateSvFromPoint(Point point)
    {
        var width = Math.Max(1, SvSurface.Bounds.Width);
        var height = Math.Max(1, SvSurface.Bounds.Height);

        var x = Math.Clamp(point.X, 0, width);
        var y = Math.Clamp(point.Y, 0, height);

        saturation = x / width;
        value = 1 - (y / height);
        ApplyHsvToSelectedColor();
    }

    private void UpdateHueFromPoint(Point point)
    {
        var width = Math.Max(1, HueBarSurface.Bounds.Width);
        var x = Math.Clamp(point.X, 0, width);
        hue = (x / width) * 360.0;
        ApplyHsvToSelectedColor();
    }

    private void RefreshFromSelectedColor()
    {
        if (!Color.TryParse(SelectedColor, out var color))
        {
            color = Colors.White;
        }

        SelectedBrush = new SolidColorBrush(color);
        RgbToHsv(color, out var parsedHue, out saturation, out value);
        if (saturation > 0.0001)
        {
            hue = parsedHue;
        }
        RefreshPickerVisuals();
    }

    private void ApplyHsvToSelectedColor()
    {
        var color = HsvToColor(hue, saturation, value);
        suppressEvents = true;
        SelectedColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        suppressEvents = false;
        SelectedBrush = new SolidColorBrush(color);
        RefreshPickerVisuals();
    }

    private void RefreshPickerVisuals()
    {
        suppressEvents = true;
        HexTextBox.Text = SelectedColor;
        suppressEvents = false;

        RefreshSvBitmap();

        var width = Math.Max(1, SvSurface.Bounds.Width);
        var height = Math.Max(1, SvSurface.Bounds.Height);
        Canvas.SetLeft(SvThumb, (saturation * width) - (SvThumb.Width / 2));
        Canvas.SetTop(SvThumb, ((1 - value) * height) - (SvThumb.Height / 2));

        var hueWidth = Math.Max(1, HueBarSurface.Bounds.Width);
        Canvas.SetLeft(HueThumb, ((hue / 360.0) * hueWidth) - (HueThumb.Width / 2));
        Canvas.SetTop(HueThumb, 0);
        RefreshHueBitmap();
    }

    private void RefreshSvBitmap()
    {
        var width = Math.Max(2, (int)Math.Round(SvSurface.Bounds.Width));
        var height = Math.Max(2, (int)Math.Round(SvSurface.Bounds.Height));
        if (width < 2 || height < 2)
        {
            return;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using var fb = bitmap.Lock();
        var data = new byte[fb.RowBytes * height];
        for (var y = 0; y < height; y++)
        {
            var v = 1.0 - (double)y / (height - 1);
            var rowStart = y * fb.RowBytes;
            for (var x = 0; x < width; x++)
            {
                var s = (double)x / (width - 1);
                var c = HsvToColor(hue, s, v);
                var idx = rowStart + (x * 4);
                data[idx + 0] = c.B;
                data[idx + 1] = c.G;
                data[idx + 2] = c.R;
                data[idx + 3] = 255;
            }
        }

        Marshal.Copy(data, 0, fb.Address, data.Length);
        SvImage.Source = bitmap;
    }

    private void RefreshHueBitmap()
    {
        var width = Math.Max(2, (int)Math.Round(HueBarSurface.Bounds.Width));
        var height = Math.Max(2, (int)Math.Round(HueBarSurface.Bounds.Height));
        if (width < 2 || height < 2)
        {
            return;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using var fb = bitmap.Lock();
        var data = new byte[fb.RowBytes * height];
        for (var y = 0; y < height; y++)
        {
            var rowStart = y * fb.RowBytes;
            for (var x = 0; x < width; x++)
            {
                var currentHue = (double)x / (width - 1) * 360.0;
                var c = HsvToColor(currentHue, 1, 1);
                var idx = rowStart + (x * 4);
                data[idx + 0] = c.B;
                data[idx + 1] = c.G;
                data[idx + 2] = c.R;
                data[idx + 3] = 255;
            }
        }

        Marshal.Copy(data, 0, fb.Address, data.Length);
        HueImage.Source = bitmap;
    }

    private static Color HsvToColor(double h, double s, double v)
    {
        h = h % 360;
        if (h < 0)
        {
            h += 360;
        }

        var c = v * s;
        var x = c * (1 - Math.Abs(((h / 60) % 2) - 1));
        var m = v - c;

        double r1;
        double g1;
        double b1;
        if (h < 60)
        {
            r1 = c; g1 = x; b1 = 0;
        }
        else if (h < 120)
        {
            r1 = x; g1 = c; b1 = 0;
        }
        else if (h < 180)
        {
            r1 = 0; g1 = c; b1 = x;
        }
        else if (h < 240)
        {
            r1 = 0; g1 = x; b1 = c;
        }
        else if (h < 300)
        {
            r1 = x; g1 = 0; b1 = c;
        }
        else
        {
            r1 = c; g1 = 0; b1 = x;
        }

        return Color.FromRgb(
            (byte)Math.Round((r1 + m) * 255),
            (byte)Math.Round((g1 + m) * 255),
            (byte)Math.Round((b1 + m) * 255));
    }

    private static void RgbToHsv(Color color, out double h, out double s, out double v)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        if (delta == 0)
        {
            h = 0;
        }
        else if (max == r)
        {
            h = 60 * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            h = 60 * (((b - r) / delta) + 2);
        }
        else
        {
            h = 60 * (((r - g) / delta) + 4);
        }

        if (h < 0)
        {
            h += 360;
        }

        s = max == 0 ? 0 : delta / max;
        v = max;
    }
}
