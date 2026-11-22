using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.EComic.Models;

namespace SkyCD.Plugin.Metadata.EComic.Tests;

/// <summary>Builds comic archives and pages in memory; tests never touch the file system.</summary>
internal static class EComicFixtures
{
    public const string FullComicInfo = """
        <?xml version="1.0" encoding="utf-8"?>
        <ComicInfo>
          <Series>Saga</Series>
          <Title>Chapter One</Title>
          <Number>1</Number>
          <Volume>2012</Volume>
          <Writer>Brian K. Vaughan, Fiona Staples</Writer>
          <Publisher>Image Comics</Publisher>
          <Genre>Science Fiction</Genre>
          <Year>2012</Year>
          <PageCount>22</PageCount>
          <LanguageISO>en</LanguageISO>
          <Summary>A young family is caught between a war that will not end.</Summary>
        </ComicInfo>
        """;

    public static byte[] Jpeg(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
        return data.ToArray();
    }

    public static byte[] Cbz(params (string Name, byte[] Data)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                using var stream = entry.Open();
                stream.Write(data);
            }
        }

        return buffer.ToArray();
    }

    public static byte[] ComicInfoBytes(string? xml = null) =>
        Encoding.UTF8.GetBytes(xml ?? FullComicInfo);

    public static byte[] ComicWithPage(string? comicInfo = FullComicInfo, int pageWidth = 600, int pageHeight = 900)
    {
        var entries = new List<(string, byte[])>();
        if (comicInfo is not null)
        {
            entries.Add(("ComicInfo.xml", ComicInfoBytes(comicInfo)));
        }

        entries.Add(("001.jpg", Jpeg(pageWidth, pageHeight, SKColors.OrangeRed)));
        return Cbz([.. entries]);
    }

    public static EComicSettings Settings(
        bool collectionEnabled = true,
        EComicNameSource nameSource = EComicNameSource.FileName,
        bool tagWriters = false,
        bool extractCovers = false,
        string coverSize = "720",
        string? extensions = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [EComicSettings.CollectionEnabledKey] = collectionEnabled ? "true" : "false",
            [EComicSettings.NameSourceKey] = nameSource switch
            {
                EComicNameSource.Series => "series",
                EComicNameSource.Title => "title",
                _ => "filename"
            },
            [EComicSettings.TagWritersKey] = tagWriters ? "true" : "false",
            [EComicSettings.ExtractCoversKey] = extractCovers ? "true" : "false",
            [EComicSettings.CoverSizeKey] = coverSize
        };
        if (extensions is not null)
        {
            values[EComicSettings.ExtensionsKey] = extensions;
        }

        return EComicSettings.From(values);
    }

    public static async Task<MediaExtractionResult> ExtractMetadataAsync(
        byte[] archive,
        string extension = ".cbz",
        EComicSettings? settings = null,
        EComicPluginOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var plugin = new EComicMetadataPlugin(options ?? EComicPluginOptions.Default);
        if (settings is not null)
        {
            plugin.ApplySettings(settings.ToDictionary());
        }

        return await plugin.ExtractAsync(
            new MediaItem(extension, null, archive.Length),
            new ByteArrayMediaSource(archive),
            progress: null,
            cancellationToken);
    }

    public static async Task<MediaPreviewResult> GenerateCoverAsync(
        byte[] archive,
        EComicSettings? settings = null,
        EComicPluginOptions? options = null,
        string extension = ".cbz",
        CancellationToken cancellationToken = default)
    {
        var plugin = new EComicCoverPlugin(options ?? EComicPluginOptions.Default);
        plugin.ApplySettings((settings ?? Settings(extractCovers: true)).ToDictionary());

        return await plugin.GeneratePreviewAsync(
            new MediaItem(extension, null, archive.Length),
            new ByteArrayMediaSource(archive),
            progress: null,
            cancellationToken);
    }

    public static SKBitmap Decode(ReadOnlyMemory<byte> data)
    {
        using var skData = SKData.CreateCopy(data.Span);
        return SKBitmap.Decode(skData) ?? throw new InvalidOperationException("The artifact is not a decodable image.");
    }
}
