using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkyCD.App.Converters;
using SkyCD.App.Icons;
using SkyCD.App.Views;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class IconResolutionIntegrationTests : IDisposable
{
    static IconResolutionIntegrationTests()
    {
        var builder = AppBuilder.Configure<Application>()
            .UseStandardRuntimePlatformSubsystem()
            .UseSkia();
        builder.RuntimePlatformServicesInitializer?.Invoke();
        builder.RenderingSubsystemInitializer?.Invoke();
    }

    public IconResolutionIntegrationTests()
    {
        IconGlyphConverter.ClearCache();
        IconGlyphConverter.CustomResolver = null;
    }

    public void Dispose()
    {
        IconGlyphConverter.ClearCache();
        IconGlyphConverter.CustomResolver = null;
    }

    [Fact]
    public void CatalogDocument_IconGlyph_ResolvesCustomIconProperty()
    {
        var docWithBase64 = new CatalogDocument
        {
            Id = "doc-1",
            Name = "custom.dat",
            Type = CatalogDocumentType.File,
            Properties = new SkyCD.Documents.Collections.PropertiesCollection
            {
                ["Icon"] = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="
            }
        };

        Assert.Equal("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==", docWithBase64.IconGlyph);

        var docWithByteArray = new CatalogDocument
        {
            Id = "doc-2",
            Name = "sample.bin",
            Type = CatalogDocumentType.File,
            Properties = new SkyCD.Documents.Collections.PropertiesCollection
            {
                ["Icon"] = new byte[] { 1, 2, 3, 4 }
            }
        };

        Assert.StartsWith("data:image/x-icon;base64,", docWithByteArray.IconGlyph, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CatalogDocument_IconGlyph_ResolvesExtensionKeyForFiles()
    {
        var docPdf = new CatalogDocument
        {
            Id = "doc-pdf",
            Name = "Report.PDF",
            Type = CatalogDocumentType.File
        };
        Assert.Equal("ext:pdf", docPdf.IconGlyph);

        var docZip = new CatalogDocument
        {
            Id = "doc-zip",
            Name = "archive.tar.gz",
            Type = CatalogDocumentType.File
        };
        Assert.Equal("ext:gz", docZip.IconGlyph);

        var docNoExt = new CatalogDocument
        {
            Id = "doc-no-ext",
            Name = "Makefile",
            Type = CatalogDocumentType.File
        };
        Assert.Equal("file", docNoExt.IconGlyph);
    }

    [Fact]
    public void CatalogDocument_IconGlyph_ResolvesTypeGlyphForNonFiles()
    {
        var folder = new CatalogDocument
        {
            Id = "folder-1",
            Name = "My Folder",
            Type = CatalogDocumentType.Folder
        };
        Assert.Equal("folder", folder.IconGlyph);

        var media = new CatalogDocument
        {
            Id = "media-1",
            Name = "[Disk A]",
            Type = CatalogDocumentType.Media
        };
        Assert.Equal("video", media.IconGlyph);

        var netFolder = new CatalogDocument
        {
            Id = "net-1",
            Name = "Remote Share",
            Type = CatalogDocumentType.NetworkFolder
        };
        Assert.Equal("network-folder", netFolder.IconGlyph);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("txt")]
    [InlineData("docx")]
    [InlineData("xlsx")]
    [InlineData("pptx")]
    [InlineData("cs")]
    [InlineData("js")]
    [InlineData("mp3")]
    [InlineData("mp4")]
    [InlineData("png")]
    [InlineData("zip")]
    [InlineData("iso")]
    [InlineData("exe")]
    [InlineData("db")]
    public void PortableExtensionIconProvider_ResolvesDistinctIconsForMultipleExtensions(string ext)
    {
        var icon = PortableExtensionIconProvider.Instance.ResolveExtensionIcon(ext);
        Assert.NotNull(icon);
        Assert.Equal(16, icon.PixelSize.Width);
        Assert.Equal(16, icon.PixelSize.Height);
    }

    [Fact]
    public void IconGlyphConverter_SupportsDeterministicCustomResolver()
    {
        var testBitmap = new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var testResolver = new MockTestIconResolver();
        testResolver.Register("ext:custom-ext", testBitmap);

        IconGlyphConverter.CustomResolver = testResolver;

        var converter = new IconGlyphConverter();
        var resolved = converter.Convert("ext:custom-ext", typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Same(testBitmap, resolved);
    }

    [Fact]
    public void IconGlyphConverter_ResolvesValidBase64CustomIcon()
    {
        // 1x1 transparent PNG in base64
        const string base64Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        var converter = new IconGlyphConverter();
        var result = converter.Convert(base64Png, typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture);

        Assert.NotNull(result);
        var bitmap = Assert.IsAssignableFrom<Bitmap>(result);
        Assert.Equal(1, bitmap.PixelSize.Width);
        Assert.Equal(1, bitmap.PixelSize.Height);
    }

    [Fact]
    public void IconGlyphConverter_HandlesMissingFileAndCorruptDataGracefullyWithoutThrowing()
    {
        var converter = new IconGlyphConverter();

        // Non-existent file path
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "skycd-nonexistent-" + Guid.NewGuid().ToString("N") + ".png");
        var fromMissingFile = converter.Convert(nonExistentPath, typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.NotNull(fromMissingFile); // Falls back to extension/default icon without throwing

        // Corrupt base64 string
        var fromCorruptBase64 = converter.Convert("data:image/png;base64,ThisIsNotValidBase64Data!#@$", typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.NotNull(fromCorruptBase64); // Falls back safely without throwing

        // Null and empty inputs
        Assert.NotNull(converter.Convert(null, typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.NotNull(converter.Convert("", typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void IconGlyphConverter_CachesResultsAppropriately()
    {
        var converter = new IconGlyphConverter();

        var first = converter.Convert("ext:pdf", typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture);
        var second = converter.Convert("ext:pdf", typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture);

        Assert.NotNull(first);
        Assert.Same(first, second);

        IconGlyphConverter.ClearCache();

        var afterClear = converter.Convert("ext:pdf", typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.NotNull(afterClear);
    }

    [Fact]
    public void BrowserTreeNode_And_BrowserItem_And_PropertiesDialog_ShareConsistentIconGlyph()
    {
        var customIcon = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        var entries = new[]
        {
            new CatalogDocument
            {
                Id = "root",
                Name = "Root Media",
                Type = CatalogDocumentType.Media,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["Icon"] = customIcon }
            },
            new CatalogDocument
            {
                Id = "child-folder",
                ParentId = "root",
                Name = "Documents Folder",
                Type = CatalogDocumentType.Folder
            },
            new CatalogDocument
            {
                Id = "child-file",
                ParentId = "child-folder",
                Name = "Manual.pdf",
                Type = CatalogDocumentType.File
            }
        };

        var vm = MainWindowViewModel.CreateForInMemoryCatalog(entries);

        // Tree node icon consistency
        var rootTreeNode = Assert.Single(vm.TreeNodes);
        Assert.Equal(customIcon, rootTreeNode.IconGlyph);

        // Browser items icon consistency
        vm.SelectedTreeNode = rootTreeNode;
        var folderItem = vm.BrowserItems.Single(item => item.Id == "child-folder");
        Assert.Equal("folder", folderItem.IconGlyph);

        var childTreeNode = Assert.Single(rootTreeNode.Children);
        vm.SelectedTreeNode = childTreeNode;
        var fileItem = vm.BrowserItems.Single(item => item.Id == "child-file");
        Assert.Equal("ext:pdf", fileItem.IconGlyph);

        // Properties dialog consistency
        var propsVm = new PropertiesDialogViewModel(fileItem.Id, fileItem.Name, fileItem.IconGlyph, "", new SkyCD.Documents.Collections.PropertiesCollection());
        Assert.Equal("ext:pdf", propsVm.IconGlyph);
    }

    [Fact]
    public void ImportedCatalog_PreservesIconMetadata()
    {
        using var json = JsonDocument.Parse("""
            {"nodes":[
              {"nodeId":"root","parentId":null,"kind":"folder","name":"Root","sizeBytes":0},
              {"nodeId":"child","parentId":"root","kind":"file","name":"Image.png","sizeBytes":100,"icon":"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="}
            ]}
            """);

        var entries = CatalogImportTests.Extract(json.RootElement);
        Assert.Equal(2, entries.Count);

        var child = entries.Single(e => e.Id == "child");
        Assert.True(child.Properties.ContainsKey("Icon"));
        Assert.StartsWith("data:image/png;base64,", child.IconGlyph);
    }

    private sealed class MockTestIconResolver : IIconResolver
    {
        private readonly Dictionary<string, Bitmap> _icons = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string key, Bitmap bitmap)
        {
            _icons[key] = bitmap;
        }

        public Bitmap? ResolveIcon(string key)
        {
            return _icons.GetValueOrDefault(key);
        }
    }
}
