namespace SkyCD.Plugin.Metadata.OpenOffice.Models;

/// <summary>A thumbnail embedded in a package, already bounded and ready to publish as a preview artifact.</summary>
internal sealed record OpenOfficeThumbnail(string ContentType, byte[] Data);
