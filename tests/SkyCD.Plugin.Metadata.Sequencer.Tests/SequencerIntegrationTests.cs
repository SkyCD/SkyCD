using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using SkyCD.SDK.Capabilities.Media;
using SkiaSharp;
using Xunit;

namespace SkyCD.Plugin.Metadata.Sequencer.Tests;

public sealed class SequencerIntegrationTests
{
    [Theory]
    [InlineData(".als", "<Ableton Creator='Live 12'><LiveSet><Tracks><AudioTrack/></Tracks><Tempo><Manual Value='132'/></Tempo></LiveSet></Ableton>", 132)]
    [InlineData(".ardour", "<Session version='7000'><TempoMap><Tempo npm='112'/></TempoMap><Routes><Route audio-playlist='1'/></Routes></Session>", 112)]
    [InlineData(".mmp", "<lmms creatorversion='1.2'><head bpm='104'/><song/></lmms>", 104)]
    [InlineData(".rg", "<rosegarden-data version='1'><composition><tempo bph='6300'/></composition></rosegarden-data>", 105)]
    [InlineData(".qtr", "<session version='1'><properties><tempo>125</tempo></properties></session>", 125)]
    [InlineData(".tracktionedit", "<EDIT appVersion='1'><TEMPOSEQUENCE><TEMPO bpm='116'/></TEMPOSEQUENCE></EDIT>", 116)]
    [InlineData(".h2song", "<song><version>1</version><bpm>90</bpm><name>Beat</name><author>Artist</author></song>", 90)]
    public async Task ProjectXmlPublishesTempo(string extension, string xml, double tempo)
    {
        var bytes = Encoding.UTF8.GetBytes(xml);
        var result = await Extract(new SequencerProjectMetadataPlugin(), extension, bytes);
        Assert.Equal(tempo, result["Tempo"].Number);
    }

    [Fact]
    public async Task ReaperCountsOnlyProjectTracksAndTheirItems()
    {
        var bytes = """
            <REAPER_PROJECT 0.1 "7.0/win64" 0
              TEMPO 120 4 4
              SAMPLERATE 48000 0 0
              MARKER 1 0 "first"
              <TRACK
                NAME "Track <One>"
                <ITEM
                >
              >
            >
            """u8.ToArray();
        var facts = await Extract(new SequencerProjectMetadataPlugin(), ".rpp", bytes);
        Assert.Equal("7.0/win64", facts["ApplicationVersion"].Text);
        Assert.Equal(1, facts["TrackCount"].Integer);
        Assert.Equal(1, facts["ClipCount"].Integer);
        Assert.Equal(1, facts["MarkerCount"].Integer);
        Assert.Equal(48000, facts["SampleRate"].Integer);
        await AssertPreview(new SequencerProjectPreviewPlugin(), ".rpp", bytes);
    }

