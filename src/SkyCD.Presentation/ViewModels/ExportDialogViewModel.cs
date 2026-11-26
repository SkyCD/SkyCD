using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Presentation.Catalog.Export;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Picks the write-only format for File &gt; Export. The dialog owns the choice only; the host shows
/// the target picker and runs the write, so the view model never touches files and stays unit-testable.
/// </summary>
public sealed partial class ExportDialogViewModel : ObservableObject
{
    public ExportDialogViewModel(IReadOnlyList<ExportFormatOption> formats, string? defaultFormatId = null)
    {
        Formats = formats;
        selectedExportFormat = SelectDefaultFormat(formats, defaultFormatId);
    }

    /// <summary>Gets the export formats the dialog offers.</summary>
    public IReadOnlyList<ExportFormatOption> Formats { get; }

    /// <summary>Gets the format the host should export, once <see cref="ExportCommand"/> closes the dialog.</summary>
    public ExportFormatOption? AcceptedFormat { get; private set; }

    /// <summary>Raised when the dialog can close after <see cref="ExportCommand"/> or cancellation.</summary>
    public event Action? CloseRequested;

    [ObservableProperty] private ExportFormatOption? selectedExportFormat;
    [ObservableProperty] private string errorText = string.Empty;

    private bool CanExport => SelectedExportFormat is not null;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export()
    {
        if (SelectedExportFormat is not { } format)
        {
            ErrorText = LocalizationService.Current.Get(UiKeys.ExportNoFormat);
            return;
        }

        AcceptedFormat = format;
        CloseRequested?.Invoke();
    }

    private static ExportFormatOption? SelectDefaultFormat(
        IReadOnlyList<ExportFormatOption> formats, string? defaultFormatId)
    {
        if (!string.IsNullOrWhiteSpace(defaultFormatId))
        {
            var match = formats.FirstOrDefault(format =>
                format.FormatId.Equals(defaultFormatId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return formats.Count > 0 ? formats[0] : null;
    }
}
