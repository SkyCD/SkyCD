using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkyCD.App.Converters;
using SkyCD.App.Icons;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class LinuxFreeDesktopIconProviderTests : IDisposable
{
    private static readonly byte[] SamplePngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private readonly string _testTempDir;

    static LinuxFreeDesktopIconProviderTests()
    {
        var builder = AppBuilder.Configure<Application>()
            .UseStandardRuntimePlatformSubsystem()
            .UseSkia();
        builder.RuntimePlatformServicesInitializer?.Invoke();
        builder.RenderingSubsystemInitializer?.Invoke();
    }

    public LinuxFreeDesktopIconProviderTests()
    {
        _testTempDir = Path.Combine(Path.GetTempPath(), "skycd-linux-icons-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testTempDir);

        LinuxFreeDesktopIconProvider.Instance.ClearCache();
        IconGlyphConverter.ClearCache();
    }

    public void Dispose()
    {
        LinuxFreeDesktopIconProvider.Instance.ClearCache();
        LinuxFreeDesktopIconProvider.Instance.SearchDirectories = null;
        LinuxFreeDesktopIconProvider.Instance.ExplicitThemeName = null;
        LinuxFreeDesktopIconProvider.Instance.MimeGlobsPaths = null;
        LinuxFreeDesktopIconProvider.Instance.EnvironmentVariableReader = null;
        LinuxFreeDesktopIconProvider.Instance.FileExistsPredicate = null;
        LinuxFreeDesktopIconProvider.Instance.DirectoryExistsPredicate = null;
        LinuxFreeDesktopIconProvider.Instance.ForceEnable = false;

        IconGlyphConverter.ClearCache();
        IconGlyphConverter.CustomResolver = null;

        try
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }
        catch
        {
            // Ignore temp cleanup errors
        }
    }

    [Fact]
    public void GetCandidateIconNames_ResolvesStandardMimeAndLegacyNames()
    {
        var provider = new LinuxFreeDesktopIconProvider();

        var pdfCandidates = provider.GetCandidateIconNames("pdf");
        Assert.Contains("application-pdf", pdfCandidates);
        Assert.Contains("gnome-mime-application-pdf", pdfCandidates);
        Assert.Contains("x-office-document", pdfCandidates);
        Assert.Contains("pdf", pdfCandidates);

        var pngCandidates = provider.GetCandidateIconNames("png");
        Assert.Contains("image-png", pngCandidates);
        Assert.Contains("image-x-generic", pngCandidates);

        var zipCandidates = provider.GetCandidateIconNames("zip");
        Assert.Contains("application-zip", zipCandidates);
        Assert.Contains("package-x-generic", zipCandidates);

        var csCandidates = provider.GetCandidateIconNames("cs");
        Assert.Contains("text-x-csharp", csCandidates);
        Assert.Contains("text-x-generic", csCandidates);
    }

    [Fact]
    public void MimeGlobs_ParsesCustomGlobsFileAndAugmentsCandidates()
    {
        var globsFile = Path.Combine(_testTempDir, "globs2");
        File.WriteAllLines(globsFile,
        [
            "# Custom MIME globs",
            "50:application/x-custom-report:*.customreport:cs",
            "50:text/x-special-log:*.speciallog"
        ]);

        var provider = new LinuxFreeDesktopIconProvider
        {
            MimeGlobsPaths = [globsFile]
        };

        var customCandidates = provider.GetCandidateIconNames("customreport");
        Assert.Contains("application-x-custom-report", customCandidates);
        Assert.Contains("gnome-mime-application-x-custom-report", customCandidates);
        Assert.Contains("application-custom-report", customCandidates);
        Assert.Contains("application-x-generic", customCandidates);

        var logCandidates = provider.GetCandidateIconNames("speciallog");
        Assert.Contains("text-x-special-log", logCandidates);
    }

    [Fact]
    public void GetActiveThemeName_ReadsFromGtk3Settings()
    {
        var gtk3Dir = Path.Combine(_testTempDir, ".config", "gtk-3.0");
        Directory.CreateDirectory(gtk3Dir);
        var settingsFile = Path.Combine(gtk3Dir, "settings.ini");
        File.WriteAllLines(settingsFile,
        [
            "[Settings]",
            "gtk-icon-theme-name=Papirus-Dark",
            "gtk-theme-name=Adwaita-dark"
        ]);

        var provider = new LinuxFreeDesktopIconProvider
        {
            EnvironmentVariableReader = key => key == "HOME" ? _testTempDir : null
        };

        var activeTheme = provider.GetActiveThemeName();
        Assert.Equal("Papirus-Dark", activeTheme);
    }

    [Fact]
    public void GetActiveThemeName_ReadsFromKdeGlobals()
    {
        var kdeDir = Path.Combine(_testTempDir, ".config");
        Directory.CreateDirectory(kdeDir);
        var kdeglobals = Path.Combine(kdeDir, "kdeglobals");
        File.WriteAllLines(kdeglobals,
        [
            "[General]",
            "ColorScheme=BreezeDark",
            "",
            "[Icons]",
            "Theme=breeze-dark"
        ]);

        var provider = new LinuxFreeDesktopIconProvider
        {
            EnvironmentVariableReader = key => key == "HOME" ? _testTempDir : null
        };

        var activeTheme = provider.GetActiveThemeName();
        Assert.Equal("breeze-dark", activeTheme);
    }

    [Fact]
    public void GetActiveThemeName_ReadsFromDesktopEnvironmentVariable()
    {
        var provider = new LinuxFreeDesktopIconProvider
        {
            EnvironmentVariableReader = key => key switch
            {
                "HOME" => Path.Combine(_testTempDir, "empty-home"),
                "XDG_CURRENT_DESKTOP" => "ubuntu:GNOME",
                _ => null
            }
        };

        var activeTheme = provider.GetActiveThemeName();
        Assert.Equal("Yaru", activeTheme);
    }

    [Fact]
    public void GetThemeInheritanceChain_TraversesIndexThemeInherits()
    {
        var iconsDir = Path.Combine(_testTempDir, "icons");
        var myThemeDir = Path.Combine(iconsDir, "MyCustomTheme");
        Directory.CreateDirectory(myThemeDir);

        File.WriteAllLines(Path.Combine(myThemeDir, "index.theme"),
        [
            "[Icon Theme]",
            "Name=MyCustomTheme",
            "Inherits=gnome,hicolor"
        ]);

        var provider = new LinuxFreeDesktopIconProvider
        {
            ExplicitThemeName = "MyCustomTheme",
            SearchDirectories = [iconsDir]
        };

        var chain = provider.GetThemeInheritanceChain();
        Assert.Equal("MyCustomTheme", chain[0]);
        Assert.Contains("gnome", chain);
        Assert.Contains("hicolor", chain);
    }

    [Fact]
    public void ResolveExtensionIcon_FindsIconInThemeDirectory()
    {
        var iconsDir = Path.Combine(_testTempDir, "icons");
        var themeDir = Path.Combine(iconsDir, "Adwaita", "16x16", "mimetypes");
        Directory.CreateDirectory(themeDir);

        var pdfIconPath = Path.Combine(themeDir, "application-pdf.png");
        File.WriteAllBytes(pdfIconPath, SamplePngBytes);

        var provider = new LinuxFreeDesktopIconProvider
        {
            ForceEnable = true,
            ExplicitThemeName = "Adwaita",
            SearchDirectories = [iconsDir]
        };

        var bitmap = provider.ResolveExtensionIcon("pdf");
        Assert.NotNull(bitmap);
        Assert.Equal(1, bitmap.PixelSize.Width);
        Assert.Equal(1, bitmap.PixelSize.Height);
    }

    [Fact]
    public void ResolveExtensionIcon_FallsBackToPixmapsDirectory()
    {
        var pixmapsDir = Path.Combine(_testTempDir, "pixmaps");
        Directory.CreateDirectory(pixmapsDir);

        var docxIconPath = Path.Combine(pixmapsDir, "application-msword.png");
        File.WriteAllBytes(docxIconPath, SamplePngBytes);

        var provider = new LinuxFreeDesktopIconProvider
        {
            ForceEnable = true,
            ExplicitThemeName = "NonExistentTheme",
            SearchDirectories = [_testTempDir]
        };

        var bitmap = provider.ResolveExtensionIcon("docx");
        Assert.NotNull(bitmap);
    }

    [Fact]
    public void IconGlyphConverter_WithLinuxProvider_ResolvesThemeIconAndFallsBack()
    {
        var iconsDir = Path.Combine(_testTempDir, "icons");
        var themeDir = Path.Combine(iconsDir, "Papirus", "16x16", "mimetypes");
        Directory.CreateDirectory(themeDir);

        var xlsIconPath = Path.Combine(themeDir, "x-office-spreadsheet.png");
        File.WriteAllBytes(xlsIconPath, SamplePngBytes);

        LinuxFreeDesktopIconProvider.Instance.ForceEnable = true;
        LinuxFreeDesktopIconProvider.Instance.ExplicitThemeName = "Papirus";
        LinuxFreeDesktopIconProvider.Instance.SearchDirectories = [iconsDir];

        // Resolves from FreeDesktop theme icon
        var xlsBitmap = IconGlyphConverter.Resolve("ext:xlsx");
        Assert.NotNull(xlsBitmap);
        Assert.Equal(1, xlsBitmap.PixelSize.Width);

        // Extension not in theme falls back to PortableExtensionIconProvider (16x16 generated bitmap)
        var rustBitmap = IconGlyphConverter.Resolve("ext:rs");
        Assert.NotNull(rustBitmap);
        Assert.Equal(16, rustBitmap.PixelSize.Width);
    }
}
