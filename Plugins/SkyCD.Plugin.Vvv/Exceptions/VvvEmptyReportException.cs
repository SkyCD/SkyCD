using System;

namespace SkyCD.Plugin.Vvv.Exceptions;

public sealed class VvvEmptyReportException()
    : InvalidOperationException("VVV export contains no file entries.")
{
}
