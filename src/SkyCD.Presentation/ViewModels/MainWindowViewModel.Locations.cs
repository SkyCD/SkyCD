using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;

namespace SkyCD.Presentation.ViewModels;

public partial class MainWindowViewModel
{
    /// <summary>
    /// Projects the resolved location name onto entries so legacy saved searches that still carry the
    /// retired <c>Location</c> field keep matching. Location editing and display are plugin-owned.
    /// </summary>
    private IReadOnlyList<CatalogDocument> ProjectLocations(IReadOnlyList<CatalogDocument> entries)
    {
        if (locationService is null)
        {
            return entries;
        }

        var all = GetAllCatalogEntries();
        locationService.ProjectLocations(all);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in all)
        {
            if (entry.HasLocation)
            {
                names[entry.Id] = entry.Location;
            }
        }

        foreach (var entry in entries)
        {
            entry.SetLocationName(names.TryGetValue(entry.Id, out var name) ? name : null);
        }

        return entries;
    }

    /// <summary>
    /// Location picker options for the add-to-list dialog, in display order.
    /// </summary>
    public IReadOnlyList<LocationItemViewModel> GetLocationChoices() =>
        locationService is null
            ? []
            : locationService.GetLocations()
                .Select(static location => new LocationItemViewModel(location.Id, location.Name))
                .ToArray();
}
