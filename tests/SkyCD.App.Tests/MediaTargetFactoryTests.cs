using System.IO;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Scanning;
using SkyCD.SDK.Capabilities.Media;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class MediaTargetFactoryTests
{
    private static CatalogDocument Root(CatalogDocumentType type, PropertiesCollection? properties = null) => new()
    {
        Id = "root",
        Name = "Root",
        Type = type,
        Properties = properties ?? new PropertiesCollection()
    };

    [Fact]
    public void FileRoot_IsAFileTarget()
    {
        var root = Root(CatalogDocumentType.File, new PropertiesCollection
        {
            ["FullName"] = Path.Combine("media", "disc.iso")
        });

        var target = MediaTargetFactory.FromCatalogRoot(root);

        Assert.Equal(MediaTargetKind.File, target.Kind);
        Assert.Equal(Path.Combine("media", "disc.iso"), target.Location);
        Assert.Equal("Root", target.Name);
    }

    [Fact]
    public void LocalFolderRoot_IsAFolderTarget()
    {
        var root = Root(CatalogDocumentType.Media, new PropertiesCollection
        {
            ["FullName"] = @"C:\Media\Album"
        });

        var target = MediaTargetFactory.FromCatalogRoot(root);

        Assert.Equal(MediaTargetKind.Folder, target.Kind);
        Assert.Equal(@"C:\Media\Album", target.Location);
    }

    [Fact]
    public void UncFolderRoot_IsANetworkFolderTarget()
    {
        var root = Root(CatalogDocumentType.Folder, new PropertiesCollection
        {
            ["FullName"] = @"\\server\share\media"
        });

        var target = MediaTargetFactory.FromCatalogRoot(root);

        Assert.Equal(MediaTargetKind.NetworkFolder, target.Kind);
        Assert.Equal(@"\\server\share\media", target.Location);
    }

    [Fact]
    public void NetworkFolderRoot_IsANetworkFolderTarget()
    {
        var root = Root(CatalogDocumentType.NetworkFolder, new PropertiesCollection
        {
            ["URL"] = "sftp://example.com/media"
        });

        var target = MediaTargetFactory.FromCatalogRoot(root);

        Assert.Equal(MediaTargetKind.NetworkFolder, target.Kind);
        Assert.Equal("sftp://example.com/media", target.Location);
    }

    [Fact]
    public void NetworkResourceRoot_IsANetworkResourceTarget()
    {
        var root = Root(CatalogDocumentType.NetworkResource, new PropertiesCollection
        {
            ["URL"] = "https://example.com/catalog.ascd"
        });

        var target = MediaTargetFactory.FromCatalogRoot(root);

        Assert.Equal(MediaTargetKind.NetworkResource, target.Kind);
        Assert.Equal("https://example.com/catalog.ascd", target.Location);
    }

    [Fact]
    public void DriveRoot_IsAVolumeTarget()
    {
        var root = Root(CatalogDocumentType.Media, new PropertiesCollection
        {
            ["RootDirectory"] = "E:\\",
            ["DriveType"] = "Fixed"
        });

        var target = MediaTargetFactory.FromCatalogRoot(root);

        Assert.Equal(MediaTargetKind.Volume, target.Kind);
        Assert.Equal("E:\\", target.Location);
    }

    [Fact]
    public void OpticalDriveRoot_IsAnOpticalDiscTarget()
    {
        var root = Root(CatalogDocumentType.Media, new PropertiesCollection
        {
            ["RootDirectory"] = "E:\\",
            ["DriveType"] = "CDRom"
        });

        var target = MediaTargetFactory.FromCatalogRoot(root);

        Assert.Equal(MediaTargetKind.OpticalDisc, target.Kind);
    }

    [Fact]
    public void AddresslessFolder_IsASourceFolderTarget()
    {
        var root = Root(CatalogDocumentType.Folder);

        var target = MediaTargetFactory.FromCatalogRoot(root);

        Assert.Equal(MediaTargetKind.SourceFolder, target.Kind);
        Assert.Null(target.Location);
    }

    [Fact]
    public void Facts_SnapshotCatalogPropertiesAsInvariantText()
    {
        var root = Root(CatalogDocumentType.Media, new PropertiesCollection
        {
            ["RootDirectory"] = "E:\\",
            ["DriveType"] = "Fixed",
            ["TotalSize"] = 42L
        });

        var target = MediaTargetFactory.FromCatalogRoot(root);

        Assert.Equal("Fixed", target.Facts?["DriveType"]);
        Assert.Equal("42", target.Facts?["TotalSize"]);
    }

    [Fact]
    public void Drive_ClassifiesOpticalAndFilesystemVolumes()
    {
        var optical = new MediaDriveInfo("E:\\", "Disc", "E:\\", DriveType.CDRom, true, 0, 0, 700_000_000, "CDFS");
        var fixedDisk = new MediaDriveInfo("C:\\", "System", "C:\\", DriveType.Fixed, true, 0, 0, 500_000_000_000,
            "NTFS");

        Assert.Equal(MediaTargetKind.OpticalDisc, MediaTargetFactory.FromDrive(optical, "Disc").Kind);
        Assert.Equal(MediaTargetKind.Volume, MediaTargetFactory.FromDrive(fixedDisk, "System").Kind);
    }
}
