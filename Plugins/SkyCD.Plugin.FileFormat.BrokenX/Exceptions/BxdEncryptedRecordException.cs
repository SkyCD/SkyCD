using System;

namespace SkyCD.Plugin.FileFormat.BrokenX.Exceptions;

/// <summary>
/// Returned when a Broken X record is password-protected (encrypted).
/// </summary>
/// <remarks>
/// Decryption is not implemented. The record is reported instead of being silently
/// omitted or rewritten, so no protected data is lost.
/// </remarks>
public sealed class BxdEncryptedRecordException(string itemName)
    : InvalidOperationException($"Broken X record '{itemName}' is password-protected. Decryption is not supported; the record was left untouched.");
