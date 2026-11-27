using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Plugin.Feature.MotionDetector.Exceptions;
using SkyCD.Plugin.Feature.MotionDetector.Models;
using SkyCD.SDK.Capabilities.Sources;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.MotionDetector.Views;

/// <summary>One selectable detector, including the "no detector" entry for continuous capture.</summary>
public sealed record DetectorChoice(string? Id, string DisplayName, string Description, bool IsNone = false);

/// <summary>A numeric detector parameter bound to a slider row on the options page.</summary>
public sealed partial class DetectorParameterRow : ObservableObject
{
    public DetectorParameterRow(SourceDetectorParameter parameter, double value)
    {
        Key = parameter.Key;
        DisplayName = parameter.DisplayName;
        Minimum = parameter.Minimum;
        Maximum = parameter.Maximum;
        this.value = value;
    }

    public string Key { get; }

    public string DisplayName { get; }

    public double Minimum { get; }

    public double Maximum { get; }

    [ObservableProperty]
    private double value;

    public string ValueText => Value.ToString("0.###", CultureInfo.CurrentCulture);

    partial void OnValueChanged(double value) => OnPropertyChanged(nameof(ValueText));
}

/// <summary>
/// Options page for the global detector selection. The page reads the plugin's in-memory settings, so it opens
/// without touching storage; saving persists through plugin storage, which requires an open catalog.
/// </summary>
public sealed partial class MotionDetectorOptionsViewModel : ObservableObject
{
    private readonly MotionDetectorPlugin plugin;
    private readonly Dictionary<string, Dictionary<string, double>> remembered = new(StringComparer.Ordinal);

    public MotionDetectorOptionsViewModel(MotionDetectorPlugin plugin)
    {
        this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        IsCatalogOpen = plugin.IsCatalogAvailable;
        foreach (var detector in plugin.GetDetectors())
        {
            Detectors.Add(new DetectorChoice(detector.Id, detector.DisplayName, detector.Description));
        }

        Detectors.Add(new DetectorChoice(null, LocalizationService.Current.Get(MotionDetectorKeys.DetectorNone),
            string.Empty, IsNone: true));

        var settings = plugin.GetDetectorSettings();
        SelectedDetector = Detectors.FirstOrDefault(choice => choice.Id == settings.DetectorId)
                           ?? Detectors.FirstOrDefault(choice => choice.IsNone == (settings.DetectorId is null));
        if (SelectedDetector?.Id == settings.DetectorId && settings.DetectorId is not null)
        {
            remembered[settings.DetectorId] = new Dictionary<string, double>(settings.Parameters,
                StringComparer.Ordinal);
        }

        RebuildParameters();
    }

    public ObservableCollection<DetectorChoice> Detectors { get; } = [];

    public ObservableCollection<DetectorParameterRow> Parameters { get; } = [];

    [ObservableProperty]
    private DetectorChoice? selectedDetector;

    [ObservableProperty]
    private bool isCatalogOpen;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string errorText = string.Empty;

    public bool HasParameters => Parameters.Count > 0;

    partial void OnSelectedDetectorChanged(DetectorChoice? value) => RebuildParameters();

    private void RebuildParameters()
    {
        RememberSelection();
        Parameters.Clear();
        var choice = SelectedDetector;
        if (choice is null || choice.IsNone)
        {
            OnPropertyChanged(nameof(HasParameters));
            return;
        }

        var descriptor = plugin.GetDetectors().FirstOrDefault(detector => detector.Id == choice.Id);
        if (descriptor is null)
        {
            OnPropertyChanged(nameof(HasParameters));
            return;
        }

        var values = remembered.TryGetValue(descriptor.Id, out var stored)
            ? stored
            : descriptor.Parameters.ToDictionary(parameter => parameter.Key,
                parameter => parameter.DefaultValue, StringComparer.Ordinal);
        foreach (var parameter in descriptor.Parameters)
        {
            Parameters.Add(new DetectorParameterRow(parameter,
                values.TryGetValue(parameter.Key, out var value) ? value : parameter.DefaultValue));
        }

        OnPropertyChanged(nameof(HasParameters));
    }

    private void RememberSelection()
    {
        if (SelectedDetector?.Id is { Length: > 0 } id && Parameters.Count > 0)
        {
            remembered[id] = Parameters.ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        StatusText = string.Empty;
        ErrorText = string.Empty;
        RememberSelection();
        var parameters = Parameters.ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        try
        {
            await plugin.SaveDetectorSettingsAsync(new SourceDetectorSettings(SelectedDetector?.Id, parameters));
            StatusText = LocalizationService.Current.Get(MotionDetectorKeys.Saved);
        }
        catch (MotionDetectorSettingsException)
        {
            ErrorText = LocalizationService.Current.Get(MotionDetectorKeys.CatalogUnavailable);
        }
        catch (MotionDetectorSourceException exception)
        {
            ErrorText = exception.Message;
        }
    }
}
