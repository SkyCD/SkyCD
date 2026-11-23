using System;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using SkyCD.UI.Localization;

namespace SkyCD.UI.Controls.Toolbars;

/// <summary>
/// Exposes toolbar toggle commands as UI Automation toggles with their live on/off state. Regular
/// toolbar commands keep the invoke-only behavior of <see cref="ButtonAutomationPeer"/>.
/// </summary>
internal sealed class ClassicToolbarButtonAutomationPeer : ButtonAutomationPeer, IToggleProvider
{
    public ClassicToolbarButtonAutomationPeer(ClassicToolbarButton owner)
        : base(owner)
    {
        owner.PropertyChanged += (_, e) =>
        {
            if (e.Property == ClassicToolbarButton.IsCheckedProperty)
            {
                RaisePropertyChangedEvent(
                    TogglePatternIdentifiers.ToggleStateProperty,
                    ToState(e.OldValue is true),
                    ToState(e.NewValue is true));
            }
        };
    }

    private ClassicToolbarButton Button => (ClassicToolbarButton)Owner;

    ToggleState IToggleProvider.ToggleState =>
        Button.IsChecked ? ToggleState.On : ToggleState.Off;

    void IToggleProvider.Toggle()
    {
        EnsureEnabled();
        var command = Button.Command;
        if (command is not null && command.CanExecute(Button.CommandParameter))
        {
            command.Execute(Button.CommandParameter);
        }
    }

    protected override object? GetProviderCore(Type providerType)
    {
        if (providerType == typeof(IToggleProvider) && !Button.IsToggle)
        {
            return null;
        }

        return base.GetProviderCore(providerType);
    }

    protected override string? GetNameCore()
    {
        var name = base.GetNameCore();
        return string.IsNullOrEmpty(name) ? MnemonicText.Strip(Button.Text) : name;
    }

    private static ToggleState ToState(bool isChecked) =>
        isChecked ? ToggleState.On : ToggleState.Off;
}
