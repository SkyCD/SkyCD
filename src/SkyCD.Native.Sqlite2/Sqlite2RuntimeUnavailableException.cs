using System;

namespace SkyCD.Native.Sqlite2;

public sealed class Sqlite2RuntimeUnavailableException(string runtime) : InvalidOperationException(
    $"The bundled SQLite 2 native library for '{runtime}' is missing or cannot be loaded. Reinstall the matching SkyCD distribution.");
