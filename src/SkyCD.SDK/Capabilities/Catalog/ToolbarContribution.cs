using System.Collections.Generic;

namespace SkyCD.SDK.Capabilities.Catalog;

/// <summary>A switchable toolbar whose buttons reference existing entry commands.</summary>
public sealed record ToolbarContribution(string Id, string Title, int Order, IReadOnlyList<ToolbarButtonContribution> Buttons);
