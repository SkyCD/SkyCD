using System;

namespace SkyCD.Plugin.Access;

public sealed class AccessExportProviderUnavailableException() : InvalidOperationException(
    "Access export requires Windows with ADOX and a Jet or ACE OLE DB provider matching the process architecture. Install the Access Database Engine runtime; Microsoft Access is not required.");
