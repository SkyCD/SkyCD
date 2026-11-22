using System;

namespace SkyCD.Plugin.FileFormat.Vvv.Exceptions;

public sealed class VvvBackupEngineUnavailableException(string searchedLocations)
    : InvalidOperationException(
        $"VVV backup import requires the Firebird 5 embedded engine. Install it or set SKYCD_FIREBIRD_ENGINE. Searched: {searchedLocations}")
{
}
