using System;
using System.IO;
using System.Reflection;
using TextMateSharp.Grammars;

namespace SkyCD.Plugin.Feature.GenDesc.Views;

/// <summary>
/// Loads the GenDesc TextMate grammar and theme for the read-only template preview. The grammar ships as an
/// embedded resource and is extracted to a cache file because TextMate loads custom grammars from paths.
/// </summary>
internal static class GenDescTemplateHighlighting
{
    internal const string ScopeName = "source.gendesc";

    private const string ResourceName = "SkyCD.Plugin.Feature.GenDesc.Assets.GenDesc.tmGrammar.json";

    internal static string EnsureGrammarFile()
    {
        var cache = Path.Combine(Path.GetTempPath(), "SkyCD", "GenDesc.tmGrammar.json");
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded grammar '{ResourceName}' was not found.");
        using var memory = new MemoryStream();
        source.CopyTo(memory);
        var bytes = memory.ToArray();
        if (File.Exists(cache) && File.ReadAllBytes(cache).AsSpan().SequenceEqual(bytes))
        {
            return cache;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.WriteAllBytes(cache, bytes);
        return cache;
    }

    internal static RegistryOptions CreateRegistryOptions(bool dark) =>
        new(dark ? ThemeName.DarkPlus : ThemeName.LightPlus);
}
