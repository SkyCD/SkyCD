using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Fonts.Models;

namespace SkyCD.Plugin.Metadata.Fonts.Tests;

/// <summary>
/// Builds minimal sfnt fonts and collections carrying only a name table, which is all the metadata reader
/// needs. Glyph outlines are irrelevant here, and a deliberately damaged buffer covers the failure paths.
/// </summary>
internal static class FontFixtures
{
    private const ushort WindowsUnicode = 3;
    private const ushort EnglishUnitedStates = 0x0409;

    public static byte[] Sfnt(
        string family,
        string? subfamily = "Regular",
        string? fullName = null,
        string? version = null,
        string? copyright = null,
        string? postScriptName = null,
        uint sfntVersion = 0x00010000)
    {
        var names = new List<(ushort Id, string Value)> { (1, family) };
        if (!string.IsNullOrEmpty(subfamily))
        {
            names.Add((2, subfamily));
        }

        names.Add((4, fullName ?? $"{family} {subfamily}".Trim()));
        if (version is not null)
        {
            names.Add((5, version));
        }

        if (copyright is not null)
        {
            names.Add((0, copyright));
        }

        if (postScriptName is not null)
        {
            names.Add((6, postScriptName));
        }

        var nameTable = BuildNameTable(names);
        var tableOffset = 12 + 16;
        var font = new byte[tableOffset + nameTable.Length];

        WriteUInt32(font, 0, sfntVersion);
        WriteUInt16(font, 4, 1);
        WriteUInt16(font, 6, 16);
        WriteUInt16(font, 8, 0);
        WriteUInt16(font, 10, 0);

        Encoding.ASCII.GetBytes("name").CopyTo(font, 12);
        WriteUInt32(font, 16, 0);
        WriteUInt32(font, 20, (uint)tableOffset);
        WriteUInt32(font, 24, (uint)nameTable.Length);
        nameTable.CopyTo(font, tableOffset);
        return font;
    }

    public static byte[] Collection(params byte[][] faces)
    {
        var headerLength = 12 + faces.Length * 4;
        using var buffer = new MemoryStream();
        buffer.Write(new byte[headerLength]);
        var offsets = new List<uint>();
        foreach (var face in faces)
        {
            offsets.Add((uint)buffer.Position);
            buffer.Write(face);
        }

        var data = buffer.ToArray();
        WriteUInt32(data, 0, 0x74746366);
        WriteUInt32(data, 4, 0x00010000);
        WriteUInt32(data, 8, (uint)faces.Length);
        for (var index = 0; index < offsets.Count; index++)
        {
            WriteUInt32(data, 12 + index * 4, offsets[index]);
            // Collection table offsets are absolute from the start of the collection.
            WriteUInt32(data, (int)offsets[index] + 20, offsets[index] + 28);
        }

        return data;
    }

    public static Dictionary<string, string?> Settings(
        bool collectionEnabled = true,
        bool previewsEnabled = false,
        string? previewText = null,
        string previewSize = "60",
        string previewLimitPerFolder = "0",
        string? extensions = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [FontSettings.CollectionEnabledKey] = collectionEnabled ? "true" : "false",
            [FontSettings.PreviewsEnabledKey] = previewsEnabled ? "true" : "false",
            [FontSettings.PreviewSizeKey] = previewSize,
            [FontSettings.PreviewLimitPerFolderKey] = previewLimitPerFolder
        };

        if (previewText is not null)
        {
            values[FontSettings.PreviewTextKey] = previewText;
        }

        if (extensions is not null)
        {
            values[FontSettings.ExtensionsKey] = extensions;
        }

        return values;
    }

    public static async Task<MediaExtractionResult> ExtractAsync(
        byte[] font,
        Dictionary<string, string?>? settings = null,
        string extension = ".ttf",
        CancellationToken cancellationToken = default)
    {
        var plugin = new FontMetadataPlugin();
        plugin.ApplySettings(settings ?? Settings());
        return await plugin.ExtractAsync(
            new MediaItem(extension, null, font.Length),
            new ByteArrayMediaSource(font),
            progress: null,
            cancellationToken);
    }

    public static async Task<MediaPreviewResult> GeneratePreviewAsync(
        byte[] font,
        Dictionary<string, string?>? settings = null,
        string? groupKey = null,
        CancellationToken cancellationToken = default)
    {
        var plugin = new FontPreviewPlugin(
            FontPluginOptions.Default,
            _ => SkiaSharp.SKTypeface.FromFamilyName(null));
        plugin.ApplySettings(settings ?? Settings(previewsEnabled: true));
        return await plugin.GeneratePreviewAsync(
            new MediaItem(".ttf", null, font.Length, groupKey),
            new ByteArrayMediaSource(font, budget: Math.Max(font.Length * 4, 1_000_000)),
            progress: null,
            cancellationToken);
    }

    private static byte[] BuildNameTable(IReadOnlyList<(ushort Id, string Value)> names)
    {
        using var strings = new MemoryStream();
        var records = new byte[names.Count * 12];
        for (var index = 0; index < names.Count; index++)
        {
            var encoded = Encoding.BigEndianUnicode.GetBytes(names[index].Value);
            var record = index * 12;
            WriteUInt16(records, record, WindowsUnicode);
            WriteUInt16(records, record + 2, 1);
            WriteUInt16(records, record + 4, EnglishUnitedStates);
            WriteUInt16(records, record + 6, names[index].Id);
            WriteUInt16(records, record + 8, (ushort)encoded.Length);
            WriteUInt16(records, record + 10, (ushort)strings.Position);
            strings.Write(encoded);
        }

        var stringOffset = 6 + records.Length;
        var table = new byte[stringOffset + strings.Length];
        WriteUInt16(table, 0, 0);
        WriteUInt16(table, 2, (ushort)names.Count);
        WriteUInt16(table, 4, (ushort)stringOffset);
        records.CopyTo(table, 6);
        strings.ToArray().CopyTo(table, stringOffset);
        return table;
    }

    private static void WriteUInt16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
