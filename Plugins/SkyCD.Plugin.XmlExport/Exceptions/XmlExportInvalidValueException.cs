namespace SkyCD.Plugin.XmlExport.Exceptions;

/// <summary>
/// Reported when a value cannot be represented in the XML export shape
/// (for example a negative size, an unparseable timestamp, or an unsafe
/// stylesheet reference). Carries the exporting item, field, and reason.
/// </summary>
public sealed class XmlExportInvalidValueException(string item, string field, string reason)
    : XmlExportException($"Item '{item}' has field '{field}' that cannot be exported: {reason}")
{
}
