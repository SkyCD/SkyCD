using System;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class AboutWindow : Window
{
    private readonly DispatcherTimer statsTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private AboutDialogViewModel? subscribedViewModel;
    private bool isWindowOpen;

    public AboutWindow()
    {
        InitializeComponent();
        statsTimer.Tick += OnStatsTimerTick;
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    internal bool IsSystemInfoSampling => statsTimer.IsEnabled;

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

        UpdateStatsTimer();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        isWindowOpen = true;
        UpdateStatsTimer();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        isWindowOpen = false;
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

    private void UpdateStatsTimer()
    {
        if (!isWindowOpen || subscribedViewModel is not { } vm)
        {
            statsTimer.Stop();
            return;
        }

        if (vm.SelectedTabKey == AboutDialogViewModel.SystemInfoTabKey)
        {
            vm.RefreshSystemInfo();
            if (!statsTimer.IsEnabled)
            {
                statsTimer.Start();
            }
        }
        else
        {
            statsTimer.Stop();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AboutDialogViewModel vm)
        {
            return;
        }

        if (e.PropertyName == nameof(AboutDialogViewModel.DialogAccepted) && vm.DialogAccepted)
        {
            Close(true);
            return;
        }

        if (e.PropertyName == nameof(AboutDialogViewModel.SelectedTabIndex))
        {
            UpdateStatsTimer();
        }
    }

    private void OnOpenRepositoryUrl(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBlock textBlock)
        {
            return;
        }

        var url = textBlock.Tag as string ?? textBlock.Text;
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
