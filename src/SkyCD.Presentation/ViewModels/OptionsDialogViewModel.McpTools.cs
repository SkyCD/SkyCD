using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel
{
    private const string McpToolsCategoryId = "mcp-tools";

    /// <summary>Index of the MCP tool exposure category; it follows the AI provider category.</summary>
    private const int McpToolsCategoryIndex = AiProviderCategoryIndex + 1;

    public ObservableCollection<McpToolItemViewModel> McpTools { get; } = [];

    public ObservableCollection<McpToolItemViewModel> FilteredMcpTools { get; } = [];

    [ObservableProperty] private string mcpToolSearchText = string.Empty;

    public bool IsMcpToolsCategorySelected => SelectedBuiltInIndex == McpToolsCategoryIndex;

    public bool ShowMcpToolsSection =>
        IsMcpToolsCategorySelected &&
        IsCurrentCategoryVisibleInSearch;

    public bool ShowNoMatchingMcpTools => FilteredMcpTools.Count == 0;

    public string McpExposedSummary => LocalizationService.Current.Format(UiKeys.McpExposedCount,
        McpTools.Count(static tool => tool.IsExposed), McpTools.Count);

    private IReadOnlyList<string>? persistedExposedToolNames;
    private bool mcpToolsLoaded;

    /// <summary>Remembers the stored selection so a failed discovery cannot silently reset it on save.</summary>
    public void InitializeMcpTools(IEnumerable<string>? exposedToolNames)
    {
        persistedExposedToolNames = exposedToolNames?.ToArray();
    }

    /// <summary>
    /// Lists the discovered tools. A <c>null</c> selection starts with every tool exposed, matching the server
    /// default; otherwise only the listed names start exposed.
    /// </summary>
    public void SetMcpTools(IEnumerable<(string Name, string CommandPath)> tools,
        IEnumerable<string>? exposedToolNames)
    {
        mcpToolsLoaded = true;
        foreach (var tool in McpTools) tool.PropertyChanged -= OnMcpToolChanged;
        McpTools.Clear();

        var exposed = exposedToolNames is null
            ? null
            : new HashSet<string>(exposedToolNames, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, commandPath) in tools
                     .Where(static tool => !string.IsNullOrWhiteSpace(tool.Name))
                     .OrderBy(static tool => tool.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!seen.Add(name)) continue;
            var item = new McpToolItemViewModel(name, commandPath ?? string.Empty)
            {
                IsExposed = exposed is null || exposed.Contains(name)
            };
            item.PropertyChanged += OnMcpToolChanged;
            McpTools.Add(item);
        }

        NotifyMcpToolsLocalizationChanged();
        RefreshFilteredMcpTools();
    }

    /// <summary>
    /// Returns the selection to persist: <c>null</c> when every tool is exposed (the server then keeps exposing
    /// future tools), otherwise the exposed tool names.
    /// </summary>
    public IReadOnlyList<string>? GetExposedMcpTools()
    {
        if (!mcpToolsLoaded)
        {
            return persistedExposedToolNames;
        }

        if (McpTools.All(static tool => tool.IsExposed))
        {
            return null;
        }

        return McpTools
            .Where(static tool => tool.IsExposed)
            .Select(static tool => tool.Name)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    [RelayCommand]
    private void SelectAllMcpTools() => SetAllMcpToolsExposed(true);

    [RelayCommand]
    private void ClearMcpTools() => SetAllMcpToolsExposed(false);

    partial void OnMcpToolSearchTextChanged(string value)
    {
        RefreshFilteredMcpTools();
    }

    private void OnMcpToolChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(McpToolItemViewModel.IsExposed)) NotifyMcpToolsLocalizationChanged();
    }

    private void SetAllMcpToolsExposed(bool exposed)
    {
        foreach (var tool in McpTools) tool.IsExposed = exposed;
        NotifyMcpToolsLocalizationChanged();
    }

    private void RefreshFilteredMcpTools()
    {
        var filter = McpToolSearchText.Trim();
        IEnumerable<McpToolItemViewModel> matching = string.IsNullOrEmpty(filter)
            ? McpTools
            : McpTools.Where(tool =>
                tool.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                tool.CommandPath.Contains(filter, StringComparison.OrdinalIgnoreCase));

        FilteredMcpTools.Clear();
        foreach (var tool in matching)
        {
            FilteredMcpTools.Add(tool);
        }

        OnPropertyChanged(nameof(ShowNoMatchingMcpTools));
    }

    private void NotifyMcpToolsLocalizationChanged()
    {
        OnPropertyChanged(nameof(McpExposedSummary));
    }
}
