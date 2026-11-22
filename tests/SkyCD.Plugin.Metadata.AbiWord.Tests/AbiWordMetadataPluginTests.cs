using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.AbiWord.Exceptions;
using Xunit;
using SkyCD.Plugin.Metadata.AbiWord.Models;

namespace SkyCD.Plugin.Metadata.AbiWord.Tests;

public sealed class AbiWordMetadataPluginTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractsMetadataFromPlainAndCompressedDocuments(bool compressed)
    {
        var data = Encoding.UTF8.GetBytes("""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE abiword PUBLIC "-//ABISOURCE//DTD AWML 1.0 Strict//EN" "http://www.abisource.com/awml.dtd">
            <abiword xmlns="http://www.abisource.com/awml.dtd" version="1.0">
              <metadata>
                <m key="dc.title"> A &amp; B </m><m key="dc.creator">Živilė</m>
                <m key="dc.subject">Subject</m><m key="dc.description"><![CDATA[<inert> text]]></m>
                <m key="abiword.keywords">one, two</m><m key="dc.language">lt-LT</m>
                <m key="dc.date">2026-11-20</m><m key="abiword.generator">AbiWord</m>
              </metadata><section><p>Not metadata</p></section>
            </abiword>
            """);
        var result = await ExtractAsync(compressed ? Compress(data) : data, compressed ? ".zabw" : ".abw");
        Assert.Equal("A & B", Text(result, AbiWordSchema.Title));
        Assert.Equal("Živilė", Text(result, AbiWordSchema.Author));
        Assert.Equal("Subject", Text(result, AbiWordSchema.Subject));
        Assert.Equal("<inert> text", Text(result, AbiWordSchema.Description));
        Assert.Equal("one, two", Text(result, AbiWordSchema.Keywords));
        Assert.Equal("lt-LT", Text(result, AbiWordSchema.Language));
        Assert.Equal("2026-11-20", Text(result, AbiWordSchema.Date));
        Assert.Equal("AbiWord", Text(result, AbiWordSchema.Generator));
        Assert.Equal("1.0", Text(result, AbiWordSchema.FormatVersion));
        Assert.Equal("abw", Text(result, AbiWordSchema.Container));
        Assert.All(result.Values, value => Assert.Contains(AbiWordSchema.Metadata.Fields,
            field => field.Key == value.Key && field.Type == value.Type));
    }

    [Theory]
    [InlineData("<abiword/>")]
    [InlineData("<abiword><metadata/></abiword>")]
    [InlineData("<abiword><metadata><m key='dc.title'> </m><m key='unknown'>ignored</m></metadata></abiword>")]
    public async Task MissingOrEmptyMetadataIsOmitted(string xml)
    {
        Assert.Equal(AbiWordSchema.Container, Assert.Single((await ExtractXmlAsync(xml)).Values).Key);
    }

    [Fact]
    public async Task DuplicateFieldsKeepLastNonemptyValueAndIgnoreBodyMetadata()
    {
        var result = await ExtractXmlAsync("""
            <abiword><metadata><m key="dc.title">first</m><m key="dc.title">last</m>
            <m key="dc.title"> </m></metadata><section><metadata><m key="dc.creator">body</m>
            </metadata></section><metadata xmlns="urn:other"><m key="dc.title">wrong</m></metadata></abiword>
            """);
        Assert.Equal("last", Text(result, AbiWordSchema.Title));
        Assert.DoesNotContain(result.Values, value => value.Key == AbiWordSchema.Author);
        Assert.Equal(2, result.Values.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not XML")]
    [InlineData("<document/>")]
    [InlineData("<abiword xmlns='urn:other'/>")]
    [InlineData("<abiword><metadata><m key='dc.title'>partial</m></metadata><section>")]
    [InlineData("<abiword/><trailing/>")]
    [InlineData("<abiword><metadata><m key='dc.title'><nested/></m></metadata></abiword>")]
    public async Task DamagedOrUnsupportedDocumentsDoNotPublishPartialMetadata(string xml)
    {
        await Assert.ThrowsAsync<AbiWordDecodeException>(() => ExtractXmlAsync(xml));
    }

    [Theory]
    [InlineData("<!DOCTYPE abiword [<!ENTITY secret SYSTEM 'file:///not-accessible/secret'>]>")]
    [InlineData("<!DOCTYPE abiword [<!ENTITY secret 'expanded'>]>")]
    public async Task CustomEntitiesAreNeverExpanded(string doctype)
    {
        await Assert.ThrowsAsync<AbiWordDecodeException>(() => ExtractXmlAsync(
            doctype + "<abiword><metadata><m key='dc.title'>&secret;</m></metadata></abiword>"));
    }

    [Fact]
    public async Task StyleLocksDoNotPreventReadingAndDatesRetainTheirMeaning()
    {
        var result = await ExtractXmlAsync("""
            <abiword styles="locked"><metadata>
            <m key="dc.date">Thu Nov 19 22:52:30 2015
            </m><m key="abiword.date_last_changed">Fri Nov 20 07:01:02 2015</m>
            </metadata></abiword>
            """);
        Assert.Equal("Thu Nov 19 22:52:30 2015", Text(result, AbiWordSchema.Date));
        Assert.Equal("Fri Nov 20 07:01:02 2015", Text(result, AbiWordSchema.LastChanged));
    }

    [Fact]
    public async Task EncryptedContainerIsUnsupportedRatherThanTreatedAsXml()
    {
        await Assert.ThrowsAsync<AbiWordDecodeException>(() => ExtractAsync([0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public async Task TruncatedGzipTrailerIsRejected(int missingBytes)
    {
        var data = Compress(Encoding.UTF8.GetBytes("<abiword/>"));
        await Assert.ThrowsAsync<AbiWordDecodeException>(() => ExtractAsync(data[..^missingBytes]));
    }

    [Fact]
    public async Task DamagedGzipChecksumIsRejected()
    {
        var data = Compress(Encoding.UTF8.GetBytes("<abiword/>"));
        data[^8] ^= 0xff;
        await Assert.ThrowsAsync<AbiWordDecodeException>(() => ExtractAsync(data));
    }

    [Fact]
    public async Task XmlEncodingDeclarationIsRespected()
    {
        var data = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(
            "<?xml version='1.0' encoding='UTF-16'?><abiword><metadata><m key='dc.title'>Ąžuolas</m></metadata></abiword>"))
            .ToArray();
        Assert.Equal("Ąžuolas", Text(await ExtractAsync(data), AbiWordSchema.Title));
    }

    [Fact]
    public async Task CompressedMagicIsRecognizedWithAbwExtension()
    {
        Assert.Equal("abw", Text(await ExtractAsync(Compress(Encoding.UTF8.GetBytes("<abiword/>"))), AbiWordSchema.Container));
    }

    [Fact]
    public async Task InvalidGzipIsRejected()
    {
        await Assert.ThrowsAsync<AbiWordDecodeException>(() => ExtractAsync([0x1f, 0x8b, 0, 0, 0, 0, 0, 0, 0, 0]));
    }

    [Fact]
    public async Task SourceLimitIsCheckedBeforeReading()
    {
        var source = new ByteSource(Encoding.UTF8.GetBytes("<abiword/>"));
        var plugin = new AbiWordMetadataPlugin(new AbiWordPluginOptions(maxSourceBytes: 4));
        await Assert.ThrowsAsync<AbiWordLimitExceededException>(() => plugin.ExtractAsync(
            new MediaItem(".abw", null, source.Length), source, null, CancellationToken.None));
        Assert.Equal(0, source.ReadCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecodedSizeIsBounded(bool compressed)
    {
        var data = Encoding.UTF8.GetBytes("<abiword><section><p>" + new string('a', 2000) + "</p></section></abiword>");
        await Assert.ThrowsAsync<AbiWordLimitExceededException>(() => ExtractAsync(
            compressed ? Compress(data) : data, options: new AbiWordPluginOptions(maxDecompressedBytes: 100)));
    }

    [Fact]
    public async Task FieldSizeIsBoundedAcrossTextAndCdata()
    {
        await Assert.ThrowsAsync<AbiWordLimitExceededException>(() => ExtractXmlAsync(
            "<abiword><metadata><m key='dc.title'>123<![CDATA[456]]></m></metadata></abiword>",
            new AbiWordPluginOptions(maxMetadataCharacters: 5)));
    }

    [Fact]
    public async Task XmlDepthIsBounded()
    {
        await Assert.ThrowsAsync<AbiWordLimitExceededException>(() => ExtractXmlAsync(
            "<abiword>" + string.Concat(Enumerable.Repeat("<section>", 65)) +
            string.Concat(Enumerable.Repeat("</section>", 65)) + "</abiword>"));
    }

    [Fact]
    public async Task ShortSourceReadIsRejected()
    {
        var source = new ByteSource(Encoding.UTF8.GetBytes("<abiword/>"), shortRead: true);
        await Assert.ThrowsAsync<AbiWordDecodeException>(() => new AbiWordMetadataPlugin().ExtractAsync(
            new MediaItem(".abw", null, source.Length), source, null, CancellationToken.None));
    }

    [Fact]
    public async Task CancellationIsObservedDuringSourceReading()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new ByteSource(Encoding.UTF8.GetBytes("<abiword>" + new string(' ', 70000) + "</abiword>"),
            afterRead: cancellation.Cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AbiWordMetadataPlugin().ExtractAsync(
            new MediaItem(".abw", null, source.Length), source, null, cancellation.Token));
        Assert.Equal(1, source.ReadCount);
    }

    [Fact]
    public async Task CancellationIsObservedBeforeParsing()
    {
        using var cancellation = new CancellationTokenSource();
        var data = Encoding.UTF8.GetBytes("<abiword/>");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AbiWordMetadataPlugin().ExtractAsync(
            new MediaItem(".abw", null, data.Length), new ByteSource(data),
            new CancelProgress(cancellation), cancellation.Token));
    }

    [Fact]
    public void OptionsRequirePositiveLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AbiWordPluginOptions(maxSourceBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AbiWordPluginOptions(maxDecompressedBytes: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AbiWordPluginOptions(maxMetadataCharacters: 0));
    }

    private static string? Text(MediaExtractionResult result, string key) => result.Values.Single(value => value.Key == key).Text;

    private static Task<MediaExtractionResult> ExtractXmlAsync(string xml, AbiWordPluginOptions? options = null) =>
        ExtractAsync(Encoding.UTF8.GetBytes(xml), options: options);

    private static Task<MediaExtractionResult> ExtractAsync(byte[] data, string extension = ".abw", AbiWordPluginOptions? options = null) =>
        new AbiWordMetadataPlugin(options ?? AbiWordPluginOptions.Default).ExtractAsync(
            new MediaItem(extension, null, data.Length), new ByteSource(data), null, CancellationToken.None);

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }

    private sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<MediaProgress>
    {
        public void Report(MediaProgress value) => cancellation.Cancel();
    }

    private sealed class ByteSource(byte[] bytes, bool shortRead = false, Action? afterRead = null) : IMediaSource
    {
        public long Length => bytes.Length;
        public int ReadCount { get; private set; }

        public ValueTask<ReadOnlyMemory<byte>> ReadAsync(long offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            afterRead?.Invoke();
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(bytes.AsMemory((int)offset, shortRead ? count - 1 : count));
        }
    }
}
