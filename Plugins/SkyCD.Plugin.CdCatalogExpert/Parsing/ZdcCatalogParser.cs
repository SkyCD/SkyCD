using System;
using System.Collections.Generic;
using System.Threading;
using SkyCD.Plugin.CdCatalogExpert.Exceptions;
using SkyCD.Plugin.CdCatalogExpert.Models;

namespace SkyCD.Plugin.CdCatalogExpert.Parsing;

/// <summary>Parsed catalog plus structured warnings collected while reading.</summary>
public sealed record ZdcCatalogReadResult(ZdcCatalog Catalog, IReadOnlyList<string> Warnings);

/// <summary>
/// Resolves <c>zdc_index.xml</c>, disc tree XML, and optional cover members
/// from a parsed container into the catalog model.
/// </summary>
public static class ZdcCatalogParser
{
    public static ZdcCatalogReadResult Read(ZdcDatabase database, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);

        var membersByName = new Dictionary<string, ZdcMember>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in database.Members)
        {
            membersByName[member.FileName] = member;
        }

        if (!membersByName.TryGetValue(ZdcFormat.IndexMemberName, out var indexMember))
        {
            throw new ZdcFormatException($"required index member '{ZdcFormat.IndexMemberName}' is missing.");
        }

        if (indexMember.Payload.Length == 0)
        {
            throw new ZdcFormatException($"required index member '{ZdcFormat.IndexMemberName}' is empty.");
        }

        var warnings = new List<string>();
        var catalog = ZdcCatalogXmlReader.ReadIndex(
            indexMember.FileName,
            indexMember.Payload,
            warnings,
            cancellationToken);

        var claimedMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            indexMember.FileName
        };
        WalkDiscs(catalog.Children, disc => AttachDiscMembers(disc, membersByName, claimedMembers, warnings, cancellationToken));

        return new ZdcCatalogReadResult(catalog, warnings);
    }

    private static void AttachDiscMembers(
        ZdcDisc disc,
        Dictionary<string, ZdcMember> members,
        HashSet<string> claimedMembers,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var treeName = ResolveTreeMemberName(disc.GuidText, members)
            ?? throw new ZdcFormatException(
                $"disc '{disc.Name}' references missing disc-tree member '{CanonicalTreeFileName(disc.GuidText)}'.");
        if (!claimedMembers.Add(treeName))
        {
            throw new ZdcFormatException($"disc-tree member '{treeName}' is referenced more than once.");
        }

        disc.Tree = ZdcCatalogXmlReader.ReadDiscTree(
            treeName,
            members[treeName].Payload,
            warnings,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(disc.CoverMemberName))
        {
            return;
        }

        if (!members.TryGetValue(disc.CoverMemberName, out var coverMember))
        {
            warnings.Add($"Disc '{disc.Name}' references missing cover '{disc.CoverMemberName}'; the disc was kept.");
            return;
        }

        if (!IsJpeg(coverMember.Payload))
        {
            warnings.Add($"Disc '{disc.Name}' cover '{disc.CoverMemberName}' is not a JPEG image; it was ignored.");
            return;
        }

        disc.Cover = coverMember.Payload;
        claimedMembers.Add(coverMember.FileName);
    }

    private static string? ResolveTreeMemberName(
        string guidText,
        Dictionary<string, ZdcMember> members)
    {
        foreach (var candidate in TreeMemberCandidates(guidText))
        {
            if (members.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> TreeMemberCandidates(string guidText)
    {
        var trimmed = guidText.Trim();
        var bare = trimmed.StartsWith('{') && trimmed.EndsWith('}')
            ? trimmed[1..^1]
            : trimmed;

        yield return trimmed.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? trimmed : $"{trimmed}.xml";
        yield return $"{bare}.xml";
        yield return $"{{{bare}}}.xml";
    }

    private static string CanonicalTreeFileName(string guidText)
    {
        var trimmed = guidText.Trim();
        return trimmed.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? trimmed : $"{trimmed}.xml";
    }

    private static bool IsJpeg(byte[] payload) =>
        payload.Length >= 4 &&
        payload[0] == 0xFF && payload[1] == 0xD8 &&
        payload[^2] == 0xFF && payload[^1] == 0xD9;

    private static void WalkDiscs(IEnumerable<ZdcCatalogEntry> entries, Action<ZdcDisc> visit)
    {
        foreach (var entry in entries)
        {
            switch (entry)
            {
                case ZdcGroup group:
                    WalkDiscs(group.Children, visit);
                    break;
                case ZdcDisc disc:
                    visit(disc);
                    break;
                default:
                    throw new ZdcFormatException($"unsupported catalog entry '{entry.GetType().Name}'.");
            }
        }
    }
}
