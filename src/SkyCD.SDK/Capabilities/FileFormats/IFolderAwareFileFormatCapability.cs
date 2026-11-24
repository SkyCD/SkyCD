namespace SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

/// <summary>
/// Capability contract for file format plugins that inspect directory contents to determine format compatibility.
/// </summary>
public interface IFolderAwareFileFormatCapability : IPluginCapability
{
    /// <summary>
    /// Inspects folder contents to determine if the folder matches this format.
    /// </summary>
    bool CanHandleFolder(string folderPath);
}
