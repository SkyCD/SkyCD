using System;
using System.IO;
using UCanAccess.File;

namespace SkyCD.Plugin.MyCatalogue.Tests;

internal static class MyCatalogueFixture
{
    public static void Create(string path, string variant = "valid")
    {
        using var database = Database.Create(path, version: "2003");
        var disks = database.CreateTable("Disks",
        [
            Number("ID_Disk"), Number("Group_ID"), Text("DiskName"), Number("Directories"), Number("Files"),
            new ColumnBuilder("Bytes", DataType.Double), Text("Serial"), Number("Type"), Number("DriveNumber")
        ]);
        var groups = database.CreateTable("Groups", variant == "missing-column"
            ? [Number("ID_Group"), Number("Group_ID")]
            : [Number("ID_Group"), Number("Group_ID"), Text("GroupName")]);
        var items = database.CreateTable("Items",
        [
            Number("ID_Item"), Number("Disk_ID"), Text("ItemName"), Flag("IsDirectory"),
            new ColumnBuilder("Bytes", DataType.Double), Number("CRC32"), Flag("IsCRC32Calculated"),
            Number("Item_ID"), Date("DateCreated"), Date("DateModified")
        ]);
        var info = database.CreateTable("Info",
        [Number("ID_Info"), Number("VersionMajor"), Number("VersionMinor"), Text("LastSearchText"), Text("LastCatalogueExt")]);

        info.AddRow([1, 1, variant == "version" ? 9 : 2, null!, "mdb"]);
        if (variant == "missing-column") return;
        groups.AddRow([1, variant == "group-cycle" ? 2 : 0, "Music"]);
        groups.AddRow([2, 1, "Žanrai"]);
        groups.AddRow([3, 0, "Empty group"]);
        if (variant == "groups-only") return;
        disks.AddRow([1, variant == "missing-group" ? 99 : 2, "Disc α", 2, 2, 300d, "SN-01", 2, 3]);
        disks.AddRow([2, 0, "Disc β", 1, 1, null!, null!, 4, 0]);
        var timestamp = new DateTime(2021, 2, 3, 4, 5, 6, DateTimeKind.Unspecified);
        items.AddRow([1, 1, "Folder A", true, null!, 0, false, variant == "item-cycle" ? 3 : 0, timestamp, timestamp]);
        items.AddRow([variant == "null-id" ? null! : 2, 1, "same ü.txt", false, variant == "negative-size" ? -1d : 9876543210d,
            variant == "missing-crc" ? null! : int.MinValue, true,
            variant == "null-parent" ? null! : variant == "missing-parent" ? 99 : variant == "file-parent" ? 4 : 1, timestamp, timestamp]);
        items.AddRow([3, 1, "Folder B", true, null!, 0, false, variant == "item-cycle" ? 1 : 0, null!, null!]);
        items.AddRow([4, 1, "same ü.txt", false, null!, 1234, false, 3, null!, null!]);
        items.AddRow([5, 2, "Empty", true, null!, 0, false, variant == "cross-disk" ? 1 : 0, null!, null!]);
        items.AddRow([6, 2, "root.bin", false, 0d, 0, false, 0, null!, null!]);
        if (variant == "duplicate-id")
            items.AddRow([6, 2, "duplicate", false, 0d, 0, false, 0, null!, null!]);
    }

    public static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "skycd-mycatalogue-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static ColumnBuilder Number(string name) => new(name, DataType.Long);
    private static ColumnBuilder Text(string name) => new ColumnBuilder(name, DataType.Text).WithLength(510);
    private static ColumnBuilder Flag(string name) => new(name, DataType.Boolean);
    private static ColumnBuilder Date(string name) => new(name, DataType.ShortDateTime);
}
