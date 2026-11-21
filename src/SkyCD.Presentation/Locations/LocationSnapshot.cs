namespace SkyCD.Presentation.Locations;

/// <summary>
/// One stored location as the Options dialog sees it when it opens: the stable id, the current name and
/// the number of catalog entries effectively using it (own assignment or inherited from an ancestor).
/// </summary>
public sealed record LocationSnapshot(string Id, string Name, int UsageCount);
