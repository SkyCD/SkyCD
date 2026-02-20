using Avalonia.Data.Converters;
using System;
using System.Globalization;
using SkyCD.Models.VirtualFileSystem;

namespace SkyCD.Converters
{
    public class MediaTypeToPathConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is MediaType mt)
            {
                // simple SVG path data approximations for icons
                return mt switch
                {
                    MediaType.CD => "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2M12,17A5,5 0 1,1 17,12A5,5 0 0,1 12,17Z",
                    MediaType.DVD => "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2M7,12A5,5 0 0,1 12,7",
                    MediaType.BluRay => "M12,2C6.48,2 2,6.48 2,12C2,17.52 6.48,22 12,22C17.52,22 22,17.52 22,12C22,6.48 17.52,2 12,2Z",
                    MediaType.FDD => "M3,7H21V17H3V7M5,9V15H19V9H5Z",
                    MediaType.HDD => "M20,8H4V16H20M20,6C21.1,6 22,6.9 22,8V16C22,17.1 21.1,18 20,18H4C2.9,18 2,17.1 2,16V8C2,6.9 2.9,6 4,6H20Z",
                    MediaType.FTP => "M12,2L2,7L12,12L22,7L12,2Z",
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
