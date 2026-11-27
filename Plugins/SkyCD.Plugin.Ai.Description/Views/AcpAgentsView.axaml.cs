using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SkyCD.Plugin.Ai.Description.Models;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Ai.Description.Views;

public partial class AcpAgentsView : UserControl
{
    public AcpAgentsView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => _ = InitializeAsync();
    }

    public AcpAgentsView(AcpAgentsPageViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private async Task InitializeAsync()
    {
        if (DataContext is AcpAgentsPageViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private async void OnBrowseAcpAgentClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AcpAgentsPageViewModel { CanChooseAcpAgent: true } viewModel ||
            sender is not Button { DataContext: AcpAgentChoice { ExpectedExecutable: { } expected } agent }) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.Current.Format(UiKeys.AcpFindExecutable, expected),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(expected) { Patterns = [expected] }]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
            viewModel.ChooseLocalAcpAgent(agent, path);
    }

    private async void OnInstallAcpAgentClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AcpAgentChoice agent } || DataContext is not AcpAgentsPageViewModel viewModel)
            return;

        var error = agent.IsManaged
            ? await viewModel.UpdateAcpAgentAsync(agent, viewModel.Lifetime)
            : await viewModel.InstallAcpAgentAsync(agent, viewModel.Lifetime);
        if (error is not null) viewModel.AcpAlertMessage = error;
    }

    private async void OnUninstallAcpAgentClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AcpAgentChoice agent } || DataContext is not AcpAgentsPageViewModel viewModel)
            return;

        var error = await viewModel.UninstallAcpAgentAsync(agent, viewModel.Lifetime);
        if (error is not null) viewModel.AcpAlertMessage = error;
    }

    private void OnSelectAcpAgentClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AcpAgentChoice { IsInstalled: true } agent } &&
            DataContext is AcpAgentsPageViewModel { CanChooseAcpAgent: true } viewModel)
            viewModel.SelectAcpAgent(agent);
    }
}
