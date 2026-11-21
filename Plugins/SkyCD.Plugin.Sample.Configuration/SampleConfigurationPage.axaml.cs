using Avalonia.Controls;

namespace SkyCD.Plugin.Sample.Configuration;

public partial class SampleConfigurationPage : UserControl
{
    private readonly SampleConfigurationEditor editor;

    public SampleConfigurationPage()
        : this(new SampleConfigurationEditor())
    {
    }

    public SampleConfigurationPage(SampleConfigurationEditor editor)
    {
        this.editor = editor;
        InitializeComponent();
        GreetingTextBox.Text = editor.Greeting;
        SignatureTextBox.Text = editor.Signature;
    }

    public void UpdateDraft()
    {
        editor.Greeting = GreetingTextBox.Text ?? string.Empty;
        editor.Signature = SignatureTextBox.Text ?? string.Empty;
    }

}
