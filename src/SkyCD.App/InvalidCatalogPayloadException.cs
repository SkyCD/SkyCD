using System;

namespace SkyCD.App;

internal sealed class InvalidCatalogPayloadException()
    : InvalidOperationException("The file format plugin returned an unsupported catalog payload.");
