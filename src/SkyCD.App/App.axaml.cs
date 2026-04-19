using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SkyCD.App.Services;
using SkyCD.Presentation.ViewModels;
using SkyCD.App.Views;

namespace SkyCD.App;

public partial class App : Avalonia.Application
{
    private readonly SqliteBrowserDataStore browserDataStore = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += (_, _) => browserDataStore.Dispose();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(browserDataStore),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
