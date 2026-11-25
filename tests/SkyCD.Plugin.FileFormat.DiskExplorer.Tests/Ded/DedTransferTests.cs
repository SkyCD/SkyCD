using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Exceptions;
using Xunit;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Import;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Models;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Tests.Ded;

public sealed class DedTransferTests
{
    private static DedDatabase Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return DedReader.Read(stream);
    }

    [Fact]
    public void SelectVolumes_CopiesOnlyRequestedSubtree()
    {
        var source = DedTestData.BuildSampleDatabase();
        var selected = DedVolumeTransfer.SelectVolumes(source, [3]);

        Assert.Equal(new ulong[] { 3, 4, 5, 6 }, selected.Objects.Select(item => item.ObjectId));
        Assert.Equal(0UL, selected.Find(3)!.ParentId);
        Assert.Equal(source.Title, selected.Title);
        Assert.Equal(4, selected.Find(6)!.Payloads.Count);
        Assert.Equal(new ulong[] { 2 }, selected.Find(6)!.CategoryIds);

        var reread = Read(DedTestData.WriteToStream(selected).ToArray());
        Assert.Equal(4, reread.Objects.Count);
        Assert.Equal(0UL, reread.Find(3)!.ParentId);
    }

    [Fact]
    public void TransferOptions_OmitEachPayloadKind()
    {
        var source = DedTestData.BuildSampleDatabase();
        var withoutContents = DedVolumeTransfer.SelectVolumes(
            source,
            [3],
            new DedTransferOptions { IncludeContents = false });
        Assert.DoesNotContain(withoutContents.Find(6)!.Payloads, payload => payload.Kind == DedPayloadKind.StoredContent);
        Assert.Contains(withoutContents.Find(6)!.Payloads, payload => payload.Kind == DedPayloadKind.Thumbnail);
        Assert.Contains(withoutContents.Find(6)!.Payloads, payload => payload.Kind == DedPayloadKind.Unknown);

        var withoutThumbnails = DedVolumeTransfer.SelectVolumes(
            source,
            [3],
            new DedTransferOptions { IncludeThumbnails = false });
        Assert.DoesNotContain(withoutThumbnails.Find(6)!.Payloads, payload => payload.Kind == DedPayloadKind.Thumbnail);

        var withoutDescriptions = DedVolumeTransfer.SelectVolumes(
            source,
            [3],
            new DedTransferOptions { IncludeGeneratedDescriptions = false });
        Assert.DoesNotContain(
            withoutDescriptions.Find(6)!.Payloads,
            payload => payload.Kind == DedPayloadKind.GeneratedDescription);

        var withoutAny = DedVolumeTransfer.SelectVolumes(
            source,
            [3],
            new DedTransferOptions
            {
                IncludeContents = false,
                IncludeThumbnails = false,
                IncludeGeneratedDescriptions = false
            });
        Assert.Equal(new[] { DedPayloadKind.Unknown }, withoutAny.Find(6)!.Payloads.Select(payload => payload.Kind));
        Assert.Equal(0x00000077u, withoutAny.Find(6)!.Payloads[0].RawKind);
    }

    [Fact]
    public void Transfer_NeverModifiesSource()
    {
        var source = DedTestData.BuildSampleDatabase();
        var before = source.Objects.Count;
        var file = source.Find(6)!;
        var filePayloadCount = file.Payloads.Count;
        var volumeParent = source.Find(3)!.ParentId;

        _ = DedVolumeTransfer.SelectVolumes(
            source,
            [3],
            new DedTransferOptions
            {
                IncludeContents = false,
                IncludeThumbnails = false,
                IncludeGeneratedDescriptions = false
            });

        Assert.Equal(before, source.Objects.Count);
        Assert.Equal(filePayloadCount, source.Find(6)!.Payloads.Count);
        Assert.Equal(volumeParent, source.Find(3)!.ParentId);
    }

    [Fact]
    public void Merge_RemapsIdsIntoExistingDatabase()
    {
        var existing = new DedDatabase { Title = "Existing" };
        var existingVolume = new DedCatalogObject
        {
            Kind = DedObjectKind.Volume,
            ObjectId = 100,
            Name = DedText.FromAnsi("Existing volume")
        };
        existing.Objects.Add(existingVolume);

        var source = DedTestData.BuildSampleDatabase();
        var incoming = DedVolumeTransfer.SelectVolumes(source, [3]);
        var merged = DedVolumeTransfer.Merge(existing, incoming);

        Assert.Equal("Existing", merged.Title);
        Assert.Equal(existing.Objects.Count + incoming.Objects.Count, merged.Objects.Count);
        Assert.Equal(merged.Objects.Count, merged.Objects.Select(item => item.ObjectId).Distinct().Count());
        Assert.Equal(100UL, merged.Find(100)!.ObjectId);

        var reread = Read(DedTestData.WriteToStream(merged).ToArray());
        Assert.Equal(merged.Objects.Count, reread.Objects.Count);
        Assert.Equal(
            new[] { DedObjectKind.Volume, DedObjectKind.Folder, DedObjectKind.Archive, DedObjectKind.File },
            reread.Objects.Where(item => item.ObjectId != 100).Select(item => item.Kind));
    }

    [Fact]
    public void ImportRows_ContainOnlySelectedVolumeHierarchy()
    {
        var source = DedTestData.BuildSampleDatabase();
        var selected = DedVolumeTransfer.SelectVolumes(source, [3]);
        var document = DedCatalogMapper.BuildRows(selected, "selected.ded");

        Assert.Equal(4, document.Rows.Count);
        Assert.Equal(1, document.Summary.Volumes);
        Assert.Equal("Disc 1", document.Rows[0]["name"]);
        Assert.Null(document.Rows[0]["parentId"]);
        Assert.DoesNotContain(document.Rows, row => (string)row["name"]! == "Disc 2");
    }

    [Fact]
    public async Task Plugin_ExportsSelectedVolumesWithOmissions()
    {
        var source = DedTestData.BuildSampleDatabase();
        var plugin = new DedPlugin();
        using var target = new MemoryStream();

        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "disk-explorer-ded",
            FileName = "disc1.ded",
            Target = target,
            Payload = new DedExportRequest
            {
                Database = source,
                VolumeIds = [3],
                Title = "Only Disc 1",
                Options = new DedTransferOptions
                {
                    IncludeContents = false,
                    IncludeThumbnails = false,
                    IncludeGeneratedDescriptions = false
                }
            }
        });

        Assert.True(result.Success, result.Error);
        target.Position = 0;
        var reread = DedReader.Read(target);
        Assert.Equal("Only Disc 1", reread.Title);
        Assert.Equal(4, reread.Objects.Count);
        Assert.Equal(new[] { DedPayloadKind.Unknown }, reread.Find(6)!.Payloads.Select(payload => payload.Kind));
        Assert.Equal(4, source.Find(6)!.Payloads.Count);
    }

    [Fact]
    public void SelectVolumes_RejectsNonVolumeSelection()
    {
        var source = DedTestData.BuildSampleDatabase();
        Assert.Throws<DedTransferException>(() => DedVolumeTransfer.SelectVolumes(source, [6]));
        Assert.Throws<DedTransferException>(() => DedVolumeTransfer.SelectVolumes(source, [404]));
    }

    [Fact]
    public async Task ExportIntoExistingDatabase_AppendsSelection()
    {
        var source = DedTestData.BuildSampleDatabase();
        var existing = new DedDatabase { Title = "Target" };
        existing.Objects.Add(new DedCatalogObject
        {
            Kind = DedObjectKind.Volume,
            ObjectId = 50,
            Name = DedText.FromAnsi("First")
        });

        var plugin = new DedPlugin();
        using var target = new MemoryStream();
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "disk-explorer-ded",
            FileName = "appended.ded",
            Target = target,
            Payload = new DedExportRequest
            {
                Database = source,
                VolumeIds = [7],
                ExistingDatabase = existing
            }
        });

        Assert.True(result.Success, result.Error);
        target.Position = 0;
        var reread = DedReader.Read(target);
        Assert.Equal(3, reread.Objects.Count);
        Assert.Contains(reread.Objects, item => item.Name.Value == "Disc 2");
        Assert.Contains(reread.Objects, item => item.Name.Value == "First");
    }

    [Fact]
    public void ToRows_RejectsUnknownSelectionGraciously()
    {
        var source = DedTestData.BuildSampleDatabase();
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["nodeId"] = "x", ["parentId"] = "missing", ["kind"] = "file", ["name"] = "orphan.txt" }
        };
        var database = DedCatalogMapper.ToDatabase(rows, "Orphans");
        Assert.Single(database.Objects);
        Assert.Equal(0UL, database.Objects[0].ParentId);
        Assert.Equal(DedObjectKind.File, database.Objects[0].Kind);
    }
}
