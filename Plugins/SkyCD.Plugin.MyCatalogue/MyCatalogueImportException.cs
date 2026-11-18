using System;

namespace SkyCD.Plugin.MyCatalogue;

public sealed class MyCatalogueImportException(string reason)
    : InvalidOperationException($"Invalid or unsupported MyCatalogue catalog: {reason}")
{
}
