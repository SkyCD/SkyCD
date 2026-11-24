using System.IO;

namespace SkyCD.Plugin.Feature.Split.Exceptions;

internal sealed class LegacyCatalogWriteException(string path, string error)
    : IOException($"The part file '{path}' could not be written: {error}.");
