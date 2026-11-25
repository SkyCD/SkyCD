using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class AddingProgressWindow : Window
{
    private INotifyCollectionChanged? observedLogEntries;
    private AddingProgressDialogViewModel? subscribedProgressVm;
    private readonly TaskbarProgressThrottle taskbarThrottle = new();
    private bool scrollPending;

    public AddingProgressWindow()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            if (DataContext is AddingProgressDialogViewModel vm)
            {
                if (vm.CanCancel || vm.IsCancellationPending)
                {
                    vm.Cancel();
                    e.Cancel = true;
                    return;
                }

                vm.StopAnalysis();
            }
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (observedLogEntries is not null)
        {
            observedLogEntries.CollectionChanged -= OnLogEntriesChanged;
        }

        observedLogEntries = (DataContext as AddingProgressDialogViewModel)?.LogEntries;
        if (observedLogEntries is not null)
        {
            observedLogEntries.CollectionChanged += OnLogEntriesChanged;
        }

        if (subscribedProgressVm is not null)
        {
            subscribedProgressVm.PropertyChanged -= OnProgressViewModelPropertyChanged;
        }

        subscribedProgressVm = DataContext as AddingProgressDialogViewModel;
        if (subscribedProgressVm is not null)
        {
            subscribedProgressVm.PropertyChanged += OnProgressViewModelPropertyChanged;
            ReportTaskbarProgress();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (observedLogEntries is not null)
        {
            observedLogEntries.CollectionChanged -= OnLogEntriesChanged;
            observedLogEntries = null;
        }

        if (subscribedProgressVm is not null)
        {
            subscribedProgressVm.PropertyChanged -= OnProgressViewModelPropertyChanged;
            subscribedProgressVm = null;
        }

        if (OperatingSystem.IsWindows())
        {
            WindowsTaskbarProgress.Clear(TaskbarWindowHandle);
        }

        base.OnClosed(e);
    }

    private void OnProgressViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AddingProgressDialogViewModel.OverallProgressValue)
            or nameof(AddingProgressDialogViewModel.IsOverallIndeterminate))
        {
            ReportTaskbarProgress();
        }
    }

    private void ReportTaskbarProgress()
    {
        if (!OperatingSystem.IsWindows() || subscribedProgressVm is null)
        {
            return;
        }

        var state = subscribedProgressVm.IsOverallIndeterminate
            ? TaskbarProgressState.Indeterminate
            : TaskbarProgressState.Normal;
        var completed = (ulong)Math.Clamp(subscribedProgressVm.OverallProgressValue, 0, 100);

        if (!taskbarThrottle.ShouldReport(state, completed))
        {
            return;
        }

        if (state == TaskbarProgressState.Indeterminate)
        {
            WindowsTaskbarProgress.SetIndeterminate(TaskbarWindowHandle);
        }
        else
        {
            WindowsTaskbarProgress.SetProgress(TaskbarWindowHandle, completed);
        }
    }

    private IntPtr TaskbarWindowHandle
    {
        get
        {
            // The progress dialog never shows in the taskbar itself, so the indicator belongs
            // to the owning main window; fall back to this window when shown without an owner.
            var owner = Owner as TopLevel ?? this;
            return owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        }
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A whole flush arrives as many Add events; scroll once after the batch is realized.
        if (scrollPending)
        {
            return;
        }

        scrollPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            scrollPending = false;
            LogScrollViewer.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    private void OnDismissClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddingProgressDialogViewModel vm)
        {
            return;
        }

        if (vm.IsCloseMode)
        {
            Close();
        }
        else if (vm.CanCancel)
        {
            vm.Cancel();
        }
    }

    private async void OnCopyLogClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AddingProgressDialogViewModel vm)
        {
            var text = vm.GetFullLogText();
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (!string.IsNullOrEmpty(text) && clipboard is not null)
            {
                await clipboard.SetTextAsync(text);
            }
        }
    }
}
