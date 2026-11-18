using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.BrokenX.Exceptions;

namespace SkyCD.Plugin.BrokenX;

/// <summary>
/// Selects records for partial export: the selected roots plus all descendants.
/// </summary>
public static class BxdSubtreeSelector
{
    /// <summary>
    /// Filters flat node rows to the selected node IDs and their descendants, preserving row order.
    /// </summary>
    public static IReadOnlyList<Dictionary<string, object?>> Select(
        IReadOnlyList<Dictionary<string, object?>> rows,
        IReadOnlyCollection<string> selectedNodeIds)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(selectedNodeIds);

        if (selectedNodeIds.Count == 0)
        {
            throw new BxdSelectionException("no records were selected.");
        }

        var byId = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var nodeId = row.TryGetValue("nodeId", out var idValue) ? idValue?.ToString() : null;
            if (string.IsNullOrWhiteSpace(nodeId) || !byId.TryAdd(nodeId, row))
            {
                continue;
            }

            var parentId = row.TryGetValue("parentId", out var parentValue) ? parentValue?.ToString() : null;
            if (!string.IsNullOrWhiteSpace(parentId))
            {
                if (!children.TryGetValue(parentId, out var siblings))
                {
                    siblings = [];
                    children[parentId] = siblings;
                }

                siblings.Add(nodeId);
            }
        }

        var unknown = selectedNodeIds.Where(id => !byId.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new BxdSelectionException($"unknown record IDs: {string.Join(", ", unknown)}.");
        }

        var selected = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>(selectedNodeIds);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!selected.Add(current))
            {
                continue;
            }

            if (children.TryGetValue(current, out var direct))
            {
                foreach (var child in direct)
                {
                    stack.Push(child);
                }
            }
        }

        return rows.Where(row =>
                row.TryGetValue("nodeId", out var idValue) &&
                idValue?.ToString() is { } nodeId &&
                selected.Contains(nodeId)).ToList();
    }
}
