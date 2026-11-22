using System;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Exceptions;

public sealed class AbeMedaEmptyReportException()
    : InvalidOperationException("abeMeda XML export contains no catalog entries.")
{
}
