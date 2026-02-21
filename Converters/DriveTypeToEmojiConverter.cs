using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SkyCD.Converters
{
    public class DriveTypeToEmojiConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is System.IO.DriveType dt)
            {
                return dt switch
                {
                    System.IO.DriveType.Fixed => "\U0001F5A5",    // desktop computer
                    System.IO.DriveType.Removable => "\U0001F4BE", // floppy disk
                    System.IO.DriveType.CDRom => "\U0001F4BF",     // optical disc
                    System.IO.DriveType.Network => "\U0001F310",   // globe
                    System.IO.DriveType.Ram => "\U0001F4BB",       // laptop
                    _ => "\U0001F5A5",
                };
            }

            return string.Empty;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
