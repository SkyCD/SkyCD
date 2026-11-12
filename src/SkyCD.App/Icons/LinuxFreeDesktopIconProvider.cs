using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;

namespace SkyCD.App.Icons;

/// <summary>
/// Resolves file-type icons on Linux desktop environments (GNOME, KDE Plasma, XFCE, MATE, Cinnamon)
/// in accordance with the FreeDesktop.org (XDG) Icon Theme and Shared MIME-Info specifications.
/// </summary>
public sealed class LinuxFreeDesktopIconProvider : IFileExtensionIconProvider
{
    private static readonly string[] ImageExtensions = [".png", ".ico", ".bmp", ".jpg", ".jpeg"];

    private static readonly string[] SubdirectoryResolutionOrder =
    [
        "16x16/mimetypes",
        "16x16/places",
        "16x16/apps",
        "22x22/mimetypes",
        "22x22/apps",
        "24x24/mimetypes",
        "24x24/apps",
        "32x32/mimetypes",
        "32x32/apps",
        "48x48/mimetypes",
        "48x48/apps",
        "64x64/mimetypes",
        "128x128/mimetypes",
        "mimetypes/16",
        "mimetypes/22",
        "mimetypes/24",
        "mimetypes/32",
        "mimetypes/48",
        "mimetypes",
        "apps",
        "scalable/mimetypes",
        "scalable/apps"
    ];

    private static readonly Dictionary<string, string[]> ExtensionIconNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Documents
        ["pdf"] = ["application-pdf", "gnome-mime-application-pdf", "application-x-pdf", "x-office-document"],
        ["doc"] = ["application-msword", "gnome-mime-application-msword", "x-office-document"],
        ["docx"] = ["application-vnd.openxmlformats-officedocument.wordprocessingml.document", "application-msword", "x-office-document"],
        ["odt"] = ["application-vnd.oasis.opendocument.text", "x-office-document"],
        ["rtf"] = ["application-rtf", "text-rtf", "x-office-document"],
        ["txt"] = ["text-plain", "gnome-mime-text-plain", "text-x-generic", "text-x-generic-template"],
        ["md"] = ["text-markdown", "text-x-markdown", "text-plain", "text-x-generic"],
        ["markdown"] = ["text-markdown", "text-x-markdown", "text-plain", "text-x-generic"],
        ["log"] = ["text-x-log", "text-plain", "text-x-generic"],
        ["nfo"] = ["text-plain", "text-x-generic"],
        ["csv"] = ["text-csv", "application-csv", "x-office-spreadsheet"],
        ["tsv"] = ["text-tab-separated-values", "x-office-spreadsheet"],
        ["xls"] = ["application-vnd.ms-excel", "gnome-mime-application-vnd.ms-excel", "x-office-spreadsheet"],
        ["xlsx"] = ["application-vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application-vnd.ms-excel", "x-office-spreadsheet"],
        ["ods"] = ["application-vnd.oasis.opendocument.spreadsheet", "x-office-spreadsheet"],
        ["ppt"] = ["application-vnd.ms-powerpoint", "gnome-mime-application-vnd.ms-powerpoint", "x-office-presentation"],
        ["pptx"] = ["application-vnd.openxmlformats-officedocument.presentationml.presentation", "application-vnd.ms-powerpoint", "x-office-presentation"],
        ["odp"] = ["application-vnd.oasis.opendocument.presentation", "x-office-presentation"],
        ["epub"] = ["application-epub+zip", "x-office-document"],
        ["mobi"] = ["application-x-mobipocket-ebook", "x-office-document"],

        // Images
        ["png"] = ["image-png", "gnome-mime-image-png", "image-x-generic"],
        ["jpg"] = ["image-jpeg", "gnome-mime-image-jpeg", "image-x-generic"],
        ["jpeg"] = ["image-jpeg", "gnome-mime-image-jpeg", "image-x-generic"],
        ["gif"] = ["image-gif", "gnome-mime-image-gif", "image-x-generic"],
        ["bmp"] = ["image-bmp", "image-x-generic"],
        ["svg"] = ["image-svg+xml", "image-svg", "image-x-generic"],
        ["webp"] = ["image-webp", "image-x-generic"],
        ["ico"] = ["image-vnd.microsoft.icon", "image-x-ico", "image-x-generic"],
        ["tiff"] = ["image-tiff", "image-x-generic"],
        ["tif"] = ["image-tiff", "image-x-generic"],
        ["psd"] = ["image-vnd.adobe.photoshop", "image-x-psd", "image-x-generic"],
        ["xcf"] = ["image-x-xcf", "image-x-generic"],

