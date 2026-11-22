using System;

namespace SkyCD.Plugin.FileFormat.SmartCDCatalogPro.Exceptions;

public sealed class SmartCdCatalogProInvalidFormatException(string reason)
    : InvalidOperationException($"Not a Smart CD Catalog PRO .scdc catalog: {reason}")
{
}
