using System.Collections.Generic;

namespace SkyCD.Documents;

/// <summary>A saved button layout for one plugin toolbar.</summary>
public sealed class PluginToolbarLayoutDocument
{
    public string ToolbarId { get; set; } = string.Empty;

    public List<ToolbarItemDocument> Items { get; set; } = [];
}
