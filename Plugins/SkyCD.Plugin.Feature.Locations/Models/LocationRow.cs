using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.Locations.Models;

/// <summary>One stored location with the number of entries whose effective location it is.</summary>
public sealed record LocationRow(string Id, string Name, int UsageCount)
{
    public string UsageText => LocalizationService.Current.Format(LocationsKeys.UsageCount, UsageCount);
}
