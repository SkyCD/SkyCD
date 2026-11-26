using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Feature.GenDesc.Exceptions;
using SkyCD.Plugin.Feature.GenDesc.Localization;
using SkyCD.Plugin.Feature.GenDesc.Models;
using SkyCD.Plugin.Feature.GenDesc.Parsing;
using SkyCD.Plugin.Feature.GenDesc.Rendering;
using SkyCD.Plugin.Feature.GenDesc.ViewModels;
using SkyCD.Plugin.Feature.GenDesc.Views;
using SkyCD.SDK.Capabilities.GenDesc;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.SDK.Capabilities.Options;
using SkyCD.SDK.Capabilities.Ui;
using SkyCD.SDK.Localization;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.GenDesc;

/// <summary>
/// Generates .nfo files and torrent-site descriptions from editable templates. The two File-menu commands are
/// enabled only for one selected local file; facts come from the host's catalog entry and metadata cache.
/// </summary>
public sealed class GenDescPlugin : IMenuPluginCapability, IGenDescHostConsumer, IHostDialogConsumer,
    IHostFileDialogConsumer, IClipboardConsumer, IOptionsPageCapability, ILocalizationPluginCapability
{
    private const string NfoCommandId = "skycd.gendesc.nfo";
    private const string DescriptionCommandId = "skycd.gendesc.description";
    private const string FileMenuLocation = "File";
    private const string OptionsPageId = "gendesc-templates";

    private IGenDescHostApi? genDescHost;
    private IHostDialogApi? dialogApi;
    private IHostFileDialogApi? fileDialogs;
    private IClipboardApi? clipboard;

    /// <summary>Overrides the user template directory in tests; production uses the shared default.</summary>
    internal string? TemplateDirectory { get; set; }

    /// <inheritdoc />
    public void InitializeGenDescHost(IGenDescHostApi genDescHost) =>
        this.genDescHost = genDescHost ?? throw new ArgumentNullException(nameof(genDescHost));

    /// <inheritdoc />
    public void InitializeDialogApi(IHostDialogApi dialogApi) =>
        this.dialogApi = dialogApi ?? throw new ArgumentNullException(nameof(dialogApi));

    /// <inheritdoc />
    public void InitializeFileDialogApi(IHostFileDialogApi fileDialogApi) =>
        fileDialogs = fileDialogApi ?? throw new ArgumentNullException(nameof(fileDialogApi));

    /// <inheritdoc />
    public void InitializeClipboard(IClipboardApi clipboard) =>
        this.clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));

    /// <inheritdoc />
    public IReadOnlyCollection<MenuContribution> GetMenuContributions() =>
    [
        new(NfoCommandId, LocalizationService.Current.Get(GenDescKeys.GenerateNfo), FileMenuLocation, 10, true),
        new(DescriptionCommandId, LocalizationService.Current.Get(GenDescKeys.GenerateDescription),
            FileMenuLocation, 20, true)
    ];

    /// <inheritdoc />
    public async Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(commandId, NfoCommandId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(commandId, DescriptionCommandId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var entryId = context.Properties is not null &&
                      context.Properties.TryGetValue("EntryId", out var selectedId) &&
                      !string.IsNullOrWhiteSpace(selectedId)
            ? selectedId
            : null;
        if (entryId is null)
        {
            await NotifyAsync(context, LocalizationService.Current.Get(GenDescKeys.NoSelection), cancellationToken);
            return;
        }

        if (genDescHost is null)
        {
            await NotifyAsync(context, LocalizationService.Current.Get(GenDescKeys.HostUnavailable), cancellationToken);
            return;
        }

        var entry = await genDescHost.GetEntryContextAsync(entryId, cancellationToken);
        if (entry is null)
        {
            await NotifyAsync(context, LocalizationService.Current.Get(GenDescKeys.FileUnavailable), cancellationToken);
            return;
        }

        var kind = string.Equals(commandId, NfoCommandId, StringComparison.OrdinalIgnoreCase)
            ? TemplateInfo.NfoKind
            : TemplateInfo.DescriptionKind;
        GenDescResult result;
        try
        {
            result = GenDescRenderer.Render(GenDescTemplatePaths.LoadAndSeed(TemplateDirectory), entry, kind);
        }
        catch (Exception exception) when (exception is GenDescGenerationException or TemplateSyntaxException
                                              or IOException or UnauthorizedAccessException)
        {
            await NotifyAsync(context, LocalizationService.Current.Format(GenDescKeys.GenerationFailed,
                exception.Message), cancellationToken);
            return;
        }

        if (kind == TemplateInfo.NfoKind)
        {
            await SaveNfoAsync(context, entry, result, cancellationToken);
        }
        else
        {
            await ShowDescriptionAsync(entry, result);
        }
    }

    /// <inheritdoc />
    public OptionsPageDescriptor OptionsPage =>
        new(OptionsPageId, LocalizationService.Current.Get(GenDescKeys.OptionsTitle), 50);

    /// <inheritdoc />
    public object CreateOptionsPage() =>
        new GenDescTemplatesView { DataContext = new GenDescTemplatesViewModel(fileDialogs, TemplateDirectory) };

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetTranslations() => GenDescLocalization.Translations;

    private async Task SaveNfoAsync(MenuCommandContext context, GenDescEntryContext entry, GenDescResult result,
        CancellationToken cancellationToken)
    {
        if (fileDialogs is null)
        {
            await NotifyAsync(context, LocalizationService.Current.Get(GenDescKeys.HostUnavailable), cancellationToken);
            return;
        }

        var suggestedName = Path.GetFileNameWithoutExtension(entry.FileName) + ".nfo";
        var target = await fileDialogs.ShowSaveAsync(new SaveFileRequest(
            LocalizationService.Current.Get(GenDescKeys.SaveNfoTitle),
            suggestedName,
            ".nfo",
            [new FileDialogFilter("NFO", ["*.nfo"])]));
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(target, result.Text, new UTF8Encoding(false), cancellationToken);
            await NotifyAsync(context, LocalizationService.Current.Format(GenDescKeys.NfoSaved, target,
                result.Template.Id), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await NotifyAsync(context, LocalizationService.Current.Format(GenDescKeys.GenerationFailed,
                exception.Message), cancellationToken);
        }
    }

    private async Task ShowDescriptionAsync(GenDescEntryContext entry, GenDescResult result)
    {
        if (dialogApi is null)
        {
            return;
        }

        var title = LocalizationService.Current.Get(GenDescKeys.DescriptionTitle) + " - " + entry.FileName;
        await dialogApi.ShowAsync(new HostWindowRequest(title, 640, 520, () =>
            new DescriptionDialogView
            {
                DataContext = new DescriptionDialogViewModel(title, result.Text, clipboard)
            }));
    }

    private static Task NotifyAsync(MenuCommandContext context, string message, CancellationToken cancellationToken) =>
        context.HostApi?.NotifyAsync(message, cancellationToken) ?? Task.CompletedTask;
}
