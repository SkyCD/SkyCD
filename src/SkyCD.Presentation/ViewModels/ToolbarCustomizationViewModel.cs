using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Edits a working copy of the toolbar layout for the Options window; the page is only handed to
/// <see cref="MainWindowViewModel.ApplyToolbarLayout"/> when Save accepts it.
/// </summary>
public sealed partial class ToolbarCustomizationViewModel : ObservableObject
{
    private readonly Dictionary<string, ToolbarCommandChoiceViewModel> choicesById;

    public ToolbarCustomizationViewModel(
        IReadOnlyList<ToolbarCommandDescriptor> commands,
        IEnumerable<ToolbarItemDocument> layout)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(layout);
        choicesById = new Dictionary<string, ToolbarCommandChoiceViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands)
        {
            var choice = new ToolbarCommandChoiceViewModel(command);
            choicesById[command.Id] = choice;
            AvailableCommands.Add(choice);
        }

        LoadLayout(layout);
    }

    public ObservableCollection<ToolbarCommandChoiceViewModel> AvailableCommands { get; } = [];

    public ObservableCollection<ToolbarEntryViewModel> SelectedItems { get; } = [];

    [ObservableProperty] private ToolbarCommandChoiceViewModel? selectedCommand;

    [ObservableProperty] private ToolbarEntryViewModel? selectedItem;

    public IReadOnlyList<ToolbarItemDocument> GetLayout() => SelectedItems.Select(entry => entry.ToDocument()).ToArray();

    partial void OnSelectedCommandChanged(ToolbarCommandChoiceViewModel? value)
    {
        AddCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedItemChanged(ToolbarEntryViewModel? value)
    {
        NotifyEntryCommands();
    }

    private bool CanAdd() => SelectedCommand is { IsInToolbar: false };

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        if (SelectedCommand is null || SelectedCommand.IsInToolbar)
        {
            return;
        }

        var entry = ToolbarEntryViewModel.FromCommand(SelectedCommand.Descriptor);
        SelectedItems.Insert(InsertionIndex, entry);
        SelectedCommand.IsInToolbar = true;
        SelectedItem = entry;
        AddCommand.NotifyCanExecuteChanged();
    }

    private bool CanAddSeparator() => SelectedItems.Count > 0;

    [RelayCommand(CanExecute = nameof(CanAddSeparator))]
    private void AddSeparator()
    {
        var entry = ToolbarEntryViewModel.CreateSeparator();
        SelectedItems.Insert(InsertionIndex, entry);
        SelectedItem = entry;
    }

    private bool CanRemove() => SelectedItem is not null;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var index = SelectedItems.IndexOf(SelectedItem);
        if (SelectedItem.Descriptor is { } descriptor && choicesById.TryGetValue(descriptor.Id, out var choice))
        {
            choice.IsInToolbar = false;
        }

        SelectedItems.RemoveAt(index);
        SelectedItem = SelectedItems.Count == 0 ? null : SelectedItems[Math.Min(index, SelectedItems.Count - 1)];
        RefreshAvailability();
        AddCommand.NotifyCanExecuteChanged();
        AddSeparatorCommand.NotifyCanExecuteChanged();
    }

    private bool CanMoveUp() => SelectedItem is not null && SelectedItems.IndexOf(SelectedItem) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => MoveSelectedItem(-1);

    private bool CanMoveDown() =>
        SelectedItem is not null && SelectedItems.IndexOf(SelectedItem) is var index && index >= 0 &&
        index < SelectedItems.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => MoveSelectedItem(1);

    [RelayCommand]
    private void Reset() => LoadLayout(ToolbarLayout.CreateDefault());

    private void MoveSelectedItem(int offset)
    {
        if (SelectedItem is null)
        {
            return;
        }

        var index = SelectedItems.IndexOf(SelectedItem);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= SelectedItems.Count)
        {
            return;
        }

        SelectedItems.Move(index, target);
        NotifyEntryCommands();
    }

    private void LoadLayout(IEnumerable<ToolbarItemDocument> layout)
    {
        SelectedItems.Clear();
        foreach (var item in layout)
        {
            if (string.Equals(item.Kind, ToolbarItemDocument.SeparatorKind, StringComparison.OrdinalIgnoreCase))
            {
                SelectedItems.Add(ToolbarEntryViewModel.CreateSeparator());
            }
            else if (item.CommandId is { } id && choicesById.TryGetValue(id, out var choice))
            {
                SelectedItems.Add(ToolbarEntryViewModel.FromCommand(choice.Descriptor));
            }
        }

        SelectedItem = SelectedItems.FirstOrDefault();
        RefreshAvailability();
        AddCommand.NotifyCanExecuteChanged();
        AddSeparatorCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Recomputes which commands are already on the toolbar after any list change.</summary>
    private void RefreshAvailability()
    {
        var present = SelectedItems
            .Select(entry => entry.CommandId)
            .Where(id => id is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var choice in AvailableCommands)
        {
            choice.IsInToolbar = present.Contains(choice.Descriptor.Id);
        }

        AddCommand.NotifyCanExecuteChanged();
    }

    private int InsertionIndex
    {
        get
        {
            var index = SelectedItem is null ? -1 : SelectedItems.IndexOf(SelectedItem);
            return index < 0 ? SelectedItems.Count : index + 1;
        }
    }

    private void NotifyEntryCommands()
    {
        RemoveCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }
}
