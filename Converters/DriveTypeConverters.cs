using Avalonia.Data.Converters;
using System;
using System.Globalization;
using System.IO;

namespace SkyCD.Converters
{
    public static class DriveTypeConverters
    {

        public static string ToEmoji(DriveType dt)
        {
            return dt switch
            {
                DriveType.Fixed => "\U0001F5A5",
                DriveType.Removable => "\U0001F4BE",
                DriveType.CDRom => "\U0001F4BF",
                DriveType.Network => "\U0001F310",
                DriveType.Ram => "\U0001F4BB",
                _ => "\U0001F5A5",
            };
        }
    }

    public class DriveTypeConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not DriveType dt)
                return parameter is string p && p?.Equals("emojiUri", StringComparison.OrdinalIgnoreCase) == true ? null! : string.Empty;

            return DriveTypeConverters.ToEmoji(dt);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
