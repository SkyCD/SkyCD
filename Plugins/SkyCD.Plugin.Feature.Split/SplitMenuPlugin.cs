using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.SDK.Capabilities.Ui;
using SkyCD.Plugin.Feature.Split.Exceptions;
using SkyCD.Plugin.Feature.Split.Models;
using SkyCD.Plugin.Feature.Split.Parsing;
using SkyCD.Plugin.Feature.Split.Views;

namespace SkyCD.Plugin.Feature.Split;

/// <summary>
/// Contributes the catalog split command: it reads the open legacy text catalog, collects the part limits in a
/// host dialog and writes the parts at entry boundaries.
/// </summary>
public sealed class SplitMenuPlugin : IMenuPluginCapability, ICatalogApiConsumer, IHostDialogConsumer
{
    /// <summary>The identifier of the contributed split command.</summary>
    public const string CommandId = "skycd.split";

    private const string MenuTitle = "Split Catalog...";
    private const string DialogTitle = "Split Catalog";
    private const string Requirement = "Split Catalog needs a legacy SkyCD text (.scd) catalog.";
    private const int MenuOrder = 140;
    private ICatalogApi? catalogApi;
    private IHostDialogApi? dialogApi;

    /// <inheritdoc />
    public void InitializeCatalogApi(ICatalogApi api) => catalogApi = api;

    /// <inheritdoc />
    public void InitializeDialogApi(IHostDialogApi api) => dialogApi = api;

    /// <inheritdoc />
    public IReadOnlyCollection<MenuContribution> GetMenuContributions() =>
        [new MenuContribution(CommandId, MenuTitle, "Tools", MenuOrder)];

    /// <inheritdoc />
    public async Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
        CancellationToken cancellationToken = default)
    {
        if (!commandId.Equals(CommandId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (catalogApi?.OpenedCatalogPath is not { Length: > 0 } sourcePath)
        {
            await NotifyAsync(context, $"{Requirement} Open one to split it.", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!string.Equals(Path.GetExtension(sourcePath), ".scd", StringComparison.OrdinalIgnoreCase))
        {
            await NotifyAsync(context,
                    $"{Requirement} The open catalog '{Path.GetFileName(sourcePath)}' is not one.",
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (dialogApi is not { } dialogs)
        {
            await NotifyAsync(context, "Split needs a host dialog window, which this host does not provide.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var catalog = await ReadAsync(context, sourcePath, cancellationToken).ConfigureAwait(false);
        if (catalog is null)
        {
            return;
        }

        var viewModel = new SplitDialogViewModel(sourcePath, catalog);
        await dialogs.ShowAsync(new HostWindowRequest(DialogTitle, 620, 460,
            () => new SplitEditor { DataContext = viewModel })).ConfigureAwait(false);
        if (viewModel.OutcomeMessage is { } outcome)
        {
            await NotifyAsync(context, outcome, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<LegacyTextCatalog?> ReadAsync(MenuCommandContext context, string sourcePath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await LegacyTextCatalogReader.ReadAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NotLegacyTextCatalogException exception)
        {
            await NotifyAsync(context,
                    $"{Requirement} '{Path.GetFileName(sourcePath)}' cannot be split: {exception.Reason}.",
                    cancellationToken)
                .ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            await NotifyAsync(context, $"Split is unavailable for '{sourcePath}': {exception.Message}",
                cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    private static Task NotifyAsync(MenuCommandContext context, string message, CancellationToken cancellationToken) =>
        context.HostApi?.NotifyAsync(message, cancellationToken) ?? Task.CompletedTask;
}
