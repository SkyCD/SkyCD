using Avalonia.Controls;
using SkyCD.Presentation.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SkyCD.App.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? subscribedViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.AddToListRequested -= OnAddToListRequested;
            subscribedViewModel.AboutRequested -= OnAboutRequested;
            subscribedViewModel.PropertiesRequested -= OnPropertiesRequested;
        }

        subscribedViewModel = DataContext as MainWindowViewModel;
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.AddToListRequested += OnAddToListRequested;
            subscribedViewModel.AboutRequested += OnAboutRequested;
            subscribedViewModel.PropertiesRequested += OnPropertiesRequested;
        }
    }

    private async void OnAddToListRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var dialogVm = new AddToListDialogViewModel();
        var dialog = new AddToListWindow
        {
            DataContext = dialogVm
        };

        var accepted = await dialog.ShowDialog<bool?>(this);
        if (accepted != true)
        {
            return;
        }

        if (dialogVm.SourceMode == AddToListSourceMode.Internet)
        {
            var loginVm = new LoginDialogViewModel();
            var loginDialog = new LoginWindow
            {
                DataContext = loginVm
            };

            var loginAccepted = await loginDialog.ShowDialog<bool?>(this);
            if (loginAccepted != true)
            {
                vm.StatusText = "Login canceled.";
                return;
            }
        }

        await ShowAddProgressAsync(dialogVm);

        vm.StatusText = "Done.";
        vm.IsDirtyDocument = true;
    }

    private async void OnPropertiesRequested(object? sender, PropertiesDialogRequestedEventArgs e)
    {
        var dialog = new PropertiesWindow
        {
            DataContext = e.Dialog
        };

        var accepted = await dialog.ShowDialog<bool?>(this);
        e.Complete(accepted == true, e.Dialog.Comments);
    }

    private async void OnAboutRequested(object? sender, EventArgs e)
    {
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "3.0.0";
        var dialogVm = new AboutDialogViewModel("SkyCD", version, "https://github.com/SkyCD/SkyCD");
        var dialog = new AboutWindow
        {
            DataContext = dialogVm
        };

        await dialog.ShowDialog<bool?>(this);
    }

    private async Task ShowAddProgressAsync(AddToListDialogViewModel addDialog)
    {
        var progressVm = new AddingProgressDialogViewModel();
        var progressDialog = new AddingProgressWindow
        {
            DataContext = progressVm
        };

        progressDialog.Show(this);
        try
        {
            foreach (var (text, value) in BuildAddProgressSteps(addDialog))
            {
                progressVm.OperationText = text;
                progressVm.ProgressValue = value;
                await Task.Delay(140);
            }
        }
        finally
        {
            progressDialog.Close();
        }
    }

    private static IReadOnlyList<(string Text, int Value)> BuildAddProgressSteps(AddToListDialogViewModel addDialog)
    {
        return addDialog.SourceMode switch
        {
            AddToListSourceMode.Internet =>
            [
                ("Reading directory from remote server...", 20),
                ("Preparing database for modifications...", 55),
                ("Updating indexes...", 100)
            ],
            AddToListSourceMode.Folder =>
            [
                ("Reading source folder...", 25),
                ("Preparing database for modifications...", 60),
                ("Updating indexes...", 100)
            ],
            _ =>
            [
                ("Reading media metadata...", 35),
                ("Preparing database for modifications...", 70),
                ("Updating indexes...", 100)
            ]
        };
    }
}
