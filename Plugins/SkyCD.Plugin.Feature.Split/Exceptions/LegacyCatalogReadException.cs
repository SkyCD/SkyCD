using System.IO;

namespace SkyCD.Plugin.Feature.Split.Exceptions;

internal sealed class LegacyCatalogReadException(string error)
    : IOException($"The legacy text catalog could not be read: {error}.");
