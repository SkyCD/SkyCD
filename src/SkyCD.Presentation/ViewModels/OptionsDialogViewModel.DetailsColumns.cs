using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel
{
    public const string DetailsColumnsCategoryId = "details-columns";
    private const int DetailsColumnsCategoryIndex = 7;

    public ObservableCollection<DetailsColumnOptionItemViewModel> DetailsColumns { get; } = [];

    [ObservableProperty] private DetailsColumnOptionItemViewModel? selectedDetailsColumn;

    public bool IsDetailsColumnsCategorySelected => SelectedBuiltInIndex == DetailsColumnsCategoryIndex;

    public bool ShowDetailsColumnsSection =>
        IsDetailsColumnsCategorySelected &&
        IsCurrentCategoryVisibleInSearch;

    public void SetDetailsColumns(IEnumerable<DetailsColumnSettingDocument>? settings)
    {
        DetailsColumns.Clear();
        foreach (var setting in DetailsColumnsCatalog.Normalize(settings))
        {
            var definition = DetailsColumnsCatalog.Find(setting.Id);
            if (definition is null)
            {
                continue;
            }

            DetailsColumns.Add(new DetailsColumnOptionItemViewModel
            {
                Id = definition.Id,
                Header = definition.DisplayHeader,
                IsVisible = setting.IsVisible,
                IsRequired = definition.IsRequired,
                Width = setting.Width
            });
        }

        SelectedDetailsColumn = DetailsColumns.FirstOrDefault();
        NotifyDetailsColumnMoveCommands();
    }

    public IReadOnlyList<DetailsColumnSettingDocument> GetDetailsColumns()
    {
        return DetailsColumns
            .Select(item => new DetailsColumnSettingDocument
            {
                Id = item.Id,
                IsVisible = item.IsRequired || item.IsVisible,
                Width = item.Width
            })
            .ToArray();
    }

    [RelayCommand(CanExecute = nameof(CanMoveDetailsColumnUp))]
    private void MoveDetailsColumnUp()
    {
        MoveSelectedDetailsColumn(-1);
    }

    [RelayCommand(CanExecute = nameof(CanMoveDetailsColumnDown))]
    private void MoveDetailsColumnDown()
    {
        MoveSelectedDetailsColumn(1);
    }

    private void MoveSelectedDetailsColumn(int offset)
    {
        if (SelectedDetailsColumn is null)
        {
            return;
        }

        var index = DetailsColumns.IndexOf(SelectedDetailsColumn);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= DetailsColumns.Count)
        {
            return;
        }

        DetailsColumns.Move(index, target);
        NotifyDetailsColumnMoveCommands();
    }

    private bool CanMoveDetailsColumnUp()
    {
        return SelectedDetailsColumn is not null && DetailsColumns.IndexOf(SelectedDetailsColumn) > 0;
    }

    private bool CanMoveDetailsColumnDown()
    {
        return SelectedDetailsColumn is not null &&
               DetailsColumns.IndexOf(SelectedDetailsColumn) is var index &&
               index >= 0 &&
               index < DetailsColumns.Count - 1;
    }

    partial void OnSelectedDetailsColumnChanged(DetailsColumnOptionItemViewModel? value)
    {
        NotifyDetailsColumnMoveCommands();
    }

    private void NotifyDetailsColumnMoveCommands()
    {
        MoveDetailsColumnUpCommand.NotifyCanExecuteChanged();
        MoveDetailsColumnDownCommand.NotifyCanExecuteChanged();
    }

    private void RefreshDetailsColumnsLocalization()
    {
        foreach (var item in DetailsColumns)
        {
            item.RefreshLocalization(DetailsColumnsCatalog.Find(item.Id)?.DisplayHeader ?? item.Header);
        }
    }
}
