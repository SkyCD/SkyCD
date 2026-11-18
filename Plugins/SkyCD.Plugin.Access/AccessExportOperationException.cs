using System;

namespace SkyCD.Plugin.Access;

public sealed class AccessExportOperationException(string operation, Exception innerException)
    : InvalidOperationException($"Access export failed while {operation}: {innerException.Message}", innerException);