        // Audio
        ["mp3"] = ["audio-mpeg", "audio-mp3", "gnome-mime-audio-mpeg", "audio-x-generic", "sound"],
        ["flac"] = ["audio-flac", "audio-x-flac", "audio-x-generic", "sound"],
        ["wav"] = ["audio-x-wav", "audio-wav", "audio-x-generic", "sound"],
        ["ogg"] = ["audio-x-vorbis+ogg", "audio-ogg", "audio-x-generic", "sound"],
        ["oga"] = ["audio-ogg", "audio-x-generic", "sound"],
        ["opus"] = ["audio-opus", "audio-x-generic", "sound"],
        ["m4a"] = ["audio-mp4", "audio-m4a", "audio-x-generic", "sound"],
        ["aac"] = ["audio-aac", "audio-x-generic", "sound"],
        ["wma"] = ["audio-x-ms-wma", "audio-x-generic", "sound"],
        ["mid"] = ["audio-midi", "audio-x-generic", "sound"],
        ["midi"] = ["audio-midi", "audio-x-generic", "sound"],

        // Video
        ["mp4"] = ["video-mp4", "gnome-mime-video-mp4", "video-x-generic"],
        ["mkv"] = ["video-x-matroska", "video-x-generic"],
        ["avi"] = ["video-x-msvideo", "video-avi", "video-x-generic"],
        ["mov"] = ["video-quicktime", "video-x-generic"],
        ["webm"] = ["video-webm", "video-x-generic"],
        ["wmv"] = ["video-x-ms-wmv", "video-x-generic"],
        ["flv"] = ["video-x-flv", "video-x-generic"],
        ["mpg"] = ["video-mpeg", "video-x-generic"],
        ["mpeg"] = ["video-mpeg", "video-x-generic"],
        ["3gp"] = ["video-3gpp", "video-x-generic"],

        // Archives
        ["zip"] = ["application-zip", "application-x-zip", "gnome-mime-application-zip", "package-x-generic", "application-x-archive"],
        ["tar"] = ["application-x-tar", "application-x-archive", "package-x-generic"],
        ["gz"] = ["application-gzip", "application-x-gzip", "application-x-archive", "package-x-generic"],
        ["tgz"] = ["application-x-compressed-tar", "application-gzip", "package-x-generic"],
        ["bz2"] = ["application-x-bzip2", "application-x-archive", "package-x-generic"],
        ["xz"] = ["application-x-xz", "application-x-archive", "package-x-generic"],
        ["7z"] = ["application-x-7z-compressed", "package-x-generic", "application-x-archive"],
        ["rar"] = ["application-x-rar", "application-x-rar-compressed", "package-x-generic"],
        ["deb"] = ["application-vnd.debian.binary-package", "package-x-generic"],
        ["rpm"] = ["application-x-rpm", "package-x-generic"],

        // Disk Images
        ["iso"] = ["application-x-cd-image", "application-x-iso-image", "media-optical", "drive-optical"],
        ["img"] = ["application-x-raw-disk-image", "application-x-cd-image", "media-optical"],
        ["dmg"] = ["application-x-apple-diskimage", "application-x-cd-image", "media-optical"],
        ["scd"] = ["application-x-cd-image", "media-optical"],
        ["ascd"] = ["application-x-cd-image", "media-optical"],
        ["cscd"] = ["application-x-cd-image", "media-optical"],

