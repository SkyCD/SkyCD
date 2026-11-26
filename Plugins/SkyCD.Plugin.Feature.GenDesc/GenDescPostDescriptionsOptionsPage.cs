using System;
using SkyCD.Plugin.Feature.GenDesc.Localization;
using SkyCD.Plugin.Feature.GenDesc.Models;
using SkyCD.Plugin.Feature.GenDesc.ViewModels;
using SkyCD.Plugin.Feature.GenDesc.Views;
using SkyCD.SDK.Capabilities.Options;
using SkyCD.SDK.Capabilities.Ui;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.GenDesc;

/// <summary>
/// Options page editing the templates that produce torrent-site post descriptions. It shares the
/// "GenDesc templates" sidebar group with the info-files page and offers no kind filter of its own.
/// </summary>
public sealed class GenDescPostDescriptionsOptionsPage : IOptionsPageCapability, IHostFileDialogConsumer
{
    private const string OptionsPageId = "gendesc-post-descriptions";
    private const int OptionsPageOrder = 51;

    private IHostFileDialogApi? fileDialogs;

    /// <summary>Overrides the user template directory in tests; production uses the shared default.</summary>
    internal string? TemplateDirectory { get; set; }

    /// <inheritdoc />
    public void InitializeFileDialogApi(IHostFileDialogApi fileDialogApi) =>
        fileDialogs = fileDialogApi ?? throw new ArgumentNullException(nameof(fileDialogApi));

    /// <inheritdoc />
    public OptionsPageDescriptor OptionsPage => new(OptionsPageId,
        LocalizationService.Current.Get(GenDescKeys.PostDescriptionsTitle), OptionsPageOrder,
        Group: LocalizationService.Current.Get(GenDescKeys.OptionsGroup));

    /// <inheritdoc />
    public object CreateOptionsPage() =>
        new GenDescTemplatesView { DataContext = CreateTemplatesViewModel() };

    internal GenDescTemplatesViewModel CreateTemplatesViewModel() =>
        new(TemplateInfo.DescriptionKind, fileDialogs, TemplateDirectory);
}
