using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded;
using Xunit;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Import;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Models;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Tests.Ded;

public sealed class DedRoundTripTests
{
    private static DedDatabase Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return DedReader.Read(stream);
    }

    [Fact]
    public void RoundTrip_PreservesHierarchyAndMetadata()
    {
        var source = DedTestData.BuildSampleDatabase();
        var bytes = DedTestData.WriteToStream(source).ToArray();
        var reread = Read(bytes);

        Assert.Equal(source.Title, reread.Title);
        Assert.Equal(source.HeaderFlags, reread.HeaderFlags);
        Assert.Equal(source.ReservedFlags, reread.ReservedFlags);
        Assert.Equal(source.CreationFileTime, reread.CreationFileTime);
        Assert.Equal(source.Objects.Count, reread.Objects.Count);

        var file = reread.Find(6)!;
        Assert.Equal(DedObjectKind.File, file.Kind);
        Assert.Equal(6UL, file.ObjectId);
        Assert.Equal(4UL, file.ParentId);
        Assert.Equal("Café €.txt", file.Name.Value);
        Assert.Equal(DedStringEncoding.Windows1252, file.Name.Encoding);
        Assert.Equal("D:\\Setup\\Café €.txt", file.Path.Value);
        Assert.Equal("Grüße aus Europa", file.Description.Value);
        Assert.Equal(4_294_967_296UL + 1024, file.LogicalSize);
        Assert.Equal(4_294_967_296UL + 1024, file.AggregateSize);
        Assert.Equal(0x0000000Fu, file.Flags);
        Assert.Equal(new[] { 2UL }, file.CategoryIds);

        Assert.Collection(
            file.Extensions,
            extension =>
            {
                Assert.Equal(0x0000DEADu, extension.Tag);
                Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, extension.Data);
            },
            extension =>
            {
                Assert.Equal(0x0000BEEFu, extension.Tag);
                Assert.Equal("opaque", System.Text.Encoding.ASCII.GetString(extension.Data));
            });

        Assert.Equal(
            new[] { DedPayloadKind.StoredContent, DedPayloadKind.Thumbnail, DedPayloadKind.GeneratedDescription, DedPayloadKind.Unknown },
            file.Payloads.Select(payload => payload.Kind));
        Assert.Equal("file contents", System.Text.Encoding.ASCII.GetString(file.Payloads[0].Data));
        Assert.Equal(0x00000077u, file.Payloads[3].RawKind);

        var archive = reread.Find(5)!;
        Assert.Equal(ulong.MaxValue, archive.ModifiedFileTime);

        var folder = reread.Find(4)!;
        Assert.Equal(new[] { 5UL, 6UL }, folder.ChildIds);
        Assert.Equal(new[] { 2UL, 3UL, 7UL }, reread.Find(1)!.ChildIds);
    }

    [Fact]
    public void Write_IsDeterministic()
    {
        var database = DedTestData.BuildSampleDatabase();
        var first = DedTestData.WriteToStream(database).ToArray();
        var second = DedTestData.WriteToStream(DedTestData.BuildSampleDatabase()).ToArray();
        Assert.Equal(first, second);
    }

    [Fact]
    public void Rows_MapKindsParentsAndUnsignedSizes()
    {
        var database = DedTestData.BuildSampleDatabase();
        var document = DedCatalogMapper.BuildRows(database, "sample.ded");

        Assert.Equal(database.Objects.Count, document.Rows.Count);
        Assert.Equal("Sample Archive", document.Summary.Title);
        Assert.Equal(2, document.Summary.Volumes);
        Assert.Equal(1, document.Summary.Categories);
        Assert.Equal(1, document.Summary.Folders);
        Assert.Equal(1, document.Summary.Archives);
        Assert.Equal(2, document.Summary.Files);

        var rows = document.Rows.ToDictionary(row => (string)row["id"]!, StringComparer.Ordinal);
        Assert.Equal("media", rows["ded:3"]["kind"]);
        Assert.Equal("ded:1", rows["ded:3"]["parentId"]);
        Assert.Equal("folder", rows["ded:4"]["kind"]);
        Assert.Equal("file", rows["ded:5"]["kind"]);
        Assert.Equal("4294968320", rows["ded:6"]["sizeBytes"]);

        var properties = (Dictionary<string, object?>)rows["ded:6"]["properties"]!;
        Assert.Equal("4294968320", properties[DedCatalogMapper.LogicalSizeProperty]);
        Assert.Equal("ansi", properties[DedCatalogMapper.NameEncodingProperty]);
        Assert.Equal("2021-06-07", properties["Date"]);
        Assert.Equal("08:09:10", properties["Time"]);
        Assert.False(rows["ded:5"]["properties"] is Dictionary<string, object?> archiveProperties
            && archiveProperties.ContainsKey("Date"));
    }

    [Fact]
    public void Rows_ConvertBackToDatabaseLosslessly()
    {
        var source = DedTestData.BuildSampleDatabase();
        var document = DedCatalogMapper.BuildRows(source, "sample.ded");
        var rebuilt = DedCatalogMapper.ToDatabase(document.Rows, "Rebuilt");

        Assert.Equal("Rebuilt", rebuilt.Title);
        Assert.Equal(source.Objects.Count, rebuilt.Objects.Count);

        var file = rebuilt.Find(6)!;
        Assert.Equal("Café €.txt", file.Name.Value);
        Assert.Equal(4_294_967_296UL + 1024, file.LogicalSize);
        Assert.Equal(0x0000000Fu, file.Flags);
        Assert.Equal(4, file.Payloads.Count);
        Assert.Equal("file contents", System.Text.Encoding.ASCII.GetString(file.Payloads[0].Data));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, file.Extensions[0].Data);
        Assert.Equal(new[] { 5UL, 6UL }, rebuilt.Find(4)!.ChildIds);

        var bytes = DedTestData.WriteToStream(rebuilt).ToArray();
        var reread = Read(bytes);
        Assert.Equal(rebuilt.Objects.Count, reread.Objects.Count);
        Assert.Equal("Café €.txt", reread.Find(6)!.Name.Value);
    }

    [Fact]
    public void DeepHierarchy_ReadsAndWritesWithoutRecursion()
    {
        var database = new DedDatabase { Title = "Deep" };
        var root = new DedCatalogObject { Kind = DedObjectKind.Volume, ObjectId = 1, Name = DedText.FromAnsi("V") };
        database.Objects.Add(root);
        var parent = root;
        for (ulong id = 2; id <= 400; id++)
        {
            var folder = new DedCatalogObject
            {
                Kind = DedObjectKind.Folder,
                ObjectId = id,
                ParentId = parent.ObjectId,
                Name = DedText.FromAnsi("Level " + id)
            };
            parent.ChildIds.Add(id);
            database.Objects.Add(folder);
            parent = folder;
        }

        var bytes = DedTestData.WriteToStream(database).ToArray();
        var reread = Read(bytes);
        Assert.Equal(400, reread.Objects.Count);
        Assert.Equal(400UL, reread.Find(400)!.ObjectId);
        Assert.Equal(399UL, reread.Find(400)!.ParentId);
    }

    [Fact]
    public void EmptyDatabase_ReadsBackWithNoObjects()
    {
        var database = new DedDatabase { Title = "Empty" };
        var bytes = DedTestData.WriteToStream(database).ToArray();
        var reread = Read(bytes);

        Assert.Empty(reread.Objects);
        Assert.Equal("Empty", reread.Title);
        Assert.Equal(DedFormat.InitialTableSlots, reread.TableSlotCount);
    }

    [Fact]
    public void OpaqueExtras_SurviveReadModifyWriteByteForByte()
    {
        var database = DedTestData.BuildSampleDatabase();
        var file = database.Find(6)!;
        var rawKind = 99u;
        var raw = new byte[40];
        DedTestData.WriteUInt32(raw, 0, rawKind);
        DedTestData.WriteUInt32(raw, 4, 0xA5A5A5A5);
        DedTestData.WriteUInt32(raw, 8, (uint)raw.Length);
        DedTestData.WriteUInt64(raw, 12, 9);
        DedTestData.WriteUInt64(raw, 20, 1);
        database.Objects.Add(new DedCatalogObject
        {
            RawKind = rawKind,
            ObjectId = 9,
            ParentId = 1,
            Flags = 0xA5A5A5A5,
            RawRecord = raw
        });
        database.Find(1)!.ChildIds.Add(9);

        var first = Read(DedTestData.WriteToStream(database).ToArray());
        file = first.Find(6)!;
        var payloadBytes = file.Payloads.Select(payload => Convert.ToBase64String(payload.Data)).ToArray();
        var extensionBytes = file.Extensions.Select(extension => Convert.ToBase64String(extension.Data)).ToArray();
        var unknown = first.Find(9)!;
        Assert.Equal(raw, unknown.RawRecord);

        var second = Read(DedTestData.WriteToStream(first).ToArray());
        var updated = second.Find(6)!;
        Assert.Equal(payloadBytes, updated.Payloads.Select(payload => Convert.ToBase64String(payload.Data)));
        Assert.Equal(extensionBytes, updated.Extensions.Select(extension => Convert.ToBase64String(extension.Data)));
        Assert.Equal(0x0000000Fu, updated.Flags);
        Assert.Equal(raw, second.Find(9)!.RawRecord);
        Assert.Equal(0xA5A5A5A5u, second.Find(9)!.Flags);
    }
}
