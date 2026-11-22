using System;
using System.Globalization;

namespace SkyCD.Plugin.Metadata.DiskImages.Dmg.Exceptions;

/// <summary>
/// Raised when a DMG image is one segment of a segmented set, which this plugin does not join.
/// </summary>
public sealed class DmgSegmentedImageException : NotSupportedException
{
    private const string Template =
        "The DMG image is segment {1} of {0}; segmented images are not supported.";

    /// <summary>
    /// Initializes a new instance naming the segment count and this segment's position.
    /// </summary>
    public DmgSegmentedImageException(int segmentCount, int segmentNumber)
        : base(string.Format(CultureInfo.InvariantCulture, Template, segmentCount, segmentNumber))
    {
    }
}
