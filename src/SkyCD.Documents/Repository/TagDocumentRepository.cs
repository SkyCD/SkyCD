using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SkyCD.Couchbase.Repository;

namespace SkyCD.Documents.Repository;

public sealed class TagDocumentRepository : RepositoryBase<TagDocument>
{
    public IReadOnlyList<TagDocument> CreateDefaultEntries()
    {
        return [];
    }

    public IReadOnlyList<TagDocument> GetOrdered()
    {
        return GetAll()
            .Where(static tag => !string.IsNullOrWhiteSpace(tag.Name))
            .OrderBy(static tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public TagDocument? FindByName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        return GetAll().FirstOrDefault(tag =>
            string.Equals(tag.Name?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public TagDocument CreateTag(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new TagNameRequiredException();
        }

        var trimmed = name.Trim();
        if (FindByName(trimmed) is not null)
        {
            throw new TagDuplicateNameException(trimmed);
        }

        var tag = new TagDocument { Id = BuildTagId(trimmed, GetAll()), Name = trimmed };
        Save(tag.Id, tag);
        return tag;
    }

    internal static string BuildTagId(string name, IEnumerable<TagDocument> existing)
    {
        var slug = new StringBuilder("tag-");
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
            candidate = "tag-untagged";
        }

        var taken = new HashSet<string>(
            existing.Select(static tag => tag.Id),
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
