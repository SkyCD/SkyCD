using System.Collections.Generic;
using System.IO;
using System.Text;
using SkyCD.Plugin.FileFormat.Adc;
using SkyCD.Plugin.FileFormat.Adc.Models;

namespace SkyCD.Plugin.FileFormat.Adc.Tests;

/// <summary>
/// Serializes synthetic catalogs using the documented layout so redistributable fixtures can
/// be checked in. This writer exists only in the test project; the plugin itself is read-only.
/// </summary>
public static class AdcFixtureWriter
{
    public static byte[] Write(AdcCatalog catalog, IReadOnlyList<AdcRawRecord>? extraRecords = null)
    {
        var body = BuildBody(catalog, extraRecords);
        var commentTable = BuildCommentTable(catalog.Comments);
        var bodyOffset = AdcFormat.HeaderSize;
        var commentTableOffset = bodyOffset + body.Length;

        using var file = new MemoryStream();
        using (var writer = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(AdcFormat.SignatureLength);
            writer.Write(AdcFormat.Signature);
            writer.Write(catalog.VersionMajor);
            writer.Write(catalog.VersionMinor);
            writer.Write(catalog.Flags);
            writer.Write((uint)(catalog.Volumes.Count + catalog.Categories.Count + catalog.Items.Count +
                                (extraRecords?.Count ?? 0)));
            writer.Write((ulong)bodyOffset);
            writer.Write((ulong)commentTableOffset);
            writer.Write((uint)catalog.Comments.Count);
            writer.Write((uint)catalog.Categories.Count);
            writer.Write(0u);
            writer.Write(body);
            writer.Write(commentTable);
        }

        return file.ToArray();
    }

    private static byte[] BuildBody(AdcCatalog catalog, IReadOnlyList<AdcRawRecord>? extraRecords)
    {
        using var body = new MemoryStream();
        using (var writer = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var volume in catalog.Volumes)
            {
                WriteVolume(writer, volume);
            }

            foreach (var category in catalog.Categories)
            {
                WriteCategory(writer, category);
            }

            foreach (var item in catalog.Items)
            {
                WriteItem(writer, item);
            }

            if (extraRecords is not null)
            {
                foreach (var record in extraRecords)
                {
                    writer.Write(record.Type);
                    writer.Write((uint)record.Payload.Length);
                    writer.Write(record.Payload);
                }
            }
        }

        return body.ToArray();
    }

    private static void WriteVolume(BinaryWriter writer, AdcVolume volume)
    {
        WriteRecordHeader(writer, AdcFormat.RecordTypeVolume, payload =>
        {
            var record = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true);
            record.Write(volume.VolumeId);
            record.Write((byte)volume.VolumeType);
            record.Write(volume.Flags);
            record.Write((ushort)0);
            record.Write(volume.SerialNumber);
            record.Write(volume.CapacityBytes);
            WriteString(record, volume.Label);
            WriteString(record, volume.Source);
            WriteProperties(record, volume.Properties);
        });
    }

    private static void WriteCategory(BinaryWriter writer, AdcCategory category)
    {
        WriteRecordHeader(writer, AdcFormat.RecordTypeCategory, payload =>
        {
            var record = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true);
            record.Write(category.CategoryId);
            WriteString(record, category.Name);
        });
    }

    private static void WriteItem(BinaryWriter writer, AdcItem item)
    {
        WriteRecordHeader(writer, AdcFormat.RecordTypeItem, payload =>
        {
            var record = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true);
            record.Write(item.ItemId);
            record.Write(item.ParentId);
            record.Write(item.VolumeId);
            record.Write((byte)item.Kind);
            record.Write(item.Attributes);
            record.Write((ushort)0);
            record.Write(item.TypeCode);
            record.Write(item.SizeBytes);
            record.Write(item.CreatedFileTime);
            record.Write(item.ModifiedFileTime);
            record.Write(item.AccessedFileTime);
            record.Write(item.CommentIndex);
            record.Write((ushort)item.CategoryIds.Count);
            foreach (var categoryId in item.CategoryIds)
            {
                record.Write(categoryId);
            }

            WriteString(record, item.Name);
            WriteProperties(record, item.Properties);
        });
    }

    private static void WriteRecordHeader(
        BinaryWriter writer,
        uint type,
        System.Action<MemoryStream> writePayload)
    {
        using var payload = new MemoryStream();
        writePayload(payload);
        writer.Write(type);
        writer.Write((uint)payload.Length);
        writer.Write(payload.ToArray());
    }

    private static void WriteProperties(BinaryWriter writer, List<AdcProperty> properties)
    {
        writer.Write((ushort)properties.Count);
        foreach (var property in properties)
        {
            writer.Write(property.Descriptor);
            switch (property.Kind)
            {
                case AdcPropertyKind.Uint32:
                    writer.Write(property.UintValue);
                    break;
                case AdcPropertyKind.String:
                    WriteString(writer, property.Text ?? string.Empty);
                    break;
                case AdcPropertyKind.Blob:
                    writer.Write((uint)(property.Blob?.Length ?? 0));
                    writer.Write(property.Blob ?? []);
                    break;
            }
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        writer.Write((uint)(bytes.Length / 2));
        writer.Write(bytes);
    }

    private static byte[] BuildCommentTable(List<string> comments)
    {
        using var table = new MemoryStream();
        using (var writer = new BinaryWriter(table, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((uint)comments.Count);
            var offsets = new uint[comments.Count];
            var lengths = new uint[comments.Count];
            var dataOffset = (uint)(4 + comments.Count * 8);
            var running = dataOffset;
            for (var i = 0; i < comments.Count; i++)
            {
                var bytes = Encoding.Unicode.GetBytes(comments[i]);
                offsets[i] = running;
                lengths[i] = (uint)bytes.Length;
                running += (uint)bytes.Length;
            }

            for (var i = 0; i < comments.Count; i++)
            {
                writer.Write(offsets[i]);
                writer.Write(lengths[i]);
            }

            foreach (var comment in comments)
            {
                writer.Write(Encoding.Unicode.GetBytes(comment));
            }
        }

        return table.ToArray();
    }
}

/// <summary>
/// An extra framed record used to exercise unknown-record tolerance.
/// </summary>
public sealed record AdcRawRecord(uint Type, byte[] Payload);
