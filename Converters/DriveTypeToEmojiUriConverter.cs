using Avalonia.Data.Converters;
using Avalonia;
using Avalonia.Platform;
using System;
using System.Globalization;

namespace SkyCD.Converters
{
    // Converts System.IO.DriveType to a Twemoji CDN URI (PNG).
    // The Image control can use the returned Uri as its Source.
    public class DriveTypeToEmojiUriConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is System.IO.DriveType dt)
            {
                // Map drive types to representative emoji codepoints (hex, lower-case, no U+)
                var code = dt switch
                {
                    System.IO.DriveType.Fixed => "1f5a5",       // desktop computer
                    System.IO.DriveType.Removable => "1f4be",   // floppy disk
                    System.IO.DriveType.CDRom => "1f4bf",       // optical disc
                    System.IO.DriveType.Network => "1f310",    // globe
                    System.IO.DriveType.Ram => "1f4bb",        // laptop
                    _ => "1f5a5",
                };
                // Return local avares asset URI. If you have the twemoji images bundled (via NuGet or Assets),
                // Avalonia will resolve the avares:// URI. Otherwise adjust to return a web URL.
                var assetUri = new Uri($"avares://SkyCD/Assets/twemoji/{code}.png");
                return assetUri;
            }

            return null!;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
