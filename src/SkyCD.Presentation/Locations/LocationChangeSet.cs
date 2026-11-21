using System.Collections.Generic;

namespace SkyCD.Presentation.Locations;

/// <summary>
/// The staged location definition edits collected by the Options dialog. Changes are applied by the host
/// only when the dialog is accepted, in the order they are listed here.
/// </summary>
public sealed class LocationChangeSet
{
    private readonly List<LocationChange> changes = [];

    public LocationChangeSet()
    {
    }

    public LocationChangeSet(IEnumerable<LocationChange> changes)
    {
        this.changes.AddRange(changes ?? []);
    }

    public IReadOnlyList<LocationChange> Changes => changes;

    public bool IsEmpty => changes.Count == 0;
}

/// <summary>One staged location definition edit, applied in list order when the Options dialog is saved.</summary>
public abstract record LocationChange
{
    private LocationChange()
    {
    }

    public sealed record Create(string Name) : LocationChange;

    public sealed record Rename(string LocationId, string OldName, string NewName) : LocationChange;

    public sealed record Merge(string SourceId, string SourceName, string TargetId) : LocationChange;

    public sealed record Delete(string LocationId, string Name) : LocationChange;
}
