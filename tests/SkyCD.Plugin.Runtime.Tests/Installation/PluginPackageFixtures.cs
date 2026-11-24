using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests.Installation;

/// <summary>
/// Builds signing-free .nupkg zips from assemblies already present in the test output. Plugin assemblies are read
/// from disk and copied into the package; nothing is loaded or executed.
/// </summary>
internal sealed record PluginFixture(string PackageId, string Version, string AssemblyFile,
    string EntryAssemblyFileName);

internal sealed class PluginPackageFixtures
{
    public const string DefaultTargetFramework = "net10.0";

    public PluginFixture CreateFreshPlugin(string pluginId, string version, string assemblyFile = "Slice.dll") =>
        new(Slug(pluginId), version, assemblyFile, Path.GetFileName(assemblyFile));

    /// <summary>
    /// Builds a package whose nuspec and lib content describe the same assembly. The package file name may differ
    /// from the assembly name because NuGet installation renames files; identity comes from assembly metadata.
    /// </summary>
    public byte[] Build(PluginFixture fixture, string? nuspecId = null, string? nuspecVersion = null,
        string? entryAssembly = null, Action<ZipArchive>? mutate = null,
        IReadOnlyDictionary<string, byte[]>? extraEntries = null,
        IReadOnlyDictionary<string, byte[]>? overrides = null)
    {
        var payload = ReadAssembly(fixture.AssemblyFile);
        // A real, discovery-excluded assembly proves bundled private dependencies travel with the entry.
        var privateDependency = ReadAssembly("SkyCD.SDK.dll");
        var entryName = entryAssembly ?? fixture.EntryAssemblyFileName;
        var nuspec = BuildNuspec(nuspecId ?? fixture.PackageId, nuspecVersion ?? fixture.Version, entryName);
        var privateName = "SkyCD.Plugin.SlipSlice.Private.dll";
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, $"{fixture.PackageId}.nuspec", nuspec);
            WriteEntry(archive, "[Content_Types].xml",
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />");
            WriteEntry(archive, "package/services/metadata/core-properties/props.psmdcp", "<coreProperties />");
            WriteEntry(archive, "build/SkyCD.Plugin.SlipSlice.targets", "<Project />");
            WriteEntry(archive, $"lib/{DefaultTargetFramework}/{entryName}",
                ResolveContent(overrides, $"lib/{DefaultTargetFramework}/{entryName}", payload));
            WriteEntry(archive, $"lib/{DefaultTargetFramework}/{privateName}",
                ResolveContent(overrides, $"lib/{DefaultTargetFramework}/{privateName}", privateDependency));
            // Metadata-only version marker: proves which package content is active without loading the assembly.
            WriteEntry(archive, $"lib/{DefaultTargetFramework}/package-version.txt", fixture.Version);
            foreach (var (name, content) in extraEntries ?? new Dictionary<string, byte[]>())
            {
                WriteEntry(archive, name, content);
            }

            mutate?.Invoke(archive);
        }

        return buffer.ToArray();
    }

    public byte[] BuildTarGz(string pluginId, string version)
    {
        // Valid gzip, not a zip container.
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes($"{pluginId} {version}"));
        }

        return buffer.ToArray();
    }

    public static void WriteEntry(ZipArchive archive, string name, string content) =>
        WriteEntry(archive, name, Encoding.UTF8.GetBytes(content));

    public static void WriteEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static byte[] ResolveContent(IReadOnlyDictionary<string, byte[]>? overrides, string name,
        byte[] fallback) => overrides is not null && overrides.TryGetValue(name, out var content) ? content : fallback;

    private static string BuildNuspec(string packageId, string version, string? entryAssembly) =>
        $"""
         <?xml version="1.0" encoding="utf-8"?>
         <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
           <metadata>
             <id>{packageId}</id>
             <version>{version}</version>
             <authors>SkyCD tests</authors>
             <description>Test plugin package for staged install integration tests.</description>
           </metadata>
           {(entryAssembly is null ? string.Empty : $"""
             <files>
               <file src="lib/{DefaultTargetFramework}/{entryAssembly}" target="" />
             </files>
             """)}
         </package>
         """;

    private static byte[] ReadAssembly(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        var bytes = File.ReadAllBytes(path);
        var assemblyName = AssemblyName.GetAssemblyName(path);
        Assert.True(bytes.Length > 0, $"Fixture assembly {assemblyName.FullName} must not be empty.");
        return bytes;
    }

    private static string Slug(string pluginId) => new string(
        pluginId.Select(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' ? character : '.').ToArray());
}
