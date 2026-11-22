using System;

namespace SkyCD.Plugin.Metadata.Dmg.Exceptions;

/// <summary>
/// Raised when the image is an encrypted (AES-encrypted) DMG, which cannot be read without a password.
/// </summary>
public sealed class DmgEncryptedImageException()
    : NotSupportedException("The DMG image is encrypted and cannot be read without a password.");
