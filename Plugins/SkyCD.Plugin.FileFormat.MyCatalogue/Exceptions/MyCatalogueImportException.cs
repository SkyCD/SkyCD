using System;

namespace SkyCD.Plugin.FileFormat.MyCatalogue.Exceptions;

public sealed class MyCatalogueImportException(string reason)
    : InvalidOperationException($"Invalid or unsupported MyCatalogue catalog: {reason}")
{
}
