using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SkyCD.Couchbase.Repository;

namespace SkyCD.Documents.Repository;

public sealed class LocationDocumentRepository : RepositoryBase<LocationDocument>
{
    public IReadOnlyList<LocationDocument> GetOrdered()
    {
        return GetAll()
            .Where(static location => !string.IsNullOrWhiteSpace(location.Name))
            .OrderBy(static location => location.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public LocationDocument? FindByName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        return GetAll().FirstOrDefault(location =>
            string.Equals(location.Name?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public LocationDocument CreateLocation(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new LocationNameRequiredException();
        }

        var trimmed = name.Trim();
        if (FindByName(trimmed) is not null)
        {
            throw new LocationDuplicateNameException(trimmed);
        }

        var location = new LocationDocument { Id = BuildLocationId(trimmed, GetAll()), Name = trimmed };
        Save(location.Id, location);
        return location;
    }

    internal static string BuildLocationId(string name, IEnumerable<LocationDocument> existing)
    {
        var slug = new StringBuilder("loc-");
        foreach (var character in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                slug.Append(character);
            }
            else if (slug.Length > 4 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var candidate = slug.ToString().TrimEnd('-');
        if (candidate.Length <= 4)
        {
            candidate = "loc-unassigned";
        }

        var taken = new HashSet<string>(
            existing.Select(static location => location.Id),
            StringComparer.Ordinal);
        if (!taken.Contains(candidate))
        {
            return candidate;
        }

        var suffix = 2;
        while (taken.Contains($"{candidate}-{suffix}"))
        {
            suffix++;
        }

        return $"{candidate}-{suffix}";
    }
}
