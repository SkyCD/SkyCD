using System.Xml.Linq;

namespace SkyCD.Plugin.Metadata.OpenOffice.Models;

/// <summary>
/// XML namespaces used by OpenDocument 1.2 and later and by the OpenOffice.org 1.0 lineage, which shares the
/// <c>http://openoffice.org/2000/</c> namespace family with OpenDocument 1.0 and 1.1.
/// </summary>
internal static class OpenOfficeNamespaces
{
    public const string OdfOffice = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    public const string LegacyOffice = "http://openoffice.org/2000/office";
    public const string OdfMeta = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
    public const string LegacyMeta = "http://openoffice.org/2000/meta";
    public const string DublinCore = "http://purl.org/dc/elements/1.1/";
    public const string OdfManifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
    public const string LegacyManifest = "http://openoffice.org/2000/manifest";

    public static bool IsOffice(XName name) => name.NamespaceName is OdfOffice or LegacyOffice;

    public static bool IsMeta(XName name) => name.NamespaceName is OdfMeta or LegacyMeta;

    public static bool IsDublinCore(XName name) => name.NamespaceName is DublinCore;

    public static bool IsManifest(string namespaceName, string localName) =>
        namespaceName is OdfManifest or LegacyManifest &&
        localName is "encryption-data";
}
