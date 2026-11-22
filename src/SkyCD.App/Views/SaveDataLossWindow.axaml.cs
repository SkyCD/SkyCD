using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SkyCD.UI.Localization;

namespace SkyCD.App.Views;

public partial class SaveDataLossWindow : Window
{
    public SaveDataLossWindow()
    {
        InitializeComponent();
    }

    public SaveDataLossWindow(string formatDisplayName, IReadOnlyList<string> lines) : this()
    {
        IntroText.Text = LocalizationService.Current.Format(UiKeys.SaveDataLossIntro, formatDisplayName);
        LossList.ItemsSource = lines;
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnDoNotSaveClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
