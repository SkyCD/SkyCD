using System;

namespace SkyCD.Plugin.FileFormat.Tellico.Exceptions;

public sealed class TellicoEmptyCollectionException()
    : InvalidOperationException("Tellico collection contains no entries.")
{
}
