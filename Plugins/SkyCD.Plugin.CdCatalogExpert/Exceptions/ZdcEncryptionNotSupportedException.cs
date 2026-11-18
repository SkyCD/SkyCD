using System;

namespace SkyCD.Plugin.CdCatalogExpert.Exceptions;

/// <summary>
/// Raised when a container or member is encrypted. Decryption is outside the
/// initial ZDC scope; encrypted data is never silently emitted as corrupt data.
/// </summary>
public sealed class ZdcEncryptionNotSupportedException(string detail)
    : InvalidOperationException($"CD Catalog Expert catalog is encrypted and cannot be imported: {detail}");
