using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SkyCD.Presentation.ViewModels;
using System;
using System.ComponentModel;
using System.Diagnostics;

namespace SkyCD.App.Views;

public partial class AboutWindow : Window
{
    private readonly DispatcherTimer statsTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private AboutDialogViewModel? subscribedViewModel;

    public AboutWindow()
    {
        InitializeComponent();
        statsTimer.Tick += OnStatsTimerTick;
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (sender is AboutWindow window && window.DataContext is AboutDialogViewModel vm)
        {
            subscribedViewModel = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
        else
        {
            subscribedViewModel = null;
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        subscribedViewModel?.RefreshSystemInfo();
        statsTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        statsTimer.Stop();
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnStatsTimerTick(object? sender, EventArgs e)
    {
        subscribedViewModel?.RefreshSystemInfo();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is AboutDialogViewModel vm &&
            e.PropertyName == nameof(AboutDialogViewModel.DialogAccepted) &&
            vm.DialogAccepted)
        {
            Close(true);
        }
    }

    private void OnOpenRepositoryUrl(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBlock textBlock)
        {
            return;
        }

        var url = textBlock.Text;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Intentionally ignore shell launch errors for About links.
        }
    }
}
