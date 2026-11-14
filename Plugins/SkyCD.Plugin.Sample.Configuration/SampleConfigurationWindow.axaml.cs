using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SkyCD.Plugin.Sample.Configuration;

public partial class SampleConfigurationWindow : Window
{
    private readonly SampleConfigurationEditor editor;

    public SampleConfigurationWindow()
        : this(new SampleConfigurationEditor())
    {
    }

    public SampleConfigurationWindow(SampleConfigurationEditor editor)
    {
        this.editor = editor;
        InitializeComponent();
        GreetingTextBox.Text = editor.Greeting;
        SignatureTextBox.Text = editor.Signature;
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        editor.Greeting = GreetingTextBox.Text ?? string.Empty;
        editor.Signature = SignatureTextBox.Text ?? string.Empty;
        Close(true);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
