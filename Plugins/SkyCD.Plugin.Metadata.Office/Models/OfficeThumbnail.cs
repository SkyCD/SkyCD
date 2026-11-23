namespace SkyCD.Plugin.Metadata.Office.Models;

/// <summary>A thumbnail embedded in a document, already bounded and ready to publish as a preview artifact.</summary>
internal sealed record OfficeThumbnail(string ContentType, byte[] Data);
