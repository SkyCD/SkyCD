using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using SkyCD.SDK.Capabilities;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.SDK.Capabilities.Catalog.Locations;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.SDK.Localization;
using SkyCD.Plugin.Feature.Locations.Models;
using SkyCD.Plugin.Feature.Locations.Views;
using SkyCD.UI.Localization;
using YamlDotNet.Serialization;

namespace SkyCD.Plugin.Feature.Locations;

/// <summary>
/// First-party feature plugin that owns the storage-location UI. Location documents stay in the catalog and
/// the host keeps storage and inheritance resolution internal behind <see cref="ILocationApi"/>. Management
/// lives in the host's Options dialog as a contributed page rather than a floating window.
/// </summary>
public sealed class LocationsPlugin : IPluginCapability, ILocationApiConsumer, ICatalogFeaturePluginCapability,
    IPluginConfigurationCapability, ILocalizationPluginCapability
{
    private const string OptionsPageId = "locations";
    private const string ColumnKey = "location";

    private ILocationApi? locationApi;

    /// <inheritdoc />
    public event EventHandler? DataChanged;

    /// <inheritdoc />
    public void InitializeLocationApi(ILocationApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (locationApi is not null)
        {
            locationApi.Changed -= OnLocationsChanged;
        }

        locationApi = api;
        api.Changed += OnLocationsChanged;
    }

    /// <inheritdoc />
    public IReadOnlyList<PluginConfigurationDescriptor> Configurations =>
        [new(OptionsPageId, LocalizationService.Current.Get(LocationsKeys.Locations), Order: -50)];

    /// <inheritdoc />
    public Task<PluginConfigurationPage> CreatePageAsync(PluginConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Offers(request.ConfigurationId))
        {
            return Task.FromResult(new PluginConfigurationPage(
                () => new TextBlock
                {
                    Text = $"Unsupported configuration '{request.ConfigurationId}'.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                _ => Task.FromResult(new PluginConfigurationResult
                {
                    Success = false,
                    Error = $"Unsupported configuration '{request.ConfigurationId}'."
                })));
        }

        var api = locationApi
            ?? throw new InvalidOperationException("The locations page requires the host location API.");
        var view = new LocationsView { DataContext = new LocationsManagerViewModel(api, RaiseDataChanged) };
        return Task.FromResult(new PluginConfigurationPage(
            () => view,
            token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new PluginConfigurationResult
                {
                    Success = true,
                    Settings = request.CurrentSettings
                });
            }));
    }

    private bool Offers(string configurationId) =>
        Configurations.Any(descriptor =>
            descriptor.ConfigurationId.Equals(configurationId, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public IReadOnlyList<EntryDecoration> GetEntryDecorations(IReadOnlyList<CatalogEntry> entries) => [];

    /// <inheritdoc />
    public IReadOnlyList<DetailsColumnContribution> GetDetailsColumns() =>
    [
        new(
            ColumnKey,
            LocalizationService.Current.Get(LocationsKeys.LocationColumn),
            56,
            entry => locationApi?.GetEffectiveLocationName(entry.Id))
    ];

    /// <inheritdoc />
    public IReadOnlyList<AdvancedSearchFieldContribution> GetAdvancedSearchFields()
    {
        string? Value(CatalogEntry entry) => locationApi?.GetEffectiveLocationName(entry.Id);
        return
        [
            new(
                ColumnKey,
                LocalizationService.Current.Get(LocationsKeys.LocationColumn),
                entry => Value(entry) is not null,
                entry => Value(entry))
        ];
    }

    /// <inheritdoc />
    public IReadOnlyList<EntryCommandContribution> GetEntryCommands() => [];

    /// <inheritdoc />
    public Task<bool> ExecuteEntryCommandAsync(string commandId, CatalogEntry entry,
        CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetTranslations() => Translations;

    private void OnLocationsChanged(object? sender, EventArgs args) => DataChanged?.Invoke(this, EventArgs.Empty);

    private void RaiseDataChanged() => DataChanged?.Invoke(this, EventArgs.Empty);

    private static readonly Dictionary<string, string> English = new()
    {
        ["ui.Locations"] = "Locations",
        ["ui.ManageHint"] = "Locations are stored in the catalog. An entry without its own location inherits the location of its parent.",
        ["ui.LocationColumn"] = "Location",
        ["ui.NewLocation"] = "New location name",
        ["ui.Create"] = "Create",
        ["ui.LocationName"] = "Location name",
        ["ui.Rename"] = "Rename",
        ["ui.Delete"] = "Delete",
        ["ui.MergeInto"] = "Merge into",
        ["ui.Merge"] = "Merge",
        ["ui.MergeLocations"] = "Merge locations",
        ["ui.MergeSummary"] = "Merge {0} into the selected location. Its {1} entries move with it.",
        ["ui.Cancel"] = "Cancel",
        ["ui.UsageCount"] = "{0} item(s)",
        ["ui.UsageColumn"] = "Usage",
        ["ui.AddLocationTooltip"] = "Add location",
        ["ui.DeleteLocationTooltip"] = "Delete selected location",
        ["ui.LocationNameRequired"] = "Enter a location name.",
        ["ui.LocationNameDuplicate"] = "Another location already uses the name \"{0}\".",
        ["ui.LocationNotFound"] = "The selected location no longer exists.",
        ["ui.CatalogUnavailable"] = "Open a catalog before managing locations.",
        ["ui.LocationCreated"] = "Location \"{0}\" created.",
        ["ui.LocationRenamed"] = "Location renamed to \"{0}\".",
        ["ui.LocationDeleted"] = "Location \"{0}\" deleted.",
        ["ui.LocationDeletedInUse"] = "Location \"{0}\" deleted; assignments for {1} item(s) were cleared.",
        ["ui.LocationMerged"] = "Location \"{0}\" merged into \"{1}\"."
    };

    private static readonly Dictionary<string, string> Lithuanian = new()
    {
        ["ui.Locations"] = "Vietos",
        ["ui.ManageHint"] = "Vietos saugomos kataloge. Elementas be savo vietos paveldi tėvinio elemento vietą.",
        ["ui.LocationColumn"] = "Vieta",
        ["ui.NewLocation"] = "Naujos vietos pavadinimas",
        ["ui.Create"] = "Sukurti",
        ["ui.LocationName"] = "Vietos pavadinimas",
        ["ui.Rename"] = "Pervadinti",
        ["ui.Delete"] = "Pašalinti",
        ["ui.MergeInto"] = "Sujungti su",
        ["ui.Merge"] = "Sujungti",
        ["ui.MergeLocations"] = "Sujungti vietas",
        ["ui.MergeSummary"] = "Sujungti {0} su pasirinkta vieta. Kartu bus perkelti {1} įrašai.",
        ["ui.Cancel"] = "Atšaukti",
        ["ui.UsageCount"] = "Elementų: {0}",
        ["ui.UsageColumn"] = "Naudojimas",
        ["ui.AddLocationTooltip"] = "Pridėti vietą",
        ["ui.DeleteLocationTooltip"] = "Pašalinti pasirinktą vietą",
        ["ui.LocationNameRequired"] = "Įveskite vietos pavadinimą.",
        ["ui.LocationNameDuplicate"] = "Kita vieta jau naudoja pavadinimą „{0}“.",
        ["ui.LocationNotFound"] = "Pasirinktos vietos kataloge jau nėra.",
        ["ui.CatalogUnavailable"] = "Prieš tvarkydami vietas atverkite katalogą.",
        ["ui.LocationCreated"] = "Vieta „{0}“ sukurta.",
        ["ui.LocationRenamed"] = "Vieta pervadinta į „{0}“.",
        ["ui.LocationDeleted"] = "Vieta „{0}“ pašalinta.",
        ["ui.LocationDeletedInUse"] = "Vieta „{0}“ pašalinta; {1} elementų priskyrimai išvalyti.",
        ["ui.LocationMerged"] = "Vieta „{0}“ sujungta su „{1}“."
    };

    private static readonly IReadOnlyDictionary<string, string> Translations = new Dictionary<string, string>
    {
        ["en"] = Serialize(English),
        ["lt"] = Serialize(Lithuanian)
    };

    private static string Serialize(Dictionary<string, string> values) =>
        new SerializerBuilder().Build().Serialize(values);
}
