using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class AddingProgressWindow : Window
{
    private INotifyCollectionChanged? observedLogEntries;

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
    }

    protected override void OnClosed(EventArgs e)
    {
        if (observedLogEntries is not null)
        {
            observedLogEntries.CollectionChanged -= OnLogEntriesChanged;
            observedLogEntries = null;
        }

        base.OnClosed(e);
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Wait for the ItemsControl to realize the new entries before scrolling.
        Dispatcher.UIThread.Post(() => LogScrollViewer.ScrollToEnd(), DispatcherPriority.Background);
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
