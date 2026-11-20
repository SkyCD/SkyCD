using Xunit;

namespace SkyCD.Plugin.FileMetadata.Tests;

/// <summary>
/// The macOS readers are untestable on other systems, but the plist decoding they depend on is pure; these
/// fixtures write the same <c>bplist00</c> layout Finder produces.
/// </summary>
public sealed class BinaryPlistReaderTests
{
    [Fact]
    public void StringArray_IsReadAndFinderColourSuffixIsStripped()
    {
        var data = BinaryPlistFixtures.ArrayOfStrings("Red\n6", "Favourite\n0");

        Assert.Equal(["Red", "Favourite"], BinaryPlistReader.ReadStrings(data));
    }

    [Fact]
    public void Utf16String_IsDecoded()
    {
        var data = BinaryPlistFixtures.SingleString("Caf\u00e9 cr\u00e8me", utf16: true);

        Assert.Equal("Caf\u00e9 cr\u00e8me", BinaryPlistReader.ReadString(data));
    }

    [Fact]
    public void AsciiSingleString_IsDecoded()
    {
        var data = BinaryPlistFixtures.SingleString("Plain comment");

        Assert.Equal("Plain comment", BinaryPlistReader.ReadString(data));
    }

    [Fact]
    public void ExtendedStringLengths_AreDecoded()
    {
        foreach (var value in new[] { "A comment of sixteen", new string('x', 300) })
        {
            Assert.Equal(value, BinaryPlistReader.ReadString(BinaryPlistFixtures.SingleString(value)));
            Assert.Equal(value, BinaryPlistReader.ReadString(BinaryPlistFixtures.SingleString(value, utf16: true)));
            Assert.Equal([value], BinaryPlistReader.ReadStrings(BinaryPlistFixtures.ArrayOfStrings(value)));
        }
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x62, 0x70, 0x6C, 0x69, 0x73, 0x74 })]
    public void TruncatedInput_YieldsNothing(byte[] data)
    {
        Assert.Empty(BinaryPlistReader.ReadStrings(data));
        Assert.Null(BinaryPlistReader.ReadString(data));
    }

    [Fact]
    public void CorruptTrailerSizes_YieldNothing()
    {
        var data = BinaryPlistFixtures.ArrayOfStrings("tag");
        data[^26] = 0;

        Assert.Empty(BinaryPlistReader.ReadStrings(data));
    }

    [Fact]
    public void UnexpectedTopObject_IsSkippedRatherThanThrowing()
    {
        var data = BinaryPlistFixtures.SingleString("value");

        Assert.Empty(BinaryPlistReader.ReadStrings(data));
    }
}
