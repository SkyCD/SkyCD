namespace SkyCD.Plugin.FileFormat.XmlExport.Exceptions;

/// <summary>
/// Reported when a value contains a character that XML 1.0 cannot represent.
/// Carries the exporting item and field so the offending value can be found.
/// </summary>
public sealed class XmlExportInvalidCharacterException(string item, string field)
    : XmlExportException(
        $"Item '{item}' has field '{field}' containing a character that XML 1.0 cannot represent. Remove or replace the character and retry.")
{
}
