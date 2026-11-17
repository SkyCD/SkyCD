using System;

namespace SkyCD.Plugin.WhereIsIt.Exceptions;

public sealed class WhereIsItEmptyReportException()
    : InvalidOperationException("Where Is It? XML report contains no catalog entries.")
{
}
