using System;

namespace SkyCD.Plugin.MyCatalogue.Exceptions;

public sealed class MyCatalogueImportException(string reason)
    : InvalidOperationException($"Invalid or unsupported MyCatalogue catalog: {reason}")
{
}
