using System;
using System.IO;
using SkyCD.Plugin.DiskExplorer.Ded;
using SkyCD.Plugin.DiskExplorer.Ded.Exceptions;
using Xunit;

namespace SkyCD.Plugin.DiskExplorer.Tests.Ded;

public sealed class DedValidationTests
{
    private static DedDatabase Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return DedReader.Read(stream);
    }

    private static byte[] SampleBytes() => DedTestData.WriteToStream(DedTestData.BuildSampleDatabase()).ToArray();

    [Fact]
    public void TruncatedHeader_FailsTyped()
    {
        Assert.Throws<DedCorruptException>(() => Read(new byte[10]));
    }

    [Fact]
    public void InvalidMagic_FailsTyped()
    {
        var bytes = SampleBytes();
        bytes[0] = (byte)'X';
        Assert.Throws<DedCorruptException>(() => Read(bytes));
    }

    [Fact]
    public void UnsupportedVersion_FailsTyped()
    {
        var bytes = SampleBytes();
        DedTestData.WriteUInt32(bytes, 0x04, DedFormat.CurrentVersion + 1);
        Assert.Throws<DedUnsupportedVersionException>(() => Read(bytes));
    }

    [Fact]
    public void HugeTableCount_FailsWithLimit()
    {
        var bytes = SampleBytes();
        DedTestData.WriteUInt32(bytes, 0x08, uint.MaxValue);
        Assert.Throws<DedLimitExceededException>(() => Read(bytes));
    }

    [Fact]
    public void TableBeyondFile_FailsTyped()
    {
        var bytes = SampleBytes();
        DedTestData.WriteUInt32(bytes, 0x08, 100_000);
        Assert.Throws<DedCorruptException>(() => Read(bytes));
    }

    [Fact]
    public void PopulatedCountMismatch_FailsTyped()
    {
        var bytes = SampleBytes();
        DedTestData.WriteUInt32(bytes, 0x0C, 1);
        Assert.Throws<DedCorruptException>(() => Read(bytes));
    }

    [Fact]
    public void OffsetOutsideFile_FailsTyped()
    {
        var bytes = SampleBytes();
        DedTestData.WriteUInt32(bytes, DedFormat.HeaderSize, (uint)bytes.Length + 32);
        Assert.Throws<DedCorruptException>(() => Read(bytes));
    }

    [Fact]
    public void PartiallyOverlappingRecords_FailTyped()
    {
        var bytes = SampleBytes();
        var first = DedTestData.ReadUInt32(bytes, DedFormat.HeaderSize);
        DedTestData.WriteUInt32(bytes, DedFormat.HeaderSize + 4, first + 4);
        Assert.ThrowsAny<InvalidOperationException>(() => Read(bytes));
    }

    [Fact]
    public void SharedOffset_IsReadOnce()
    {
        var item = new DedCatalogObject { Kind = DedObjectKind.Volume, ObjectId = 1, Name = DedText.FromAnsi("One") };
        var bytes = DedTestData.BuildContainer([DedRecordSerializer.Serialize(item)], 2);
        var first = DedTestData.ReadUInt32(bytes, DedFormat.HeaderSize);
        DedTestData.WriteUInt32(bytes, DedFormat.HeaderSize + 4, first);
        DedTestData.WriteUInt32(bytes, 0x0C, 1);
        var database = Read(bytes);
        Assert.Single(database.Objects);
        Assert.Equal(1UL, database.Objects[0].ObjectId);
        Assert.NotEqual(0u, first);
        Assert.Single(Read(DedTestData.WriteToStream(database).ToArray()).Objects);
    }

    [Fact]
    public void SparseOffsetTable_IsAccepted()
    {
        var database = new DedDatabase { Title = "Sparse" };
        var root = new DedCatalogObject { Kind = DedObjectKind.Volume, ObjectId = 1, Name = DedText.FromAnsi("One") };
        var file = new DedCatalogObject { Kind = DedObjectKind.File, ObjectId = 2, ParentId = 1, Name = DedText.FromAnsi("Two") };
        root.ChildIds.Add(2);
        database.Objects.Add(root);
        database.Objects.Add(file);
        var bytes = DedTestData.WriteToStream(database).ToArray();

        var secondOffset = DedTestData.ReadUInt32(bytes, DedFormat.HeaderSize + 4);
        DedTestData.WriteUInt32(bytes, DedFormat.HeaderSize + 4, 0);
        DedTestData.WriteUInt32(bytes, DedFormat.HeaderSize + (7 * 4), secondOffset);
        DedTestData.WriteUInt32(bytes, 0x0C, 2);

        var reread = Read(bytes);
        Assert.Equal(2, reread.Objects.Count);
        Assert.Equal(2UL, reread.Find(2)!.ObjectId);
    }

    [Fact]
    public void Cycle_IsRejected()
    {
        var first = new DedCatalogObject { Kind = DedObjectKind.Folder, ObjectId = 1, Name = DedText.FromAnsi("A") };
        var second = new DedCatalogObject { Kind = DedObjectKind.Folder, ObjectId = 2, Name = DedText.FromAnsi("B") };
        first.ChildIds.Add(2);
        second.ChildIds.Add(1);
        var bytes = DedTestData.BuildContainer(
            [DedRecordSerializer.Serialize(first), DedRecordSerializer.Serialize(second)],
            2);

        Assert.Throws<DedCorruptException>(() => Read(bytes));
    }

    [Fact]
    public void DuplicateObjectIds_AreRejected()
    {
        var first = new DedCatalogObject { Kind = DedObjectKind.File, ObjectId = 1, Name = DedText.FromAnsi("A") };
        var second = new DedCatalogObject { Kind = DedObjectKind.File, ObjectId = 1, Name = DedText.FromAnsi("B") };
        var bytes = DedTestData.BuildContainer(
            [DedRecordSerializer.Serialize(first), DedRecordSerializer.Serialize(second)],
            2);

        Assert.Throws<DedCorruptException>(() => Read(bytes));
    }

    [Fact]
    public void DanglingChildReference_IsRejected()
    {
        var item = new DedCatalogObject { Kind = DedObjectKind.Folder, ObjectId = 1, Name = DedText.FromAnsi("A") };
        item.ChildIds.Add(99);
        var bytes = DedTestData.BuildContainer([DedRecordSerializer.Serialize(item)], 1);
        Assert.Throws<DedCorruptException>(() => Read(bytes));
    }

    [Fact]
    public void InvalidUtf8Text_FailsTyped()
    {
        var item = new DedCatalogObject { Kind = DedObjectKind.File, ObjectId = 1, Name = DedText.FromUtf8("A") };
        var record = DedRecordSerializer.Serialize(item);
        DedTestData.WriteUInt32(record, 28, (uint)DedStringEncoding.Utf8);
        record[36] = 0xFF;
        var bytes = DedTestData.BuildContainer([record], 1);
        Assert.Throws<DedTextEncodingException>(() => Read(bytes));
    }

    [Fact]
    public void PayloadLengthBeyondRecord_FailsTyped()
    {
        var item = new DedCatalogObject { Kind = DedObjectKind.File, ObjectId = 1, Name = DedText.FromAnsi("A") };
        item.Payloads.Add(new DedPayload(DedPayloadKind.StoredContent, [1, 2, 3, 4]));
        var record = DedRecordSerializer.Serialize(item);
        DedTestData.WriteUInt32(record, record.Length - 12, 1000);
        var bytes = DedTestData.BuildContainer([record], 1);
        Assert.Throws<DedCorruptException>(() => Read(bytes));
    }

    [Fact]
    public void RecordLengthBeyondFile_FailsTyped()
    {
        var bytes = SampleBytes();
        var first = DedTestData.ReadUInt32(bytes, DedFormat.HeaderSize);
        DedTestData.WriteUInt32(bytes, (int)first + 8, (uint)bytes.Length);
        Assert.ThrowsAny<InvalidOperationException>(() => Read(bytes));
    }

    [Fact]
    public void PasswordProtectedDatabase_RequiresPassword()
    {
        var database = DedTestData.BuildSampleDatabase();
        database.HeaderFlags |= DedFormat.HeaderFlagPasswordProtected;
        var bytes = DedTestData.WriteToStream(database).ToArray();

        using var withoutPassword = new MemoryStream(bytes);
        Assert.Throws<DedPasswordRequiredException>(() => DedReader.Read(withoutPassword));

        using var withPassword = new MemoryStream(bytes);
        Assert.Throws<DedPasswordInvalidException>(() =>
            DedReader.Read(withPassword, new DedReadOptions { Password = new byte[] { 1, 2, 3 } }));
    }

    [Fact]
    public void Cancellation_IsObserved()
    {
        var bytes = SampleBytes();
        using var stream = new MemoryStream(bytes);
        using var source = new System.Threading.CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => DedReader.Read(stream, cancellationToken: source.Token));

        using var target = new MemoryStream();
        Assert.Throws<OperationCanceledException>(() =>
            DedWriter.Write(target, DedTestData.BuildSampleDatabase(), source.Token));
    }

    [Fact]
    public void FileWriteIsAtomicAndLeavesNoTemporarySibling()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "skycd-ded-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "catalog.ded");
            DedWriter.WriteFile(path, DedTestData.BuildSampleDatabase());

            using (var stream = File.OpenRead(path))
            {
                var database = DedReader.Read(stream);
                Assert.Equal("Sample Archive", database.Title);
            }

            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

            var replacement = DedTestData.BuildSampleDatabase();
            replacement.Title = "Replacement";
            DedWriter.WriteFile(path, replacement);
            using var replaced = File.OpenRead(path);
            Assert.Equal("Replacement", DedReader.Read(replaced).Title);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
