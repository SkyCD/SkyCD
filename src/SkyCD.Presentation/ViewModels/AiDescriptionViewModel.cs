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
/// Offline-first AI description surface for the Properties dialog. Reading, copying and clearing never
/// contact a provider; only the explicit Generate/Regenerate command runs inference.
/// </summary>
public sealed partial class AiDescriptionViewModel : ObservableObject
{
    private readonly AiDescriptionService service;
    private readonly string entryId;
    private readonly Func<string?> getComment;
    private readonly Action<string> applyComment;

    public AiDescriptionViewModel(
        AiDescriptionService service,
        string entryId,
        Func<string?> getComment,
        Action<string> applyComment)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.entryId = entryId ?? throw new ArgumentNullException(nameof(entryId));
        this.getComment = getComment ?? throw new ArgumentNullException(nameof(getComment));
        this.applyComment = applyComment ?? throw new ArgumentNullException(nameof(applyComment));
        Provider = service.GetProviderInfo();
        Details = service.Read(entryId);
        LocalizationObserver.Observe(this, static vm => vm.RefreshText());
    }

    /// <summary>Raised when the user asks to copy the description; the view supplies the clipboard.</summary>
    public event Action<string>? CopyRequested;

    [ObservableProperty] private AiDescriptionDetails details;

    [ObservableProperty] private AiDescriptionProviderInfo provider;

    [ObservableProperty] private bool isBusy;

    [ObservableProperty] private string? errorText;

    [ObservableProperty] private bool isAdoptionPending;

    public bool HasDescription => Details.HasText;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public string DescriptionText => Details.Text ?? string.Empty;
    public bool CanGenerate => Provider.IsEnabled && !IsBusy;
    public bool CanClear => HasDescription && !IsBusy;
    public bool CanAdopt => HasDescription && !IsBusy;
    public bool CanCopy => HasDescription;

    public string GenerateButtonText => HasDescription
        ? LocalizationService.Current.Get(UiKeys.Regenerate)
        : LocalizationService.Current.Get(UiKeys.Generate);

    public string StateText => Details.State switch
    {
        AiDescriptionState.Current => LocalizationService.Current.Get(UiKeys.AiDescriptionCurrent),
        AiDescriptionState.Stale => LocalizationService.Current.Get(UiKeys.AiDescriptionStale),
        AiDescriptionState.Failed => LocalizationService.Current.Format(
            TextKeys.AiDescriptionFailedState,
            Details.FailureReason ?? LocalizationService.Current.Get(TextKeys.Unknown)),
        _ => LocalizationService.Current.Get(UiKeys.AiDescriptionEmpty)
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

    public string AdoptionHint => IsAdoptionPending
        ? LocalizationService.Current.Get(TextKeys.AiDescriptionAdoptConfirmation)
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
            var outcome = await service.GenerateAsync(entryId, regenerate: HasDescription, CancellationToken.None);
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

        service.Clear(entryId);
        Details = AiDescriptionDetails.Unavailable(entryId);
        ErrorText = null;
        IsAdoptionPending = false;
    }

    /// <summary>
    /// Copies the description into the user's comment editor. A non-empty comment is never replaced
    /// silently: the first invocation asks for confirmation and only the second one applies the text.
    /// The stored AI description itself is never changed by adoption.
    /// </summary>
    [RelayCommand]
    private void Adopt()
    {
        if (!CanAdopt)
        {
            return;
        }

        var current = getComment();
        if (!string.IsNullOrWhiteSpace(current) && !IsAdoptionPending)
        {
            IsAdoptionPending = true;
            return;
        }

        applyComment(DescriptionText);
        IsAdoptionPending = false;
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
        Provider = service.GetProviderInfo();
        Details = service.Read(entryId);
    }

    private void RefreshText()
    {
        OnPropertyChanged(nameof(GenerateButtonText));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ProviderText));
        OnPropertyChanged(nameof(ContextText));
        OnPropertyChanged(nameof(ProvenanceText));
        OnPropertyChanged(nameof(AdoptionHint));
    }

    partial void OnDetailsChanged(AiDescriptionDetails value) => NotifyDescriptionChanged();

    partial void OnProviderChanged(AiDescriptionProviderInfo value) => NotifyDescriptionChanged();

    partial void OnIsBusyChanged(bool value) => NotifyDescriptionChanged();

    partial void OnIsAdoptionPendingChanged(bool value) => OnPropertyChanged(nameof(AdoptionHint));

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private void NotifyDescriptionChanged()
    {
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(DescriptionText));
        OnPropertyChanged(nameof(CanGenerate));
        OnPropertyChanged(nameof(CanClear));
        OnPropertyChanged(nameof(CanAdopt));
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(GenerateButtonText));
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
