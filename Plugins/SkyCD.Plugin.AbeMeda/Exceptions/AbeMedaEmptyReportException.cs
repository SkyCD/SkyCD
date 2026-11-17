using System;

namespace SkyCD.Plugin.AbeMeda.Exceptions;

public sealed class AbeMedaEmptyReportException()
    : InvalidOperationException("abeMeda XML export contains no catalog entries.")
{
}
