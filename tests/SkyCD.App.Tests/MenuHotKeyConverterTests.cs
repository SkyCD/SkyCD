using System.Globalization;
using Avalonia.Input;
using SkyCD.App.Converters;
using Xunit;

namespace SkyCD.App.Tests;

public class MenuHotKeyConverterTests
{
    private readonly MenuHotKeyConverter converter = new();

    [Theory]
    [InlineData("Delete", Key.Delete, KeyModifiers.None)]
    [InlineData("F2", Key.F2, KeyModifiers.None)]
    [InlineData("F5", Key.F5, KeyModifiers.None)]
    [InlineData("F12", Key.F12, KeyModifiers.None)]
    [InlineData("Ctrl+N", Key.N, KeyModifiers.Control)]
    [InlineData("Ctrl+O", Key.O, KeyModifiers.Control)]
    [InlineData("Ctrl+S", Key.S, KeyModifiers.Control)]
    [InlineData("Ctrl+C", Key.C, KeyModifiers.Control)]
    [InlineData("Ctrl+V", Key.V, KeyModifiers.Control)]
    [InlineData("Alt+Enter", Key.Enter, KeyModifiers.Alt)]
    [InlineData("Ctrl+Alt+O", Key.O, KeyModifiers.Control | KeyModifiers.Alt)]
    public void Convert_ValidShortcutStrings_ReturnsParsedKeyGesture(string input, Key expectedKey, KeyModifiers expectedModifiers)
    {
        var result = converter.Convert(input, typeof(KeyGesture), null, CultureInfo.InvariantCulture);

        var gesture = Assert.IsType<KeyGesture>(result);
        Assert.Equal(expectedKey, gesture.Key);
        Assert.Equal(expectedModifiers, gesture.KeyModifiers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Convert_NullOrEmptyString_ReturnsNull(string? input)
    {
        var result = converter.Convert(input, typeof(KeyGesture), null, CultureInfo.InvariantCulture);
        Assert.Null(result);
    }

    [Fact]
    public void Convert_InvalidShortcutString_ReturnsNull()
    {
        var result = converter.Convert("NotAValidShortcut+++", typeof(KeyGesture), null, CultureInfo.InvariantCulture);
        Assert.Null(result);
    }

    [Fact]
    public void Convert_NonStringValue_ReturnsOriginalValue()
    {
        var dummyObject = new object();
        var result = converter.Convert(dummyObject, typeof(KeyGesture), null, CultureInfo.InvariantCulture);
        Assert.Same(dummyObject, result);
    }
}