        // Code / Development
        ["cs"] = ["text-x-csharp", "text-x-csharp+source", "text-x-script", "text-x-generic"],
        ["vb"] = ["text-x-vb", "text-x-generic"],
        ["fs"] = ["text-x-fsharp", "text-x-generic"],
        ["py"] = ["text-x-python", "text-x-python3", "text-x-script", "text-x-generic"],
        ["c"] = ["text-x-csrc", "text-x-c", "text-x-generic"],
        ["cpp"] = ["text-x-c++src", "text-x-c++", "text-x-generic"],
        ["cxx"] = ["text-x-c++src", "text-x-c++", "text-x-generic"],
        ["h"] = ["text-x-chdr", "text-x-c-header", "text-x-generic"],
        ["hpp"] = ["text-x-c++hdr", "text-x-c-header", "text-x-generic"],
        ["java"] = ["text-x-java", "text-x-java-source", "text-x-generic"],
        ["js"] = ["application-javascript", "text-javascript", "text-x-script", "text-x-generic"],
        ["ts"] = ["application-typescript", "text-x-script", "text-x-generic"],
        ["jsx"] = ["text-jsx", "text-javascript", "text-x-script", "text-x-generic"],
        ["tsx"] = ["text-tsx", "application-typescript", "text-x-script", "text-x-generic"],
        ["html"] = ["text-html", "text-html-template", "web-browser", "text-x-generic"],
        ["htm"] = ["text-html", "text-html-template", "web-browser", "text-x-generic"],
        ["css"] = ["text-css", "text-x-generic"],
        ["scss"] = ["text-x-scss", "text-css", "text-x-generic"],
        ["sass"] = ["text-x-sass", "text-css", "text-x-generic"],
        ["json"] = ["application-json", "text-json", "text-x-generic"],
        ["xml"] = ["application-xml", "text-xml", "text-x-generic"],
        ["yaml"] = ["application-x-yaml", "text-yaml", "text-x-generic"],
        ["yml"] = ["application-x-yaml", "text-yaml", "text-x-generic"],
        ["sh"] = ["application-x-shellscript", "text-x-script", "text-x-generic"],
        ["bash"] = ["application-x-shellscript", "text-x-script", "text-x-generic"],
        ["ps1"] = ["application-x-powershell", "text-x-script", "text-x-generic"],
        ["rs"] = ["text-rust", "text-x-rust", "text-x-generic"],
        ["go"] = ["text-x-go", "text-x-generic"],
        ["kt"] = ["text-x-kotlin", "text-x-generic"],
        ["rb"] = ["application-x-ruby", "text-x-ruby", "text-x-script", "text-x-generic"],
        ["php"] = ["application-x-php", "text-x-php", "text-x-script", "text-x-generic"],
        ["sql"] = ["application-x-sql", "text-x-sql", "text-x-generic"],
        ["lua"] = ["text-x-lua", "text-x-script", "text-x-generic"],

        // Executables & System
        ["exe"] = ["application-x-ms-dos-executable", "application-x-executable"],
        ["dll"] = ["application-x-sharedlib", "application-x-object"],
        ["so"] = ["application-x-sharedlib", "application-x-object"],
        ["bin"] = ["application-x-executable", "application-octet-stream"],
        ["desktop"] = ["application-x-desktop"],
        ["appimage"] = ["application-x-executable", "package-x-generic"],

