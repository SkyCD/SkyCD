using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.SDK;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel
{
    private readonly Dictionary<string, OptionsContributedPage> contributedPages = new(StringComparer.OrdinalIgnoreCase);

    public OptionsContributedPage? SelectedContributedPage => CurrentCategory.ContributedPage;

    public bool ShowContributedOptionsPageSection =>
        SelectedContributedPage is not null && IsCurrentCategoryVisibleInSearch;

    /// <summary>
    /// Registers the pages the loaded plugins contribute. Pages already present keep their wrapper so an
    /// Options session that rescans plugins never recreates or loses embedded state.
    /// </summary>
    public void SetPluginOptionsPages(IEnumerable<OptionsPageProvider> providers)
    {
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            OptionsContributedPage page;
            try
            {
                page = new OptionsContributedPage(provider);
            }
            catch (Exception exception)
            {
                PluginFailure.Publish(provider.Capability.GetType(), "options", exception);
                continue;
            }

            available.Add(page.Id);
            contributedPages.TryAdd(page.Id, page);
        }

        foreach (var key in contributedPages.Keys.Where(key => !available.Contains(key)).ToArray())
        {
            contributedPages[key].Dispose();
            contributedPages.Remove(key);
        }

        RefreshCategoriesPreservingSelection();
        OnPropertyChanged(nameof(SelectedContributedPage));
        OnPropertyChanged(nameof(ShowContributedOptionsPageSection));
    }
}
