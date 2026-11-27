using SkyCD.Plugin.Feature.GenDesc.Localization;
using SkyCD.Plugin.Feature.GenDesc.Models;
using SkyCD.Plugin.Feature.GenDesc.ViewModels;
using SkyCD.Plugin.Feature.GenDesc.Views;
using SkyCD.SDK.Capabilities.Options;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.GenDesc;

/// <summary>
/// Options page editing the templates that produce .nfo info files. It shares the "GenDesc templates" sidebar
/// group with the post-description page and offers no kind filter of its own.
/// </summary>
public sealed class GenDescInfoFilesOptionsPage : IOptionsPageCapability
{
    private const string OptionsPageId = "gendesc-info-files";
    private const int OptionsPageOrder = 50;

    /// <summary>Overrides the user template directory in tests; production uses the shared default.</summary>
    internal string? TemplateDirectory { get; set; }

    /// <inheritdoc />
    public OptionsPageDescriptor OptionsPage => new(OptionsPageId,
        LocalizationService.Current.Get(GenDescKeys.InfoFilesTitle), OptionsPageOrder,
        Group: LocalizationService.Current.Get(GenDescKeys.OptionsGroup));

    /// <inheritdoc />
    public object CreateOptionsPage() =>
        new GenDescTemplatesView { DataContext = CreateTemplatesViewModel() };

    internal GenDescTemplatesViewModel CreateTemplatesViewModel() =>
        new(TemplateInfo.NfoKind, TemplateDirectory);
}
