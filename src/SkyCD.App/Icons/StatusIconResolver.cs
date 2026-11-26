using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Media;

namespace SkyCD.App.Icons;

/// <summary>
/// Resolves status-variant icon pack keys to their enum kinds and color strings to brushes
/// (issue #1226). The pack-enum reflection used to live in Presentation view models; it now runs
/// once here, where Avalonia and IconPacks are declared dependencies, and view models only pass
/// the pack/key and color strings around.
/// </summary>
public static class StatusIconResolver
{
    private static readonly Lazy<IReadOnlyDictionary<string, Enum>> KindsByKey = new(BuildKindsMap);
    private static readonly Lazy<IReadOnlyList<string>> Keys = new(() =>
        Array.AsReadOnly(KindsByKey.Value.Keys
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray()));

    /// <summary>Seam for tests: replace the reflected pack map entirely.</summary>
    internal static Func<IReadOnlyDictionary<string, Enum>>? KindsMapOverride { get; set; }

    public static IReadOnlyList<string> GetAllKeys() => KindsMapOverride is { } mapOverride
        ? Array.AsReadOnly(mapOverride().Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase).ToArray())
        : Keys.Value;

    public static bool TryResolveKind(string? key, out Enum? kind)
    {
        kind = null;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var map = KindsMapOverride is { } mapOverride ? mapOverride() : KindsByKey.Value;
        if (map.TryGetValue(key.Trim(), out var resolved))
        {
            kind = resolved;
            return true;
        }

        return false;
    }

    public static IBrush ResolveBrush(string? color)
    {
        if (Color.TryParse(color, out var parsed))
        {
            return new SolidColorBrush(parsed);
        }

        return Brushes.White;
    }

    private static IReadOnlyDictionary<string, Enum> BuildKindsMap()
    {
        TryLoadAssembly("IconPacks.Avalonia");
        var kinds = new Dictionary<string, Enum>(StringComparer.OrdinalIgnoreCase);
        var assemblies = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(static assembly =>
            {
                var name = assembly.GetName().Name;
                return name is not null && name.StartsWith("IconPacks.Avalonia", StringComparison.Ordinal);
            })
            .ToArray();

        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(static type => type is not null).Cast<Type>().ToArray();
            }

            foreach (var enumType in types.Where(static type => type.IsEnum && type.Name.StartsWith("PackIcon", StringComparison.Ordinal) && type.Name.EndsWith("Kind", StringComparison.Ordinal)))
            {
                var packName = enumType.Name["PackIcon".Length..^"Kind".Length];
                foreach (var rawValue in Enum.GetValues(enumType))
                {
                    var valueName = Enum.GetName(enumType, rawValue);
                    if (string.IsNullOrWhiteSpace(valueName))
                    {
                        continue;
                    }

                    var key = $"{packName}:{valueName}";
                    if (rawValue is Enum kind)
                    {
                        kinds.TryAdd(key, kind);
                    }
                }
            }
        }

        return kinds;
    }

    private static void TryLoadAssembly(string assemblyName)
    {
        try
        {
            var assembly = Assembly.Load(assemblyName);
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name is { } name && name.StartsWith("IconPacks.Avalonia.", StringComparison.Ordinal))
                {
                    TryLoadAssembly(name);
                }
            }
        }
        catch
        {
            // Keep running with whatever icon packs are already loaded.
        }
    }
}
