using System;

namespace SkyCD.Plugin.FileFormat.Vvv.Exceptions;

public sealed class VvvEmptyReportException()
    : InvalidOperationException("VVV export contains no file entries.")
{
}
