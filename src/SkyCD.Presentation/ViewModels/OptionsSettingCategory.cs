namespace SkyCD.Presentation.ViewModels;

/// <summary>Stable identity is independent of translated titles, list order and duplicate plugin titles.</summary>
public sealed record OptionsSettingCategory(
    string Id,
    string Title,
    int BuiltInIndex = -1,
    OptionsPluginPage? PluginPage = null,
    string? Group = null,
    OptionsContributedPage? ContributedPage = null);
