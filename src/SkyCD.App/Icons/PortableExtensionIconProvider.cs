using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SkyCD.App.Icons;

public sealed class PortableExtensionIconProvider : IFileExtensionIconProvider
{
    private static readonly ConcurrentDictionary<string, Bitmap?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    public static PortableExtensionIconProvider Instance { get; } = new();

    public Bitmap? ResolveExtensionIcon(string extension)
    {
        var normalized = (extension ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "file";
        }

        return IconCache.GetOrAdd(normalized, CreateExtensionBitmap);
    }

    private static Bitmap? CreateExtensionBitmap(string ext)
    {
        try
        {
            var accentColor = GetCategoryColor(ext);
            return RenderFileIcon(accentColor);
        }
        catch
        {
            return null;
        }
    }

    private static uint GetCategoryColor(string ext) => ext switch
    {
        // Documents
        "pdf" => 0xFFE53935, // Red
        "doc" or "docx" or "odt" or "rtf" or "pages" => 0xFF1E88E5, // Blue
        "xls" or "xlsx" or "csv" or "tsv" or "ods" or "numbers" => 0xFF43A047, // Green
        "ppt" or "pptx" or "odp" or "key" => 0xFFFB8C00, // Orange
        "txt" or "log" or "md" or "markdown" or "nfo" or "readme" => 0xFF546E7A, // Slate Gray

        // Code / Development
        "cs" or "vb" or "fs" or "sln" or "csproj" or "vbproj" => 0xFF5E35B1, // Purple
        "js" or "ts" or "jsx" or "tsx" or "mjs" => 0xFFFDD835, // Yellow
        "html" or "htm" or "css" or "scss" or "sass" or "less" => 0xFFE64A19, // Deep Orange
        "json" or "xml" or "yaml" or "yml" or "toml" or "ini" or "config" => 0xFFFFA000, // Amber
        "py" or "pyw" or "java" or "c" or "cpp" or "h" or "hpp" or "rs" or "go" or "kt" or "swift" or "rb" or "php" => 0xFF00897B, // Teal
        "sh" or "bat" or "cmd" or "ps1" or "psm1" or "vbs" => 0xFF263238, // Dark Slate

        // Audio
        "mp3" or "wav" or "flac" or "aac" or "ogg" or "wma" or "m4a" or "mid" or "midi" or "pls" or "m3u" or "xspf" => 0xFFFFB300, // Gold

        // Video
        "mp4" or "mkv" or "avi" or "mov" or "wmv" or "flv" or "webm" or "mpg" or "mpeg" or "3gp" or "m4v" => 0xFF8E24AA, // Violet

        // Images
        "png" or "jpg" or "jpeg" or "gif" or "bmp" or "svg" or "ico" or "webp" or "tiff" or "psd" or "ai" or "raw" => 0xFFD81B60, // Magenta/Pink

        // Archives
        "zip" or "7z" or "rar" or "tar" or "gz" or "bz2" or "xz" or "cab" or "tgz" => 0xFF8D6E63, // Brown/Ochre

        // Disk Images
        "iso" or "img" or "vhd" or "vhdx" or "bin" or "cue" or "dmg" or "scd" or "ascd" or "cscd" => 0xFF0288D1, // Light Blue

        // Executables & System
        "exe" or "msi" or "com" => 0xFF1565C0, // Navy Blue
        "dll" or "sys" or "so" or "dylib" => 0xFF455A64, // Blue Gray

        // Database
        "db" or "sqlite" or "sqlite3" or "mdb" or "accdb" or "sql" => 0xFF2E7D32, // Dark Green

        // Fonts
        "ttf" or "otf" or "woff" or "woff2" or "eot" => 0xFF6D4C41, // Coffee Brown

        _ => 0xFF78909C // Cool Gray
    };

    private static Bitmap RenderFileIcon(uint accentColor)
    {
        const int width = 16;
        const int height = 16;
        var pixels = new uint[width * height];

        // 16x16 Pixel Document Template
        // Document Boundary: X: 2..13, Y: 1..14
        // Fold at Top Right: X: 10..13, Y: 1..4
        const uint border = 0xFF8A9BA8;
        const uint body = 0xFFFFFFFF;
        const uint foldBack = 0xFFD8E1E8;
        const uint textLine = 0xFFCFD8DC;

        for (var y = 1; y <= 14; y++)
        {
            for (var x = 2; x <= 13; x++)
            {
                // Top-right corner fold
                if (x >= 10 && y <= 4 && (x - 9) >= (5 - y))
                {
                    continue;
                }

                var isBorder = x == 2 || x == 13 || y == 1 || y == 14 ||
                               (y <= 4 && x == 9) || (x >= 10 && y == 5);

                pixels[y * width + x] = isBorder ? border : body;
            }
        }

        // Draw folded flap
        pixels[2 * width + 10] = foldBack;
        pixels[2 * width + 11] = foldBack;
        pixels[3 * width + 10] = foldBack;
        pixels[3 * width + 11] = foldBack;
        pixels[3 * width + 12] = foldBack;
        pixels[4 * width + 10] = foldBack;
        pixels[4 * width + 11] = foldBack;
        pixels[4 * width + 12] = foldBack;

        // Draw text preview lines
        for (var x = 4; x <= 8; x++)
        {
            pixels[3 * width + x] = textLine;
            pixels[5 * width + x] = textLine;
        }

        for (var x = 4; x <= 11; x++)
        {
            pixels[7 * width + x] = textLine;
        }

        // Draw accent badge
        for (var y = 9; y <= 12; y++)
        {
            for (var x = 4; x <= 11; x++)
            {
                pixels[y * width + x] = accentColor;
            }
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (var frameBuffer = bitmap.Lock())
        {
            Marshal.Copy(
                MemoryMarshal.Cast<uint, byte>(pixels.AsSpan()).ToArray(),
                0,
                frameBuffer.Address,
                pixels.Length * 4);
        }

        return bitmap;
    }
}
