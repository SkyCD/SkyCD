using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Presentation.MediaCache;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Unified description field for the Properties dialog. A catalog Description property (imported metadata
/// or a user value) wins over stored AI text; without one, the cached AI description is shown with its
/// provenance. Reading, copying and clearing never contact a provider, and only an explicit
/// Generate/Regenerate command runs inference.
/// </summary>
public sealed partial class DescriptionViewModel : ObservableObject
{
    private static readonly AiDescriptionProviderInfo UnavailableProvider = new(
        false, false, string.Empty, string.Empty, null, "unknown", null, "no AI description plugin is available");

    private readonly AiDescriptionService? service;
    private readonly string entryId;
    private readonly string? catalogDescription;

    public DescriptionViewModel(string? catalogDescription, AiDescriptionService? service, string entryId)
    {
        this.catalogDescription = string.IsNullOrWhiteSpace(catalogDescription) ? null : catalogDescription;
        this.service = service;
        this.entryId = entryId ?? string.Empty;
        Provider = service?.GetProviderInfo() ?? UnavailableProvider;
        Details = service is null ? AiDescriptionDetails.Unavailable(this.entryId) : service.Read(this.entryId);
        LocalizationObserver.Observe(this, static vm => vm.RefreshText());
    }

    /// <summary>Raised when the user asks to copy the description; the view supplies the clipboard.</summary>
    public event Action<string>? CopyRequested;

    [ObservableProperty] private AiDescriptionDetails details;

    [ObservableProperty] private AiDescriptionProviderInfo provider;

    [ObservableProperty] private bool isBusy;

    [ObservableProperty] private string? errorText;

    /// <summary>True when a catalog property or stored AI text supplies the description value.</summary>
    public bool HasDescription => catalogDescription is not null || Details.HasText;

    /// <summary>True when the displayed value comes from AI generation rather than catalog metadata.</summary>
    public bool IsAiSourced => catalogDescription is null && Details.HasText;

    public string DescriptionText => catalogDescription ?? Details.Text ?? string.Empty;

    /// <summary>True when the dialog can offer AI actions at all; reading a catalog value stays action-free.</summary>
    public bool HasActions => service is not null;

    /// <summary>
    /// Generation is skipped for entries with an imported or user Description so explicit AI output can
    /// never silently replace it.
    /// </summary>
    public bool CanGenerate => service is not null && catalogDescription is null && Provider.IsEnabled && !IsBusy;
    public bool CanCopy => HasDescription;
    public bool CanClear => service is not null && catalogDescription is null && Details.HasText && !IsBusy;

    public bool CanShowGenerate => service is not null && catalogDescription is null && Provider.IsPluginAvailable;
    public bool CanShowClear => service is not null && catalogDescription is null && Details.HasText;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public string GenerateButtonText => Details.HasText
        ? LocalizationService.Current.Get(UiKeys.Regenerate)
        : LocalizationService.Current.Get(UiKeys.Generate);

    /// <summary>Author or provenance line shown under the value.</summary>
    public string SourceText => catalogDescription is not null
        ? LocalizationService.Current.Get(UiKeys.DescriptionCatalogSource)
        : Details.HasText ? ProvenanceText : string.Empty;

    public string StateText => catalogDescription is not null
        ? string.Empty
        : Details.State switch
        {
            AiDescriptionState.Current => LocalizationService.Current.Get(UiKeys.AiDescriptionCurrent),
            AiDescriptionState.Stale => LocalizationService.Current.Get(UiKeys.AiDescriptionStale),
            AiDescriptionState.Failed => LocalizationService.Current.Format(
                TextKeys.AiDescriptionFailedState,
                Details.FailureReason ?? LocalizationService.Current.Get(TextKeys.Unknown)),
            _ => LocalizationService.Current.Get(UiKeys.DescriptionEmpty)
        };

    public string ProviderText => !Provider.IsPluginAvailable
        ? LocalizationService.Current.Get(UiKeys.AiDescriptionUnavailable)
        : !Provider.IsEnabled
            ? LocalizationService.Current.Format(TextKeys.AiDescriptionDisabled,
                Provider.UnavailableReason ?? LocalizationService.Current.Get(TextKeys.Unknown))
            : LocalizationService.Current.Format(TextKeys.AiDescriptionProvider,
                Provider.ProviderName,
                Provider.ProviderVersion,
                DestinationText(Provider.ProcessingDestination));

    public string ContextText => Provider.ContextOptions is null
        ? string.Empty
        : LocalizationService.Current.Format(TextKeys.AiDescriptionContext,
            DescribeContext(Provider.ContextOptions));

    public string ProvenanceText => Details.HasText
        ? LocalizationService.Current.Format(TextKeys.AiDescriptionProvenance,
            Details.Agent ?? LocalizationService.Current.Get(TextKeys.Unknown),
            Details.Model ?? LocalizationService.Current.Get(TextKeys.Unknown),
            Details.GeneratedAtUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                ?? LocalizationService.Current.Get(TextKeys.Unknown),
            Details.PluginVersion ?? LocalizationService.Current.Get(TextKeys.Unknown),
            Details.PromptVersion ?? LocalizationService.Current.Get(TextKeys.Unknown),
            Details.Language ?? LocalizationService.Current.Get(TextKeys.Unknown),
            DestinationText(Details.ProcessingDestination))
        : string.Empty;

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (!CanGenerate)
        {
            return;
        }

        IsBusy = true;
        ErrorText = null;
        try
        {
            var outcome = await service!.GenerateAsync(entryId, regenerate: Details.HasText, CancellationToken.None);
            Details = outcome.Details;
        }
        catch (Exception exception)
        {
            ErrorText = LocalizationService.Current.Format(TextKeys.AiDescriptionGenerateFailed, exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Clear()
    {
        if (!CanClear)
        {
            return;
        }

        service!.Clear(entryId);
        Details = AiDescriptionDetails.Unavailable(entryId);
        ErrorText = null;
    }

    [RelayCommand]
    private void Copy()
    {
        if (!CanCopy)
        {
            return;
        }

        CopyRequested?.Invoke(DescriptionText);
    }

    /// <summary>Re-reads the stored description; used after an external generation pass.</summary>
    public void Reload()
    {
        Provider = service?.GetProviderInfo() ?? UnavailableProvider;
        Details = service is null ? AiDescriptionDetails.Unavailable(entryId) : service.Read(entryId);
    }

    private void RefreshText()
    {
        OnPropertyChanged(nameof(GenerateButtonText));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ProviderText));
        OnPropertyChanged(nameof(ContextText));
        OnPropertyChanged(nameof(ProvenanceText));
    }

    partial void OnDetailsChanged(AiDescriptionDetails value) => NotifyDescriptionChanged();

    partial void OnProviderChanged(AiDescriptionProviderInfo value) => NotifyDescriptionChanged();

    partial void OnIsBusyChanged(bool value) => NotifyDescriptionChanged();

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private void NotifyDescriptionChanged()
    {
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(IsAiSourced));
        OnPropertyChanged(nameof(DescriptionText));
        OnPropertyChanged(nameof(CanGenerate));
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(CanClear));
        OnPropertyChanged(nameof(CanShowGenerate));
        OnPropertyChanged(nameof(CanShowClear));
        OnPropertyChanged(nameof(GenerateButtonText));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ProviderText));
        OnPropertyChanged(nameof(ContextText));
        OnPropertyChanged(nameof(ProvenanceText));
    }

    private static string DestinationText(string? destination) => destination switch
    {
        "local-inference" => LocalizationService.Current.Get(UiKeys.DestinationLocalInference),
        "cloud" => LocalizationService.Current.Get(UiKeys.DestinationCloud),
        _ => LocalizationService.Current.Get(UiKeys.DestinationUnknown)
    };

    private static string DescribeContext(
        SkyCD.Plugin.Abstractions.Capabilities.Media.DescriptionContextOptions options)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (options.Filename) parts.Add(LocalizationService.Current.Get(UiKeys.ContextFilename));
        if (options.Metadata) parts.Add(LocalizationService.Current.Get(UiKeys.ContextMetadata));
        if (options.Text) parts.Add(LocalizationService.Current.Get(UiKeys.ContextText));
        if (options.Images) parts.Add(LocalizationService.Current.Get(UiKeys.ContextImages));
        if (options.Audio) parts.Add(LocalizationService.Current.Get(UiKeys.ContextAudio));
        return parts.Count == 0
            ? LocalizationService.Current.Get(UiKeys.ContextNone)
            : string.Join(", ", parts);
    }
}
