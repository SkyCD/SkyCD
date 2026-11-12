using System.IO;

namespace SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

/// <summary>
/// Capability contract for file format plugins that inspect stream content to determine format compatibility.
/// </summary>
public interface IContentAwareFileFormatCapability : IPluginCapability
{
    /// <summary>
    /// Inspects stream content to determine if the stream matches this format.
    /// Implementations must restore the stream position before returning.
    /// </summary>
    bool CanHandleContent(Stream stream, string? fileName = null);
}
