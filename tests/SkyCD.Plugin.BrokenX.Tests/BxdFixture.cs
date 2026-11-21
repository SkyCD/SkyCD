using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.BrokenX.Export;
using SkyCD.Plugin.BrokenX.Models;
using SkyCD.Plugin.BrokenX.Parsing;

namespace SkyCD.Plugin.BrokenX.Tests;

/// <summary>
/// Builds license-safe Broken X database fixtures at runtime: synthetic trees serialized
/// with the plugin writer, plus byte-level mutations for malformed inputs.
/// </summary>
public static class BxdFixture
{
    /// <summary>
    /// The canonical empty v0x40 database, byte-for-byte from the format specification.
    /// </summary>
    public static byte[] CanonicalEmpty() => new byte[]
    {
        0x42, 0x72, 0x6F, 0x6B, 0x65, 0x6E, 0x20, 0x58, 0x20, 0x44, 0x61, 0x74, 0x61, 0x62, 0x61, 0x73,
        0x65, 0x0D, 0x0A, 0x1A, 0x00, 0x40, 0x03, 0x42, 0x01, 0x00, 0x00, 0x00, 0x4E, 0x01, 0x00, 0x00,
        0x00, 0x4F, 0x53, 0x09, 0x15, 0x04, 0x00, 0x00, 0x00, 0x00
    };

    public static BxdDatabase EmptyDatabase()
    {
        var database = new BxdDatabase();
        database.RootProperties.Add(new BxdProperty(0x42, 1u));
        database.RootProperties.Add(new BxdProperty(0x4E, 1u));
        database.RootProperties.Add(new BxdProperty(0x4F, 0x04150953u));
        return database;
    }

    public static BxdDatabase OneItemDatabase()
    {
        var database = EmptyDatabase();
        database.Roots.Add(new BxdItem
        {
            Flags = 0,
            Name = "readme.txt",
            Field14 = 7,
            Field18 = 9
        });
        return database;
    }

    public static BxdDatabase SiblingDatabase()
    {
        var database = EmptyDatabase();
        var docs = new BxdItem { Flags = 0, Name = "Docs", Field14 = 1, Field18 = 2 };
        docs.Children.Add(new BxdItem { Flags = 0, Name = "a.txt", Field14 = 3, Field18 = 4 });
        docs.Children.Add(new BxdItem { Flags = 0, Name = "b.txt", Field14 = 5, Field18 = 6 });
        database.Roots.Add(docs);
        database.Roots.Add(new BxdItem { Flags = 0, Name = "loose.txt", Field14 = 7, Field18 = 8 });
        return database;
    }

    public static BxdDatabase NestedDatabase(int depth = 3)
    {
        var database = EmptyDatabase();
        var current = new BxdItem { Flags = 0, Name = "level0" };
        database.Roots.Add(current);
        for (var i = 1; i < depth; i++)
        {
            var child = new BxdItem { Flags = 0, Name = $"level{i}" };
            current.Children.Add(child);
            current = child;
        }

        current.Children.Add(new BxdItem { Flags = 0x0002, Name = "deep.txt", Field14 = 0xFFFFFFFF, Field18 = 0 });
        return database;
    }

    public static BxdDatabase UnicodeDatabase()
    {
        var database = EmptyDatabase();
        database.Roots.Add(new BxdItem { Flags = 0, Name = "crème brûlée.txt", Field14 = 1, Field18 = 2 });
        var folder = new BxdItem { Flags = 0, Name = "Документы" };
        folder.Children.Add(new BxdItem { Flags = 0, Name = "日本語.txt" });
        database.Roots.Add(folder);
        return database;
    }

    public static BxdDatabase UnknownPropertyDatabase()
    {
        var database = EmptyDatabase();
        var item = new BxdItem { Flags = 0x0010, Name = "mystery.bin", Field14 = 0xDEAD, Field18 = 0xBEEF };
        item.Metadata.Add(new BxdProperty(0x45, 12345u));
        item.Metadata.Add(new BxdProperty(0x99, "suspicious", Encoding.UTF8.GetBytes("suspicious")));
        item.Metadata.Add(new BxdProperty(0xDA, new byte[] { 1, 2, 3, 4, 5 }));
        database.Roots.Add(item);
        database.RootProperties.Add(new BxdProperty(0xC0 | 0x07, new byte[] { 9, 9 }));
        return database;
    }

    public static BxdDatabase LargeBlobDatabase(int blobBytes = 1024 * 1024)
    {
        var database = EmptyDatabase();
        var item = new BxdItem { Flags = 0, Name = "big.bin" };
        var blob = new byte[blobBytes];
        for (var i = 0; i < blob.Length; i++)
        {
            blob[i] = (byte)(i & 0xFF);
        }

        item.Metadata.Add(new BxdProperty(0xC0 | 0x01, blob));
        database.Roots.Add(item);
        return database;
    }

    public static BxdDatabase EncryptedDatabase()
    {
        var database = EmptyDatabase();
        database.Roots.Add(new BxdItem { Flags = 0x0001, Name = "secret.txt" });
        return database;
    }

    public static BxdDatabase DeepDatabase(int depth)
    {
        var database = EmptyDatabase();
        var current = new BxdItem { Flags = 0, Name = "d0" };
        database.Roots.Add(current);
        for (var i = 1; i < depth; i++)
        {
            var child = new BxdItem { Flags = 0, Name = $"d{i}" };
            current.Children.Add(child);
            current = child;
        }

        return database;
    }

    public static byte[] Serialize(BxdDatabase database)
    {
        using var stream = new MemoryStream();
        BxdWriter.Write(stream, database);
        return stream.ToArray();
    }

    public static MemoryStream StreamOf(byte[] bytes) => new(bytes, writable: false);

    public static FileFormatReadRequest ReadRequest(Stream source, string fileName = "catalog.bxd")
    {
        return new FileFormatReadRequest
        {
            Source = source,
            FormatId = "brokenx-bxd",
            FileName = fileName,
            SourcePath = fileName
        };
    }

    public static byte[] Mutate(byte[] source, Action<byte[]> mutate)
    {
        var copy = (byte[])source.Clone();
        mutate(copy);
        return copy;
    }
}