    [Fact]
    public async Task FlpRetainsFactsFromCompleteEventsWhenDataIsTruncated()
    {
        var bytes = new byte[33];
        "FLhd"u8.CopyTo(bytes);
        bytes[4] = 6;
        bytes[10] = 2;
        bytes[12] = 96;
        "FLdt"u8.CopyTo(bytes.AsSpan(14));
        bytes[18] = 20;
        new byte[] { 65, 1, 0, 156, 0xc0, 0xd4, 1, 0, 194, 7, 65 }.CopyTo(bytes, 22);
        var facts = await Extract(new SequencerProjectMetadataPlugin(), ".flp", bytes);
        Assert.Equal(120, facts["Tempo"].Number);
        Assert.Equal(1, facts["PatternCount"].Integer);
        Assert.True(facts["Truncated"].Boolean);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MidiRecognizesSmfAndRiffAndRendersDeterministically(bool riff)
    {
        var bytes = Convert.FromHexString("4D546864000000060000000101E04D54726B0000000D00903C648360803C0000FF2F00");
        var extension = riff ? ".rmi" : ".mid";
        if (riff)
        {
            var wrapped = new byte[20 + bytes.Length + (bytes.Length & 1)];
            "RIFF"u8.CopyTo(wrapped);
            BinaryPrimitives.WriteInt32LittleEndian(wrapped.AsSpan(4), wrapped.Length - 8);
            "RMIDdata"u8.CopyTo(wrapped.AsSpan(8));
            BinaryPrimitives.WriteInt32LittleEndian(wrapped.AsSpan(16), bytes.Length);
            bytes.CopyTo(wrapped, 20);
            bytes = wrapped;
        }
        var facts = await Extract(new SequencerMidiMetadataPlugin(), extension, bytes);
        Assert.Equal(1, facts["TrackCount"].Integer);
        Assert.Equal(1, facts["NoteCount"].Integer);
        Assert.Equal(0.5, facts["Duration"].Number);
        await AssertPreview(new SequencerMidiPreviewPlugin(), extension, bytes);
    }

    [Fact]
    public async Task MidiEventLimitKeepsHeaderFacts()
    {
        var events = Enumerable.Range(0, 250_001).SelectMany(_ => new byte[] { 0, 0x90, 60, 100 }).ToArray();
        var header = Convert.FromHexString("4D546864000000060000000101E04D54726B00000000");
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(18), events.Length + 4);
        var facts = await Extract(new SequencerMidiMetadataPlugin(), ".mid", [.. header, .. events, 0, 255, 47, 0]);
        Assert.True(facts["Truncated"].Boolean);
        Assert.Equal(1, facts["TrackCount"].Integer);
        Assert.DoesNotContain("NoteCount", facts.Keys);
        Assert.DoesNotContain("Duration", facts.Keys);
    }

    [Fact]
    public async Task TrackerValidatesSignatureAndRejectsLyingPatternTable()
    {
        var bytes = new byte[2108];
        "Fixture MOD"u8.CopyTo(bytes);
        bytes[950] = 1;
        "M.K."u8.CopyTo(bytes.AsSpan(1080));
        var facts = await Extract(new SequencerModuleMetadataPlugin(), ".mod", bytes);
        Assert.Equal("Fixture MOD", facts["Title"].Text);
        Assert.Equal(4, facts["ChannelCount"].Integer);
        await AssertPreview(new SequencerModulePreviewPlugin(), ".mod", bytes);
        Assert.Empty(await Extract(new SequencerModuleMetadataPlugin(), ".mod", bytes[..^1]));
        Assert.Empty(await Extract(new SequencerModuleMetadataPlugin(), ".mod", "<html/>"u8.ToArray()));
        bytes[952] = 127;
        Assert.Empty(await Extract(new SequencerModuleMetadataPlugin(), ".mod", bytes));
    }

    [Fact]
    public async Task RenoiseReadsSongXmlFromZip()
    {
        var bytes = Pack(true, "<RenoiseSong><GlobalSongData><SongName>Demo</SongName><BeatsPerMin>123</BeatsPerMin></GlobalSongData><Tracks><SequencerTrack><Name>Drums</Name></SequencerTrack></Tracks></RenoiseSong>"u8.ToArray());
        var facts = await Extract(new SequencerModuleMetadataPlugin(), ".xrns", bytes);
        Assert.Equal("Demo", facts["Title"].Text);
        Assert.Equal(1, facts["TrackCount"].Integer);
        await AssertPreview(new SequencerModulePreviewPlugin(), ".xrns", bytes);
    }

    [Fact]
    public async Task LmmsReadsQtCompressedProject()
    {
        var payload = "<lmms><head bpm='98'/></lmms>"u8;
        using var output = new MemoryStream();
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
        output.Write(length);
        using (var zlib = new ZLibStream(output, CompressionMode.Compress, leaveOpen: true))
            zlib.Write(payload);

        var facts = await Extract(new SequencerProjectMetadataPlugin(), ".mmpz", output.ToArray());
        Assert.Equal(98, facts["Tempo"].Number);
    }

