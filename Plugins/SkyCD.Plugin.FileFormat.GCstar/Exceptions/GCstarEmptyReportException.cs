using System;

namespace SkyCD.Plugin.FileFormat.GCstar.Exceptions;

public sealed class GCstarEmptyReportException()
    : InvalidOperationException("GCstar collection export contains no catalog entries.")
{
}