        // Databases & Fonts
        ["db"] = ["application-x-sqlite3", "application-x-sql", "text-x-generic"],
        ["sqlite"] = ["application-x-sqlite3", "application-x-sql", "text-x-generic"],
        ["sqlite3"] = ["application-x-sqlite3", "application-x-sql", "text-x-generic"],
        ["ttf"] = ["font-x-generic", "application-x-font-ttf"],
        ["otf"] = ["font-x-generic", "application-x-font-otf"],
        ["woff"] = ["font-x-generic", "application-font-woff"],
        ["woff2"] = ["font-x-generic", "application-font-woff"]
    };

    private readonly ConcurrentDictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _mimeGlobs = new(StringComparer.OrdinalIgnoreCase);
    private bool _mimeGlobsLoaded;

    public static LinuxFreeDesktopIconProvider Instance { get; } = new();

    public IReadOnlyList<string>? SearchDirectories { get; set; }
    public string? ExplicitThemeName { get; set; }
    public IReadOnlyList<string>? MimeGlobsPaths { get; set; }
    public Func<string, string?>? EnvironmentVariableReader { get; set; }
    public Func<string, bool>? FileExistsPredicate { get; set; }
    public Func<string, bool>? DirectoryExistsPredicate { get; set; }
    public bool ForceEnable { get; set; }

    public bool IsEnabled => ForceEnable || OperatingSystem.IsLinux();

    public void ClearCache()
    {
        _cache.Clear();
        _mimeGlobs.Clear();
        _mimeGlobsLoaded = false;
    }

    public Bitmap? ResolveExtensionIcon(string extension)
    {
        if (!IsEnabled)
        {
            return null;
        }

        var normalized = (extension ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return _cache.GetOrAdd(normalized, ResolveExtensionIconUncached);
    }

    private Bitmap? ResolveExtensionIconUncached(string ext)
    {
        try
        {
            var candidateIconNames = GetCandidateIconNames(ext);
            var themeChain = GetThemeInheritanceChain();
            var searchPaths = GetSearchDirectories();

            foreach (var theme in themeChain)
            {
                foreach (var baseDir in searchPaths)
                {
                    var themeDir = Path.Combine(baseDir, theme);
                    if (!DirectoryExists(themeDir))
                    {
                        continue;
                    }

                    var bitmap = FindIconInThemeDirectory(themeDir, candidateIconNames);
                    if (bitmap is not null)
                    {
                        return bitmap;
                    }
                }
            }

            // Direct pixmaps search fallback (e.g. /usr/share/pixmaps)
            foreach (var baseDir in searchPaths)
            {
                var pixmapsDir = baseDir.EndsWith("pixmaps", StringComparison.OrdinalIgnoreCase)
                    ? baseDir
                    : Path.Combine(baseDir, "pixmaps");

                if (DirectoryExists(pixmapsDir))
                {
                    var bitmap = FindIconInFlatDirectory(pixmapsDir, candidateIconNames);
                    if (bitmap is not null)
                    {
                        return bitmap;
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private Bitmap? FindIconInThemeDirectory(string themeDir, IReadOnlyList<string> candidateNames)
    {
        foreach (var subDir in SubdirectoryResolutionOrder)
        {
            var dir = Path.Combine(themeDir, subDir);
            if (!DirectoryExists(dir))
            {
                continue;
            }

            var bitmap = FindIconInFlatDirectory(dir, candidateNames);
            if (bitmap is not null)
            {
                return bitmap;
            }
        }

        // Direct directory scan
        return FindIconInFlatDirectory(themeDir, candidateNames);
    }

    private Bitmap? FindIconInFlatDirectory(string dir, IReadOnlyList<string> candidateNames)
    {
        foreach (var name in candidateNames)
        {
            foreach (var ext in ImageExtensions)
            {
                var filePath = Path.Combine(dir, name + ext);
                if (FileExists(filePath))
                {
                    try
                    {
                        return new Bitmap(filePath);
                    }
                    catch
                    {
                        // Continue to next candidate
                    }
                }
            }
        }

        return null;
    }

    public IReadOnlyList<string> GetCandidateIconNames(string ext)
    {
        var names = new List<string>();

        if (ExtensionIconNameMap.TryGetValue(ext, out var mappedNames))
        {
            names.AddRange(mappedNames);
        }

        EnsureMimeGlobsLoaded();
        if (_mimeGlobs.TryGetValue(ext, out var mimeType))
        {
            AddMimeIconCandidates(names, mimeType);
        }

        // Standard extension-named fallback
        names.Add(ext);

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddMimeIconCandidates(List<string> names, string mimeType)
    {
        var normalizedMime = mimeType.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedMime))
        {
            return;
        }

        var slashIndex = normalizedMime.IndexOf('/');
        if (slashIndex <= 0 || slashIndex == normalizedMime.Length - 1)
        {
            return;
        }

        var type = normalizedMime[..slashIndex];
        var subtype = normalizedMime[(slashIndex + 1)..];

        var dashName = type + "-" + subtype.Replace('/', '-');
        names.Add(dashName);
        names.Add("gnome-mime-" + dashName);

        if (subtype.StartsWith("x-", StringComparison.OrdinalIgnoreCase))
        {
            var withoutX = type + "-" + subtype[2..];
            names.Add(withoutX);
        }
        else
        {
            var withX = type + "-x-" + subtype;
            names.Add(withX);
        }

        names.Add(type + "-x-generic");
    }

    public IReadOnlyList<string> GetThemeInheritanceChain()
    {
        var chain = new List<string>();
        var activeTheme = GetActiveThemeName();

        if (!string.IsNullOrWhiteSpace(activeTheme))
        {
            CollectThemeInheritance(activeTheme, chain);
        }

        // Popular modern fallback themes for cross-desktop coverage
        var commonThemes = new[] { "Yaru", "Adwaita", "breeze", "breeze-dark", "Papirus", "elementary-xfce", "oxygen", "Tango", "hicolor" };
        foreach (var theme in commonThemes)
        {
            if (!chain.Contains(theme, StringComparer.OrdinalIgnoreCase))
            {
                chain.Add(theme);
            }
        }

        return chain;
    }

    private void CollectThemeInheritance(string themeName, List<string> chain)
    {
        if (chain.Contains(themeName, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        chain.Add(themeName);

        var searchPaths = GetSearchDirectories();
        foreach (var baseDir in searchPaths)
        {
            var themeDir = Path.Combine(baseDir, themeName);
            var indexFile = Path.Combine(themeDir, "index.theme");
            if (!FileExists(indexFile))
            {
                continue;
            }

            try
            {
                var lines = File.ReadAllLines(indexFile);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Inherits=", StringComparison.OrdinalIgnoreCase))
                    {
                        var parents = trimmed[9..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach (var parent in parents)
                        {
                            CollectThemeInheritance(parent, chain);
                        }
                    }
                }
            }
            catch
            {
                // Continue gracefully
            }

            break;
        }
    }

    public string GetActiveThemeName()
    {
        if (!string.IsNullOrWhiteSpace(ExplicitThemeName))
        {
            return ExplicitThemeName.Trim();
        }

        var customEnv = ReadEnv("SKYCD_ICON_THEME");
        if (!string.IsNullOrWhiteSpace(customEnv))
        {
            return customEnv.Trim();
        }

        var home = GetUserHomeDirectory();
        if (!string.IsNullOrWhiteSpace(home))
        {
            // 1. GNOME / XFCE / MATE GTK 3 settings
            var gtk3Settings = Path.Combine(home, ".config", "gtk-3.0", "settings.ini");
            var gtk3Theme = ReadIniProperty(gtk3Settings, "gtk-icon-theme-name");
            if (!string.IsNullOrWhiteSpace(gtk3Theme))
            {
                return gtk3Theme;
            }

            // 2. KDE Plasma settings
            var kdeGlobals = Path.Combine(home, ".config", "kdeglobals");
            var kdeTheme = ReadKdeTheme(kdeGlobals);
            if (!string.IsNullOrWhiteSpace(kdeTheme))
            {
                return kdeTheme;
            }

            // 3. GTK 2 settings
            var gtk2Settings = Path.Combine(home, ".gtkrc-2.0");
            var gtk2Theme = ReadGtk2Theme(gtk2Settings);
            if (!string.IsNullOrWhiteSpace(gtk2Theme))
            {
                return gtk2Theme;
            }
        }

        // 4. Environment heuristics
        var gtkThemeEnv = ReadEnv("GTK_THEME");
        if (!string.IsNullOrWhiteSpace(gtkThemeEnv))
        {
            return gtkThemeEnv.Trim();
        }

        var desktop = ReadEnv("XDG_CURRENT_DESKTOP")?.ToUpperInvariant() ?? string.Empty;
        if (desktop.Contains("KDE"))
        {
            return "breeze";
        }
        if (desktop.Contains("UBUNTU"))
        {
            return "Yaru";
        }
        if (desktop.Contains("GNOME"))
        {
            return "Adwaita";
        }
        if (desktop.Contains("XFCE"))
        {
            return "elementary-xfce";
        }

        return "hicolor";
    }

    public IReadOnlyList<string> GetSearchDirectories()
    {
        if (SearchDirectories is { Count: > 0 })
        {
            return SearchDirectories;
        }

        var dirs = new List<string>();
        var home = GetUserHomeDirectory();

        if (!string.IsNullOrWhiteSpace(home))
        {
            dirs.Add(Path.Combine(home, ".icons"));
            dirs.Add(Path.Combine(home, ".local", "share", "icons"));

            var dataHome = ReadEnv("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(dataHome))
            {
                dirs.Add(Path.Combine(dataHome, "icons"));
            }
        }

        var dataDirs = ReadEnv("XDG_DATA_DIRS");
        if (!string.IsNullOrWhiteSpace(dataDirs))
        {
            var parts = dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                dirs.Add(Path.Combine(part, "icons"));
            }
        }

        dirs.Add("/usr/local/share/icons");
        dirs.Add("/usr/share/icons");
        dirs.Add("/usr/share/pixmaps");

        return dirs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void EnsureMimeGlobsLoaded()
    {
        if (_mimeGlobsLoaded)
        {
            return;
        }

        _mimeGlobsLoaded = true;
        var paths = MimeGlobsPaths ?? GetDefaultMimeGlobsPaths();

        foreach (var path in paths)
        {
            if (!FileExists(path))
            {
                continue;
            }

            try
            {
                var lines = File.ReadAllLines(path);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                    {
                        continue;
                    }

                    // format globs2: weight:mimetype:pattern:flags or globs: mimetype:pattern
                    var colonParts = trimmed.Split(':');
                    if (colonParts.Length >= 3 && int.TryParse(colonParts[0], out _))
                    {
                        var mime = colonParts[1].Trim();
                        var pattern = colonParts[2].Trim();
                        RegisterPatternMime(pattern, mime);
                    }
                    else if (colonParts.Length >= 2)
                    {
                        var mime = colonParts[0].Trim();
                        var pattern = colonParts[1].Trim();
                        RegisterPatternMime(pattern, mime);
                    }
                }
            }
            catch
            {
                // Continue gracefully
            }
        }
    }

    private void RegisterPatternMime(string pattern, string mime)
    {
        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var ext = pattern[2..].ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(ext) && !_mimeGlobs.ContainsKey(ext))
            {
                _mimeGlobs[ext] = mime;
            }
        }
    }

    private IReadOnlyList<string> GetDefaultMimeGlobsPaths()
    {
        var paths = new List<string>();
        var home = GetUserHomeDirectory();
        if (!string.IsNullOrWhiteSpace(home))
        {
            paths.Add(Path.Combine(home, ".local", "share", "mime", "globs2"));
            paths.Add(Path.Combine(home, ".local", "share", "mime", "globs"));
        }

        paths.Add("/usr/local/share/mime/globs2");
        paths.Add("/usr/local/share/mime/globs");
        paths.Add("/usr/share/mime/globs2");
        paths.Add("/usr/share/mime/globs");

        return paths;
    }

    private string? ReadEnv(string variable)
    {
        return EnvironmentVariableReader is not null
            ? EnvironmentVariableReader(variable)
            : Environment.GetEnvironmentVariable(variable);
    }

    private string? GetUserHomeDirectory()
    {
        var home = ReadEnv("HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            return home;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private bool FileExists(string path)
    {
        return FileExistsPredicate is not null
            ? FileExistsPredicate(path)
            : File.Exists(path);
    }

    private bool DirectoryExists(string path)
    {
        return DirectoryExistsPredicate is not null
            ? DirectoryExistsPredicate(path)
            : Directory.Exists(path);
    }

    private string? ReadIniProperty(string filePath, string propertyName)
    {
        if (!FileExists(filePath))
        {
            return null;
        }

        try
        {
            var lines = File.ReadAllLines(filePath);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith(propertyName + "=", StringComparison.OrdinalIgnoreCase))
                {
                    var val = trimmed[(propertyName.Length + 1)..].Trim().Trim('"', '\'');
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        return val;
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }

    private string? ReadKdeTheme(string filePath)
    {
        if (!FileExists(filePath))
        {
            return null;
        }

        try
        {
            var lines = File.ReadAllLines(filePath);
            var inIconsSection = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    inIconsSection = string.Equals(trimmed, "[Icons]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (inIconsSection && trimmed.StartsWith("Theme=", StringComparison.OrdinalIgnoreCase))
                {
                    var val = trimmed[6..].Trim().Trim('"', '\'');
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        return val;
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }

    private string? ReadGtk2Theme(string filePath)
    {
        if (!FileExists(filePath))
        {
            return null;
        }

        try
        {
            var lines = File.ReadAllLines(filePath);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"gtk-icon-theme-name\s*=\s*[""']?([^""'\r\n]+)[""']?", RegexOptions.IgnoreCase);
                if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                {
                    return match.Groups[1].Value.Trim();
                }
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }
}
