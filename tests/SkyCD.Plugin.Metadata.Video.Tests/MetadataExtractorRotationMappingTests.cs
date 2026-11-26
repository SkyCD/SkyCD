using System;
using SkyCD.Plugin.Metadata.Video.Parsing;
using Xunit;

namespace SkyCD.Plugin.Metadata.Video.Tests;

/// <summary>
/// The MetadataExtractor track-rotation mapping (issue #1221): counter-clockwise degrees become
/// the clockwise convention the catalog publishes, axis-aligned values pass through and
/// everything else (including a missing value) reads as upright.
/// </summary>
public class MetadataExtractorRotationMappingTests
{
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(-90.0, 90)]
    [InlineData(-180.0, 180)]
    [InlineData(90.0, 270)]
    [InlineData(360.0, 0)]
    public void MapCounterClockwiseRotation_MapsAxisAlignedValues(double counterClockwise, int expected)
    {
        Assert.Equal(expected, Mp4VideoReader.MapCounterClockwiseRotation(counterClockwise));
    }

    [Theory]
    [InlineData(45.0)]
    [InlineData(-45.0)]
    [InlineData(30.0)]
    public void MapCounterClockwiseRotation_ReportsNonAxisAlignedAsUpright(double counterClockwise)
    {
        Assert.Equal(0, Mp4VideoReader.MapCounterClockwiseRotation(counterClockwise));
    }

    [Fact]
    public void MapCounterClockwiseRotation_MissingValueReadsAsUpright()
    {
        Assert.Equal(0, Mp4VideoReader.MapCounterClockwiseRotation(null));
        Assert.Equal(0, Mp4VideoReader.MapCounterClockwiseRotation("not-a-number"));
    }
}
