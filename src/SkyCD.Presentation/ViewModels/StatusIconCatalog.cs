using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Media;

namespace SkyCD.Presentation.ViewModels;

public static class StatusIconCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, object>> KindsByKey = new(BuildKindsMap);

    public static IReadOnlyList<string> GetAllKeys()
    {
        return KindsByKey.Value.Keys
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool TryResolveKind(string? key, out object? kind)
    {
        kind = null;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return KindsByKey.Value.TryGetValue(key.Trim(), out kind);
    }

    public static IBrush ResolveBrush(string? color)
    {
        if (Color.TryParse(color, out var parsed))
        {
            return new SolidColorBrush(parsed);
        }

        return Brushes.White;
    }

    private static IReadOnlyDictionary<string, object> BuildKindsMap()
    {
        TryLoadAssembly("IconPacks.Avalonia");
        var kinds = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
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
                    kinds.TryAdd(key, rawValue!);
                }
            }
        }

        return kinds;
    }

    private static void TryLoadAssembly(string assemblyName)
    {
        try
        {
            Assembly.Load(assemblyName);
        }
        catch
        {
            // Keep running with whatever icon packs are already loaded.
        }
    }
}
