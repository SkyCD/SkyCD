using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Toolbars;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Issue #1004: toolbar view commands expose a checked state and toggle semantics, and the state can be
/// updated in place when the setting changes elsewhere.
/// </summary>
public class ToolbarToggleTests
{
    [AvaloniaFact]
    public void ClassicToolbarButton_IsChecked_TogglesCheckedPseudoClass()
    {
        var button = new ClassicToolbarButton { IsToggle = true };
        Assert.DoesNotContain(":checked", button.Classes);

        button.IsChecked = true;
        Assert.Contains(":checked", button.Classes);

        button.IsChecked = false;
        Assert.DoesNotContain(":checked", button.Classes);
    }

    [AvaloniaFact]
    public void ClassicToolbarButton_ToggleCommand_ExposesToggleAutomationState()
    {
        var button = new ClassicToolbarButton { IsToggle = true, IsChecked = false };
        var peer = ControlAutomationPeer.CreatePeerForElement(button);
        var toggle = peer.GetProvider<IToggleProvider>();

        Assert.NotNull(toggle);
        Assert.Equal(ToggleState.Off, toggle!.ToggleState);

        button.IsChecked = true;
        Assert.Equal(ToggleState.On, toggle.ToggleState);
    }

    [AvaloniaFact]
    public void ClassicToolbarButton_RegularCommand_DoesNotExposeToggleAutomation()
    {
        var button = new ClassicToolbarButton();
        var peer = ControlAutomationPeer.CreatePeerForElement(button);

        Assert.Null(peer.GetProvider<IToggleProvider>());
        Assert.NotNull(peer.GetProvider<IInvokeProvider>());
    }

    [AvaloniaFact]
    public void ClassicToolbarButton_ToggleProvider_ToggleExecutesTheBoundCommand()
    {
        var executed = 0;
        var button = new ClassicToolbarButton
        {
            IsToggle = true,
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => executed++)
        };
        var peer = ControlAutomationPeer.CreatePeerForElement(button);

        peer.GetProvider<IToggleProvider>()!.Toggle();

        Assert.Equal(1, executed);
    }

    [AvaloniaFact]
    public void ClassicToolbar_SetCheckedState_UpdatesRenderedToggleButtonInPlace()
    {
        var toolbar = new ClassicToolbar();
        var statusBar = new ClassicToolbarButton { CommandId = "status-bar", IsToggle = true };
        var details = new ClassicToolbarButton { CommandId = "view-details", IsToggle = true, IsChecked = true };
        toolbar.Items.Add(statusBar);
        toolbar.Items.Add(details);

        toolbar.SetCheckedState("status-bar", true);

        Assert.True(statusBar.IsChecked);
        Assert.Contains(":checked", statusBar.Classes);
        Assert.True(details.IsChecked);
    }

    [AvaloniaFact]
    public void FindToolbarButton_GetsCheckedStateWhileQuickFindIsOpen()
    {
        CatalogDocument[] entries = [new() { Id = "disc", Name = "Disc", Type = CatalogDocumentType.Media }];
        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog(entries);

        var findDescriptor = Assert.Single(viewModel.ToolbarCommands(), command => command.Id == ToolbarCommandIds.Find);
        var toolbar = new ClassicToolbar();
        var button = new ClassicToolbarButton
        {
            CommandId = findDescriptor.Id,
            Command = findDescriptor.Command,
            IsToggle = findDescriptor.IsToggle,
            IsChecked = findDescriptor.IsChecked
        };
        toolbar.Items.Add(button);
        viewModel.ToolbarStateChanged += (_, _) =>
        {
            foreach (var command in viewModel.ToolbarCommands())
            {
                if (command.IsToggle) toolbar.SetCheckedState(command.Id, command.IsChecked);
            }
        };

        Assert.False(button.IsChecked);

        viewModel.FindCatalogCommand.Execute(null);
        Assert.True(button.IsChecked);
        Assert.Contains(":checked", button.Classes);

        button.Command!.Execute(button.CommandParameter);
        Assert.False(viewModel.IsQuickFilterOpen);
        Assert.False(button.IsChecked);
        Assert.DoesNotContain(":checked", button.Classes);
    }

    [AvaloniaFact]
    public void ClassicToolbarButton_CheckedState_PaintsThemeBrushBackgroundFromAppStyles()
    {
        var window = new Window { Width = 200, Height = 50 };
        var buttonStyles = new Uri("avares://SkyCD.App/Styles/ButtonStyles.axaml");
        window.Styles.Add(new StyleInclude(buttonStyles) { Source = buttonStyles });

        var toolbar = new ClassicToolbar();
        var button = new ClassicToolbarButton { Text = "_Preview", IsToggle = true };
        toolbar.Items.Add(button);
        window.Content = toolbar;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var uncheckedBackground = Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color;

        button.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        var checkedBackground = Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color;
        Assert.NotEqual(uncheckedBackground, checkedBackground);
        Assert.Equal(
            Assert.IsAssignableFrom<ISolidColorBrush>(window.FindResource("ThemeBorderMidBrush")).Color,
            checkedBackground);

        window.Close();
    }
}
