using Avalonia.Controls;

namespace SkyCD.App.Documents;

public sealed class WindowOptionsDocument
{
    public int? Left { get; set; }

    public int? Top { get; set; }

    public double? Width { get; set; }

    public double? Height { get; set; }

    public WindowState State { get; set; } = WindowState.Normal;

    public double? TreePaneWidth { get; set; }
}
