using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SkyCD.Plugin.Runtime.Installation;

/// <summary>
/// One PEReader-based reader for plugin identity attributes (issue #1228). The installer
/// (<c>PluginAssemblyMetadataInspector</c>), the discovery factory and the assembly scan all
/// resolve raw attribute values here instead of each decoding metadata or matching attribute
/// names on their own. Attribute types match on their full name, so a same-named attribute
/// from another namespace is never mistaken for an SDK lifecycle attribute.
/// </summary>
public sealed record PluginAssemblyAttributes(
    string AssemblyName,
    Version AssemblyVersion,
    string? PluginId,
    string? MinHostVersion,
    string? MaxHostVersion)
{
    private const string LifecycleNamespace = "SkyCD.SDK.Lifecycle";
    private const string PluginIdAttributeName = LifecycleNamespace + ".PluginIdAttribute";
    private const string MinHostVersionAttributeName = LifecycleNamespace + ".MinHostVersionAttribute";
    private const string MaxHostVersionAttributeName = LifecycleNamespace + ".MaxHostVersionAttribute";

    /// <summary>The SDK assembly name; the scan skips it by name instead of by file name.</summary>
    public const string SdkAssemblyName = "SkyCD.SDK";

    /// <summary>True when the assembly name is the SDK itself (case-insensitive).</summary>
    public static bool IsSdkAssemblyName(string? assemblyName) =>
        string.Equals(assemblyName?.Trim(), SdkAssemblyName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads identity attributes from a managed assembly file without loading it.
    /// Returns <c>null</c> for files without managed metadata; throws
    /// <see cref="BadImageFormatException"/> for native files and <see cref="IOException"/>
    /// for unreadable files, so callers keep their own skip-or-fail contract.
    /// </summary>
    public static PluginAssemblyAttributes? TryRead(string fullPath)
    {
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            return null;
        }

        var reader = peReader.GetMetadataReader();
        if (!reader.IsAssembly)
        {
            return null;
        }

        var definition = reader.GetAssemblyDefinition();
        var assemblyName = reader.GetString(definition.Name);
        Version assemblyVersion;
        try
        {
            assemblyVersion = definition.Version;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        string? pluginId = null;
        string? minHostVersion = null;
        string? maxHostVersion = null;

        foreach (var handle in definition.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            var attributeName = ResolveAttributeTypeName(reader, attribute);
            if (attributeName is null)
            {
                continue;
            }

            var argument = TryReadFirstStringArgument(reader, attribute);
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            var value = argument.Trim();
            if (attributeName.Equals(PluginIdAttributeName, StringComparison.Ordinal))
            {
                pluginId = value;
            }
            else if (attributeName.Equals(MinHostVersionAttributeName, StringComparison.Ordinal))
            {
                minHostVersion = value;
            }
            else if (attributeName.Equals(MaxHostVersionAttributeName, StringComparison.Ordinal))
            {
                maxHostVersion = value;
            }
        }

        return new PluginAssemblyAttributes(assemblyName, assemblyVersion, pluginId, minHostVersion, maxHostVersion);
    }

    private static string? ResolveAttributeTypeName(MetadataReader reader, CustomAttribute attribute)
    {
        try
        {
            var constructor = attribute.Constructor;
            EntityHandle typeHandle = constructor.Kind switch
            {
                HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
                HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor)
                    .GetDeclaringType(),
                _ => default
            };

            StringHandle namespaceHandle;
            StringHandle nameHandle;
            switch (typeHandle.Kind)
            {
                case HandleKind.TypeReference:
                    var typeReference = reader.GetTypeReference((TypeReferenceHandle)typeHandle);
                    namespaceHandle = typeReference.Namespace;
                    nameHandle = typeReference.Name;
                    break;
                case HandleKind.TypeDefinition:
                    var typeDefinition = reader.GetTypeDefinition((TypeDefinitionHandle)typeHandle);
                    namespaceHandle = typeDefinition.Namespace;
                    nameHandle = typeDefinition.Name;
                    break;
                default:
                    return null;
            }

            var name = reader.GetString(nameHandle);
            var @namespace = namespaceHandle.IsNil ? string.Empty : reader.GetString(namespaceHandle);
            return string.IsNullOrEmpty(@namespace) ? name : @namespace + "." + name;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static string? TryReadFirstStringArgument(MetadataReader reader, CustomAttribute attribute)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference)
        {
            return null;
        }

        var blob = reader.GetBlobReader(attribute.Value);
        if (blob.Length < 2 || blob.ReadUInt16() != 1)
        {
            return null;
        }

        try
        {
            return blob.ReadSerializedString();
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }
}
