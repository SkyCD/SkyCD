using System;
using System.Globalization;
using Avalonia.Data.Converters;
using IconPacks.Avalonia.Lucide;

namespace SkyCD.App.Converters;

public sealed class InternetProtocolIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            "ftp" => PackIconLucideKind.FolderUp,
            "ftps" => PackIconLucideKind.Lock,
            "sftp" => PackIconLucideKind.Key,
            "smb" => PackIconLucideKind.Network,
            "webdav" => PackIconLucideKind.Globe,
            "webdavs" => PackIconLucideKind.ShieldCheck,
            _ => PackIconLucideKind.Server,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
