using System;

namespace SkyCD.Plugin.Tellico.Exceptions;

public sealed class TellicoEmptyCollectionException()
    : InvalidOperationException("Tellico collection contains no entries.")
{
}
