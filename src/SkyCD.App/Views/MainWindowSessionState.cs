using System.Linq;
using Avalonia.Controls;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

internal sealed class MainWindowSessionState(AppOptionsDocumentRepository repository)
{
    public void Save(Window window, ColumnDefinition treePaneColumn, MainWindowViewModel vm)
    {
        var options = repository.GetOrCreateAppOptions();

        MainWindowPlacement.Capture(options.Window, window.WindowState, window.Position,
            window.Width, window.Height, treePaneColumn.Width);

        options.IsStatusBarVisible = vm.IsStatusBarVisible;
        options.Browser.ViewMode = vm.CurrentViewMode;
        options.Browser.SortMode = vm.CurrentSortMode;
        options.Browser.SortDescending = vm.IsSortDescending;
        options.Browser.DetailsColumns = vm.GetDetailsColumnSettings().ToList();
        if (!string.IsNullOrWhiteSpace(vm.CurrentCatalogPath))
        {
            options.LastOpenedCatalogPath = vm.CurrentCatalogPath;
        }
        repository.Save(AppOptionsDocument.DocumentId, options);
    }
}
