namespace SkyCD.Plugin.EncodingProfiles;

/// <summary>
/// Single gateway to the legacy Windows code pages shared by the plugins.
/// </summary>
/// <remarks>
/// The static constructor registers <see cref="System.Text.CodePagesEncodingProvider"/> once per process, so
/// every plugin that reads a legacy ANSI or OEM code page depends on this type instead of registering the
/// provider itself. <see cref="Windows1252"/> is the lenient ANSI encoding (replacement fallbacks) and
/// <see cref="StrictWindows1252"/> is the strict ANSI encoding that fails on characters it cannot represent.
/// Both properties are built inside the static constructor after the registration, because field and
/// property initializers run before the constructor body.
/// </remarks>
internal static class LegacyEncoding
{
    static LegacyEncoding()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        Windows1252 = Get(1252);
        StrictWindows1252 = GetStrict(1252);
    }

    /// <summary>Returns the code page with the default replacement fallbacks.</summary>
    internal static System.Text.Encoding Get(int codePage) => System.Text.Encoding.GetEncoding(codePage);

    /// <summary>Returns the code page named by <paramref name="name"/> with the default replacement fallbacks.</summary>
    internal static System.Text.Encoding Get(string name) => System.Text.Encoding.GetEncoding(name);

    /// <summary>Returns the code page with exception fallbacks for encoding and decoding.</summary>
    internal static System.Text.Encoding GetStrict(int codePage) =>
        System.Text.Encoding.GetEncoding(codePage, System.Text.EncoderFallback.ExceptionFallback,
            System.Text.DecoderFallback.ExceptionFallback);

    /// <summary>Returns the encoding named by <paramref name="name"/> with exception fallbacks for encoding and decoding.</summary>
    internal static System.Text.Encoding GetStrict(string name) =>
        System.Text.Encoding.GetEncoding(name, System.Text.EncoderFallback.ExceptionFallback,
            System.Text.DecoderFallback.ExceptionFallback);

    /// <summary>Lenient Windows-1252 (ANSI) encoding that replaces unrepresentable characters.</summary>
    internal static System.Text.Encoding Windows1252 { get; }

    /// <summary>Strict Windows-1252 (ANSI) encoding that throws on unrepresentable characters.</summary>
    internal static System.Text.Encoding StrictWindows1252 { get; }
}
