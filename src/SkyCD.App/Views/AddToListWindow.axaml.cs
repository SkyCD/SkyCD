using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;

namespace SkyCD.App.Views;

public partial class AddToListWindow : Window
{
    private readonly DispatcherTimer refreshTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500)
    };

    private bool checkingClipboard;
    private bool pastingUrl;
    private AddToListDialogViewModel? subscribedViewModel;

    public AddToListWindow()
    {
        InitializeComponent();
        refreshTimer.Tick += OnRefreshTimerTick;
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

        if (sender is AddToListWindow window && window.DataContext is AddToListDialogViewModel vm)
        {
            subscribedViewModel = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
        else
        {
            subscribedViewModel = null;
        }
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        subscribedViewModel?.RefreshDrives();
        refreshTimer.Start();
        await RefreshClipboardAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        refreshTimer.Stop();
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            subscribedViewModel.Dispose();
            subscribedViewModel = null;
        }
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        subscribedViewModel?.RefreshDrives();
        await RefreshClipboardAsync();
    }

    private async Task RefreshClipboardAsync()
    {
        if (checkingClipboard || pastingUrl || subscribedViewModel?.IsSourceInternet != true)
        {
            return;
        }

        checkingClipboard = true;
        try
        {
            var text = Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
            PasteInternetUrlButton.IsEnabled = !pastingUrl && !string.IsNullOrEmpty(text);
        }
        catch (Exception)
        {
            // The platform may temporarily deny access while another application owns the clipboard.
            PasteInternetUrlButton.IsEnabled = false;
        }
        finally
        {
            checkingClipboard = false;
        }
    }

    private async void OnPasteInternetUrlClicked(object? sender, RoutedEventArgs e)
    {
        if (pastingUrl || subscribedViewModel is not { IsSourceInternet: true } vm)
        {
            return;
        }

        pastingUrl = true;
        PasteInternetUrlButton.IsEnabled = false;
        try
        {
            string? text = null;
            string? errorKey = null;
            try
            {
                text = Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
            }
            catch (Exception)
            {
                errorKey = UiKeys.InternetClipboardUnavailable;
            }

            if (subscribedViewModel == vm && vm.IsSourceInternet &&
                (errorKey ?? vm.FillInternetFromUrl(text)) is { } error)
            {
                await ShowClipboardErrorAsync(error);
            }
        }
        finally
        {
            pastingUrl = false;
            await RefreshClipboardAsync();
        }
    }

    private Task ShowClipboardErrorAsync(string messageKey)
    {
        var localization = LocalizationService.Current;
        var close = new Button
        {
            Content = localization.Get(UiKeys.Ok),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
            IsCancel = true,
        };
        var dialog = new Window
        {
            Title = localization.Get(UiKeys.PasteInternetUrl),
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(14),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = localization.Get(messageKey), TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    close,
                },
            },
        };
        close.Click += (_, _) => dialog.Close();
        return dialog.ShowDialog(this);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is AddToListDialogViewModel vm &&
            e.PropertyName == nameof(AddToListDialogViewModel.DialogAccepted) &&
            vm.DialogAccepted)
        {
            Close(true);
        }
    }

    private async void OnBrowseFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddToListDialogViewModel vm)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.Current.Get(UiKeys.SelectFolder),
            AllowMultiple = false
        });

        var localPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            vm.SourceValue = localPath;
            if (string.IsNullOrWhiteSpace(vm.MediaName))
            {
                var trimmed = localPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var folderName = Path.GetFileName(trimmed);
                if (string.IsNullOrWhiteSpace(folderName))
                {
                    folderName = trimmed;
                }
                vm.MediaName = folderName;
            }
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void OnBrowseCaptureDestinationClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddToListDialogViewModel vm)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.Current.Get(UiKeys.CaptureDestination),
            AllowMultiple = false
        });

        var localPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            vm.CaptureDestination = localPath;
        }
    }

    private async void OnBrowseCaptureSourceClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddToListDialogViewModel vm)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = vm.CaptureLocationLabel,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(LocalizationService.Current.Get(UiKeys.CaptureSource))
                {
                    Patterns =
                    [
                        "*.mp4", "*.avi", "*.mkv", "*.mov", "*.wmv", "*.mpg", "*.mpeg", "*.webm", "*.mjpg",
                        "*.mjpeg", "*.ts", "*.m2ts", "*.dv"
                    ]
                }
            ]
        });

        var localPath = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            vm.CaptureLocation = localPath;
        }
    }

    private async void OnPreviewCaptureClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddToListDialogViewModel vm)
        {
            return;
        }

        var result = await vm.CapturePreviewAsync();
        if (result is { Succeeded: true, Image: { Length: > 0 } image })
        {
            await ShowCapturePreviewAsync(image);
        }
    }

    private Task ShowCapturePreviewAsync(byte[] image)
    {
        var localization = LocalizationService.Current;
        var close = new Button
        {
            Content = localization.Get(UiKeys.Ok),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
            IsCancel = true,
        };
        var dialog = new Window
        {
            Title = localization.Get(UiKeys.CapturePreviewTitle),
            Width = 660,
            MaxHeight = 760,
            SizeToContent = SizeToContent.Height,
            CanResize = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(14),
                Spacing = 12,
                Children =
                {
                    new Image
                    {
                        Source = new Bitmap(new MemoryStream(image)),
                        Stretch = Avalonia.Media.Stretch.Uniform,
                        MaxHeight = 640,
                    },
                    close,
                },
            },
        };
        close.Click += (_, _) => dialog.Close();
        return dialog.ShowDialog(this);
    }
}