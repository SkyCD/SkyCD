using Avalonia.Data.Converters;
using System;
using System.Globalization;
using System.IO;

namespace SkyCD.Converters
{
    public class DriveTypeToPathConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is DriveType dt)
            {
                return dt switch
                {
                    DriveType.Fixed => "M3 3h14v14H3z M5 5v10h10V5z",
                    DriveType.Removable => "M12 2l8 4v8l-8 4-8-4V6z",
                    DriveType.CDRom => "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2M12,17A5,5 0 1,1 17,12A5,5 0 0,1 12,17Z",
                    DriveType.Network => "M4 4h16v12H4z M6 6v8h12V6z",
                    _ => "M10,4C8.89,4 8,4.89 8,6V18A2,2 0 0,0 10,20H20A2,2 0 0,0 22,18V8L16,2H10Z",
                };
            }
            return "";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
