using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Feature.Split.Localization;
using SkyCD.Plugin.Feature.Split.Models;
using SkyCD.Plugin.Feature.Split.Parsing;
using SkyCD.Plugin.Feature.Split.ViewModels;
using SkyCD.Plugin.Feature.Split.Views;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.SDK.Capabilities.Ui;
using SkyCD.SDK.Localization;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.Split;

/// <summary>
/// Splits the open catalog into numbered part catalogs. The File-menu command exists only while the plugin is
/// loaded and needs an open catalog plus a loaded format that can write and read back the parts. Each part is
/// written through the host save surface, so the parts use the same staging write and data-loss warning as Save
/// As, and a cancelled or failed run leaves the source catalog and the output folder untouched.
/// </summary>
public sealed class SplitPlugin : IMenuPluginCapability, ICatalogApiConsumer, ICatalogSaveHostConsumer,
    IHostDialogConsumer, ILocalizationPluginCapability
{
    /// <summary>The File-menu command id. It is persisted menu state, so it must not change.</summary>
    public const string SplitCommandId = "skycd.split";

    private const string FileMenuLocation = "File";
    private const int FileMenuOrder = 140;

    private ICatalogApi? catalogApi;
    private ICatalogSaveHostApi? saveHost;
    private IHostDialogApi? dialogApi;

    /// <inheritdoc />
    public void InitializeCatalogApi(ICatalogApi api) =>
        catalogApi = api ?? throw new ArgumentNullException(nameof(api));

    /// <inheritdoc />
    public void InitializeCatalogSaveHost(ICatalogSaveHostApi hostApi) =>
        saveHost = hostApi ?? throw new ArgumentNullException(nameof(hostApi));

    /// <inheritdoc />
    public void InitializeDialogApi(IHostDialogApi dialogApi) =>
        this.dialogApi = dialogApi ?? throw new ArgumentNullException(nameof(dialogApi));

    /// <inheritdoc />
    public IReadOnlyCollection<MenuContribution> GetMenuContributions() =>
    [
        new(SplitCommandId, LocalizationService.Current.Get(SplitKeys.MenuTitle), FileMenuLocation,
            FileMenuOrder, RequiresWritableFormat: true)
    ];

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetTranslations() => SplitLocalization.Translations;

    /// <inheritdoc />
    public async Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(commandId, SplitCommandId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var sourcePath = catalogApi?.OpenedCatalogPath;
        if (catalogApi is null || string.IsNullOrWhiteSpace(sourcePath))
        {
            await NotifyAsync(context, LocalizationService.Current.Get(SplitKeys.NoOpenCatalog),
                cancellationToken);
            return;
        }

        if (saveHost is null || dialogApi is null)
        {
            await NotifyAsync(context, LocalizationService.Current.Get(SplitKeys.HostUnavailable),
                cancellationToken);
            return;
        }

        var entries = await catalogApi.GetEntriesAsync(cancellationToken).ConfigureAwait(false);
        if (entries.Count == 0)
        {
            await NotifyAsync(context, LocalizationService.Current.Get(SplitKeys.NoEntries),
                cancellationToken);
            return;
        }

        var formats = saveHost.GetRewritableFormats()
            .Select(SplitFormatOption.FromDescriptor)
            .ToArray();
        if (formats.Length == 0)
        {
            await NotifyAsync(context, LocalizationService.Current.Get(SplitKeys.NoWritableFormat),
                cancellationToken);
            return;
        }

        var viewModel = new SplitDialogViewModel(
            Path.GetFileName(sourcePath),
            entries.Count,
            Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? string.Empty,
            formats,
            saveHost.ResolveRewritableFormatId(sourcePath),
            (request, progress, token) => new SplitPlanner(saveHost).SplitAsync(
                entries, request, preview => ConfirmDataLossAsync(preview), progress, token));

        await dialogApi.ShowAsync(new HostWindowRequest(
            LocalizationService.Current.Get(SplitKeys.DialogTitle),
            620,
            560,
            () => new SplitEditor { DataContext = viewModel }));

        if (viewModel.OutcomeMessage is { Length: > 0 } outcome)
        {
            await NotifyAsync(context, outcome, cancellationToken);
        }
    }

    private async Task<bool> ConfirmDataLossAsync(SplitDataLossPreview preview)
    {
        if (dialogApi is null)
        {
            return false;
        }

        var localization = LocalizationService.Current;
        var result = await dialogApi.AskAsync(new QuestionRequest(
            localization.Get(UiKeys.SaveDataLoss),
            localization.Format(UiKeys.SaveDataLossIntro, preview.FormatDisplayName),
            QuestionButtons.SaveAnywayCancel,
            CatalogDataLossText.BuildLines(preview.Items))).ConfigureAwait(false);
        return result == QuestionResult.Save;
    }

    private static Task NotifyAsync(MenuCommandContext context, string message,
        CancellationToken cancellationToken) =>
        context.HostApi?.NotifyAsync(message, cancellationToken) ?? Task.CompletedTask;
}
