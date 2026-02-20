using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace SkyCD.Converters
{
    public class FlagPathToBitmapConverter : IValueConverter
    {
        private static readonly HttpClient _http = new HttpClient();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var path = value as string;
            if (string.IsNullOrWhiteSpace(path))
                return null!;

            try
            {
                if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    // download synchronously (small images) — Avalonia Bitmap can load from stream
                    var bytes = _http.GetByteArrayAsync(path).GetAwaiter().GetResult();
                    using var ms = new MemoryStream(bytes);
                    return Bitmap.DecodeToWidth(ms, 24);
                }

                if (File.Exists(path))
                {
                    using var fs = File.OpenRead(path);
                    return Bitmap.DecodeToWidth(fs, 24);
                }
            }
            catch { }

            return null!;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
