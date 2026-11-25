using System.IO;

namespace SkyCD.Plugin.Feature.Split.Exceptions;

internal sealed class NotLegacyTextCatalogException(string reason)
    : IOException($"The selected catalog is not a legacy SkyCD text catalog: {reason}.")
{
    /// <summary>Gets the specific reason the catalog cannot be split as legacy text.</summary>
    public string Reason { get; } = reason;
}
