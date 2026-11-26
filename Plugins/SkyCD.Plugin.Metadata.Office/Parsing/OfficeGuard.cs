using System;
using System.Collections.Generic;
using System.IO;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Keeps a failure inside one library value from costing the whole record.
/// </summary>
/// <remarks>
/// The library builds its projections lazily, so a getter can raise for a part this document happens to store
/// badly while every other part is readable. Only the ordinary data faults are contained here: a cancellation,
/// an out-of-memory condition or a protection refusal must still reach the caller, because each of those
/// changes what the plugin publishes rather than merely which field is absent.
/// </remarks>
internal static class OfficeGuard
{
    public static T? Try<T>(Func<T> read)
        where T : class?
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (Contained(exception))
        {
            return null;
        }
    }

    public static T? TryValue<T>(Func<T?> read)
        where T : struct
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (Contained(exception))
        {
            return null;
        }
    }

    public static int TryCount(Func<int> read, int fallback = 0)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (Contained(exception))
        {
            return fallback;
        }
    }

    public static bool TryBool(Func<bool> read, bool fallback = false)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (Contained(exception))
        {
            return fallback;
        }
    }

    private static bool Contained(Exception exception) => exception is InvalidOperationException
        or InvalidCastException
        or NotSupportedException
        or KeyNotFoundException
        or FormatException
        or OverflowException
        or InvalidDataException;
}