    [Theory]
    [InlineData(".xm", "xm", 80)]
    [InlineData(".s3m", "s3m", 96)]
    [InlineData(".it", "it", 192)]
    [InlineData(".mptm", "mptm", 192)]
    [InlineData(".mo3", "mo3", 13)]
    public async Task CoreTrackerHeadersAreDispatchedBySignature(string extension, string format, int size)
    {
        var bytes = new byte[size];
        switch (format)
        {
            case "xm":
                "Extended Module: "u8.CopyTo(bytes);
                bytes[37] = 0x1a;
                bytes[58] = 4;
                bytes[59] = 1;
                bytes[60] = 20;
                bytes[68] = 4;
                break;
            case "s3m":
                bytes[28] = 0x1a;
                bytes[29] = 16;
                "SCRM"u8.CopyTo(bytes.AsSpan(44));
                break;
            case "it":
                "IMPM"u8.CopyTo(bytes);
                break;
            case "mptm":
                "tpm."u8.CopyTo(bytes);
                break;
            case "mo3":
                "MO3"u8.CopyTo(bytes);
                bytes[3] = 5;
                bytes[5] = 2;
                bytes[8] = 1;
                break;
        }
        var facts = await Extract(new SequencerModuleMetadataPlugin(), extension, bytes);
        Assert.Equal(format, facts["Format"].Text);
        Assert.Empty(await Extract(new SequencerModuleMetadataPlugin(), extension, bytes[..^1]));
    }

    [Fact]
    public async Task CompressedXmlRejectsExpansionBeyondLimit()
    {
        var payload = new byte[32 * 1024 * 1024 + 1];
        Assert.Empty(await Extract(new SequencerProjectMetadataPlugin(), ".mmpz", Pack(false, payload)));
        Assert.Empty(await Extract(new SequencerModuleMetadataPlugin(), ".xrns", Pack(true, payload)));
        Assert.Empty(await Extract(new SequencerProjectMetadataPlugin(), ".mmp", "<!DOCTYPE lmms [<!ENTITY x 'value'>]><lmms>&x;</lmms>"u8.ToArray()));
        Assert.Equal(98, (await Extract(new SequencerProjectMetadataPlugin(), ".mmpz",
            Pack(false, "<lmms><head bpm='98'/></lmms>"u8.ToArray())))["Tempo"].Number);
    }

    private static byte[] Pack(bool zip, byte[] payload)
    {
        using var output = new MemoryStream();
        if (zip)
        {
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            using var entry = archive.CreateEntry("Song.xml").Open();
            entry.Write(payload);
        }
        else
        {
            using var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true);
            gzip.Write(payload);
        }
        return output.ToArray();
    }

    private static async Task<Dictionary<string, MediaMetadataValue>> Extract(IMediaMetadataCapability plugin, string extension, byte[] bytes)
    {
        Assert.Equal(10, plugin.Descriptor.Priority);
        Assert.Equal("skycd.sequencer.metadata", plugin.Descriptor.Schema.Id);
        var result = await plugin.ExtractAsync(new(extension, null, bytes.Length), new Source(bytes), null, TestContextToken);
        return result.Values.ToDictionary(value => value.Key);
    }

    private static async Task AssertPreview(IMediaPreviewCapability plugin, string extension, byte[] bytes)
    {
        Assert.Equal(10, plugin.Descriptor.Priority);
        Assert.Equal(plugin is SequencerMidiPreviewPlugin, plugin.Descriptor.IndexTimePreview);
        Assert.Equal("skycd.sequencer.preview", plugin.Descriptor.Schema.Id);
        var item = new MediaItem(extension, null, bytes.Length);
        var first = Assert.Single((await plugin.GeneratePreviewAsync(item, new Source(bytes), null, TestContextToken)).Artifacts);
        var second = Assert.Single((await plugin.GeneratePreviewAsync(item, new Source(bytes), null, TestContextToken)).Artifacts);
        Assert.Equal("image/png", first.ContentType);
        Assert.Equal(first.Data.ToArray(), second.Data.ToArray());
        Assert.InRange(first.Data.Length, 1, 4 * 1024 * 1024);
        using var image = SKBitmap.Decode(first.Data.ToArray());
        Assert.Equal(512, image.Width);
        Assert.Equal(384, image.Height);
    }

    private static CancellationToken TestContextToken => CancellationToken.None;

    private sealed class Source(byte[] bytes) : IMediaSource
    {
        public long Length => bytes.Length;

        public ValueTask<ReadOnlyMemory<byte>> ReadAsync(long offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(bytes.AsMemory((int)offset, count));
        }
    }
}
