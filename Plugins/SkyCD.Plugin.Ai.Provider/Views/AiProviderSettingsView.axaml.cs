using Avalonia.Controls;
using SkyCD.Plugin.Ai.Provider.Configuration;

namespace SkyCD.Plugin.Ai.Provider.Views;

public partial class AiProviderSettingsView : UserControl
{
    public AiProviderSettingsView()
    {
        InitializeComponent();
    }

    public AiProviderSettingsView(AiProviderSettingsPageViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
