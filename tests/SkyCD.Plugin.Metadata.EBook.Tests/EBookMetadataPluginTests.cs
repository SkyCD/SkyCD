using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using Xunit;
using SkyCD.Plugin.Metadata.EBook.Models;
using SkyCD.Plugin.Metadata.EBook.Parsing;

namespace SkyCD.Plugin.Metadata.EBook.Tests;

/// <summary>
/// Metadata extraction across the supported formats, the "nothing usable" states, and the schema contract the
/// host caches against.
/// </summary>
public sealed class EBookMetadataPluginTests
{
    [Fact]
    public void Descriptor_ClaimsTheDocumentedEbookTypesAndAVersionedSchema()
    {
        var descriptor = new EBookMetadataPlugin().Descriptor;

        Assert.Equal([".epub", ".ibook", ".mobi", ".prc", ".azw", ".azw3", ".fb2"], descriptor.Extensions);
        Assert.Contains("application/epub+zip", descriptor.ContentTypes);
        Assert.Contains("application/x-mobipocket-ebook", descriptor.ContentTypes);
        Assert.Equal("skycd.ebook.metadata", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
    }

    [Fact]
    public async Task Epub_PublishesEveryDublinCoreField()
    {
        var book = EBookFixtures.Epub(cover: EBookFixtures.Jpeg(600, 900, SkiaSharp.SKColors.Teal));

        var result = await EBookFixtures.ExtractMetadataAsync(book);

        Assert.Equal("zip", EBookFixtures.Text(result, EBookSchema.Container));
        Assert.Equal("epub", EBookFixtures.Text(result, EBookSchema.Format));
        Assert.Equal("The Long Way", EBookFixtures.Text(result, EBookSchema.Title));
        Assert.Equal("Hodder & Stoughton", EBookFixtures.Text(result, EBookSchema.Publisher));
        Assert.Equal("en-GB", EBookFixtures.Text(result, EBookSchema.Language));
        Assert.Equal("Science Fiction", EBookFixtures.Text(result, EBookSchema.Subject));
        Assert.Equal("A young woman leaves Earth behind.",
            EBookFixtures.Text(result, EBookSchema.Description));
        Assert.Equal("URN:ISBN:9781473601801", EBookFixtures.Text(result, EBookSchema.Identifier));
        Assert.Equal(new DateTimeOffset(2015, 7, 30, 0, 0, 0, TimeSpan.Zero),
            EBookFixtures.Timestamp(result, EBookSchema.PublishedAt));
        Assert.Equal(2015L, EBookFixtures.Integer(result, EBookSchema.PublishedYear));
        Assert.DoesNotContain(result.Values, value => value.Key == EBookSchema.Authors);
        Assert.DoesNotContain(result.Values, value => value.Key == EBookSchema.PreferredName);
        Assert.DoesNotContain(result.Values, value => value.Key == EBookSchema.Protected);
    }

    [Fact]
    public async Task Epub3_ReadsTheCoverImagePropertyAndThePackageMetadata()
    {
        var book = EBookFixtures.Epub(
            EBookFixtures.Epub3Opf,
            cover: EBookFixtures.Png(400, 300, SkiaSharp.SKColors.Orange),
            coverPath: "OEBPS/art/front.png");

        var result = await EBookFixtures.ExtractMetadataAsync(book);

        Assert.Equal("A Closed and Common Orbit", EBookFixtures.Text(result, EBookSchema.Title));
        Assert.Equal("urn:uuid:0d7b6c8e-0000-0000-0000-000000000000",
            EBookFixtures.Text(result, EBookSchema.Identifier));
        Assert.Null(EBookFixtures.Text(result, EBookSchema.Publisher));
        Assert.Null(EBookFixtures.Timestamp(result, EBookSchema.PublishedAt));

        var cover = await EBookFixtures.GenerateCoverAsync(book);
        Assert.Single(cover.Artifacts);
    }

    [Fact]
    public async Task AppleIBook_IsReportedAsItsOwnFormat()
    {
        var book = EBookFixtures.Epub();

        var result = await EBookFixtures.ExtractMetadataAsync(book, ".ibook");

        Assert.Equal("zip", EBookFixtures.Text(result, EBookSchema.Container));
        Assert.Equal("ibook", EBookFixtures.Text(result, EBookSchema.Format));
    }

    [Fact]
    public async Task MissingContainerXml_FallsBackToThePackagedOpf()
    {
        var book = EBookFixtures.Epub(includeContainer: false);

        var result = await EBookFixtures.ExtractMetadataAsync(book);

        Assert.Equal("The Long Way", EBookFixtures.Text(result, EBookSchema.Title));
    }

    [Fact]
    public async Task AuthorTagsAndPreferredName_FollowTheirOptions()
    {
        var book = EBookFixtures.Epub();

        var tagged = await EBookFixtures.ExtractMetadataAsync(
            book, settings: EBookFixtures.Settings(tagAuthors: true, nameSource: EBookNameSource.Author));
        Assert.Equal("Becky Chambers; Ann Leckie", EBookFixtures.Text(tagged, EBookSchema.Authors));
        Assert.Equal("Becky Chambers; Ann Leckie", EBookFixtures.Text(tagged, EBookSchema.PreferredName));

        var titled = await EBookFixtures.ExtractMetadataAsync(
            book, settings: EBookFixtures.Settings(nameSource: EBookNameSource.Title));
        Assert.Equal("The Long Way", EBookFixtures.Text(titled, EBookSchema.PreferredName));
        Assert.DoesNotContain(titled.Values, value => value.Key == EBookSchema.Authors);

        var plain = await EBookFixtures.ExtractMetadataAsync(book);
        Assert.DoesNotContain(plain.Values, value => value.Key == EBookSchema.PreferredName);
    }

    [Fact]
    public async Task FontObfuscation_IsNotReportedAsProtection()
    {
        var book = EBookFixtures.Epub(encryptionXml: System.Text.Encoding.UTF8.GetString(
            EBookFixtures.FontObfuscationEncryption()));

        var result = await EBookFixtures.ExtractMetadataAsync(book);

        Assert.Null(EBookFixtures.Boolean(result, EBookSchema.Protected));
        Assert.Equal("The Long Way", EBookFixtures.Text(result, EBookSchema.Title));
    }

    [Fact]
    public async Task EncryptedContent_IsReportedAsProtectedWithItsMetadata()
    {
        var book = EBookFixtures.Epub(encryptionXml: System.Text.Encoding.UTF8.GetString(
            EBookFixtures.ContentEncryption()));

        var result = await EBookFixtures.ExtractMetadataAsync(book);

        Assert.True(EBookFixtures.Boolean(result, EBookSchema.Protected));
        Assert.Equal("The Long Way", EBookFixtures.Text(result, EBookSchema.Title));
    }

    [Fact]
    public async Task FictionBook_PublishesTheDescriptionBlock()
    {
        var book = System.Text.Encoding.UTF8.GetBytes(
            EBookFixtures.Fb2Xml(EBookFixtures.Jpeg(120, 180, SkiaSharp.SKColors.Navy)));

        var result = await EBookFixtures.ExtractMetadataAsync(
            book, ".fb2", EBookFixtures.Settings(tagAuthors: true));

        Assert.Equal("xml", EBookFixtures.Text(result, EBookSchema.Container));
        Assert.Equal("fb2", EBookFixtures.Text(result, EBookSchema.Format));
        Assert.Equal("Roadside Picnic", EBookFixtures.Text(result, EBookSchema.Title));
        Assert.Equal("Arkady Strugatsky; Boris Strugatsky", EBookFixtures.Text(result, EBookSchema.Authors));
        Assert.Equal("Molodaya Gvardiya", EBookFixtures.Text(result, EBookSchema.Publisher));
        Assert.Equal("ru", EBookFixtures.Text(result, EBookSchema.Language));
        Assert.Equal("sf", EBookFixtures.Text(result, EBookSchema.Subject));
        Assert.Equal("Aliens visited and left their rubbish behind.",
            EBookFixtures.Text(result, EBookSchema.Description));
        Assert.Equal("978-5-17-000000-0", EBookFixtures.Text(result, EBookSchema.Identifier));

        // A bare year is published as a year, never completed into an invented date.
        Assert.Equal(1972L, EBookFixtures.Integer(result, EBookSchema.PublishedYear));
        Assert.Null(EBookFixtures.Timestamp(result, EBookSchema.PublishedAt));
    }

    [Fact]
    public async Task Mobipocket_PublishesExthMetadata()
    {
        var book = EBookFixtures.Mobi(cover: EBookFixtures.Jpeg(300, 450, SkiaSharp.SKColors.Teal));

        var result = await EBookFixtures.ExtractMetadataAsync(
            book, ".azw3", EBookFixtures.Settings(tagAuthors: true));

        Assert.Equal("palmdb", EBookFixtures.Text(result, EBookSchema.Container));
        Assert.Equal("mobi", EBookFixtures.Text(result, EBookSchema.Format));
        Assert.Equal("The Long Way", EBookFixtures.Text(result, EBookSchema.Title));
        Assert.Equal("Becky Chambers", EBookFixtures.Text(result, EBookSchema.Authors));
        Assert.Equal("Hodder & Stoughton", EBookFixtures.Text(result, EBookSchema.Publisher));
        Assert.Equal("en", EBookFixtures.Text(result, EBookSchema.Language));
        Assert.Equal("Science Fiction", EBookFixtures.Text(result, EBookSchema.Subject));
        Assert.Equal("A young woman leaves Earth behind.",
            EBookFixtures.Text(result, EBookSchema.Description));
        Assert.Equal("9781473601801", EBookFixtures.Text(result, EBookSchema.Identifier));
        Assert.Equal(new DateTimeOffset(2015, 7, 30, 0, 0, 0, TimeSpan.Zero),
            EBookFixtures.Timestamp(result, EBookSchema.PublishedAt));
        Assert.Equal(2015L, EBookFixtures.Integer(result, EBookSchema.PublishedYear));
        Assert.Null(EBookFixtures.Boolean(result, EBookSchema.Protected));
    }

    [Fact]
    public async Task Mobipocket_WithoutExth_FallsBackToTheFullName()
    {
        var book = EBookFixtures.Mobi(withExth: false, fullName: "Header Title");

        var result = await EBookFixtures.ExtractMetadataAsync(book, ".mobi");

        Assert.Equal("Header Title", EBookFixtures.Text(result, EBookSchema.Title));
        Assert.Equal(
            [EBookSchema.Container, EBookSchema.Format, EBookSchema.Title],
            result.Values.Select(value => value.Key));
    }

    [Fact]
    public async Task Kf8Boundary_IsReportedAsTheKf8Format()
    {
        var book = EBookFixtures.Mobi(kf8: true);

        var result = await EBookFixtures.ExtractMetadataAsync(book, ".azw3");

        Assert.Equal("kf8", EBookFixtures.Text(result, EBookSchema.Format));
    }

    [Fact]
    public async Task EncryptedMobipocket_StillPublishesItsMetadata()
    {
        var book = EBookFixtures.Mobi(encryptionType: 2);

        var result = await EBookFixtures.ExtractMetadataAsync(book, ".mobi");

        Assert.True(EBookFixtures.Boolean(result, EBookSchema.Protected));
        Assert.Equal("The Long Way", EBookFixtures.Text(result, EBookSchema.Title));
    }

    [Fact]
    public async Task PlainPalmDoc_UsesTheDatabaseNameAsItsTitle()
    {
        var book = EBookFixtures.PalmDoc(databaseName: "Meeting notes");

        var result = await EBookFixtures.ExtractMetadataAsync(book, ".prc");

        Assert.Equal("palmdb", EBookFixtures.Text(result, EBookSchema.Container));
        Assert.Equal("palmdoc", EBookFixtures.Text(result, EBookSchema.Format));
        Assert.Equal("Meeting notes", EBookFixtures.Text(result, EBookSchema.Title));
        Assert.Equal(3, result.Values.Count);
    }

    [Fact]
    public async Task BlankValues_AreOmittedRatherThanPublishedEmpty()
    {
        const string blankOpf = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:title>   </dc:title>
                <dc:creator></dc:creator>
                <dc:publisher>Line one
                  line two</dc:publisher>
              </metadata>
              <manifest/>
              <spine/>
            </package>
            """;
        var book = EBookFixtures.Epub(blankOpf);

        var result = await EBookFixtures.ExtractMetadataAsync(
            book, settings: EBookFixtures.Settings(tagAuthors: true));

        Assert.DoesNotContain(result.Values, value => value.Key == EBookSchema.Title);
        Assert.DoesNotContain(result.Values, value => value.Key == EBookSchema.Authors);
        Assert.Equal("Line one line two", EBookFixtures.Text(result, EBookSchema.Publisher));
    }

    [Fact]
    public async Task DocumentTypeDeclarations_AreNotExpanded()
    {
        const string dtdOpf = """
            <?xml version="1.0"?>
            <!DOCTYPE package [<!ENTITY secret "x">]>
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:title>&secret;</dc:title>
              </metadata>
              <manifest/>
            </package>
            """;
        var book = EBookFixtures.Epub(dtdOpf);

        var result = await EBookFixtures.ExtractMetadataAsync(book);

        Assert.Empty(result.Values);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("random")]
    [InlineData("plainzip")]
    [InlineData("truncatedzip")]
    [InlineData("truncatedpdb")]
    public async Task UnreadableSources_YieldNoValuesInsteadOfAFailure(string kind)
    {
        var book = kind switch
        {
            "empty" => Array.Empty<byte>(),
            "random" => System.Text.Encoding.ASCII.GetBytes(
                new string('x', 200) + " this is not an e-book container at all"),
            "plainzip" => EBookFixtures.Zip(
                ("readme.txt", System.Text.Encoding.ASCII.GetBytes("hello"))),
            "truncatedzip" => EBookFixtures.Truncate(EBookFixtures.Epub(), 40),
            _ => EBookFixtures.Truncate(EBookFixtures.Mobi(), 100)
        };

        var result = await EBookFixtures.ExtractMetadataAsync(book);

        Assert.Empty(result.Values);
    }

    [Fact]
    public void DisabledCollection_LeavesNoProviders()
    {
        var plugin = new EBookMetadataPlugin();
        plugin.ApplySettings(EBookFixtures.Settings(collectionEnabled: false).ToDictionary());

        Assert.Empty(plugin.Descriptor.Extensions);
        Assert.Empty(plugin.Descriptor.ContentTypes);
    }

    [Fact]
    public async Task OversizedFictionBook_IsRejectedBeforeParsing()
    {
        var book = System.Text.Encoding.UTF8.GetBytes(EBookFixtures.Fb2Xml());
        var result = await EBookFixtures.ExtractMetadataAsync(
            book, ".fb2", options: new EBookPluginOptions(maxSourceBytes: 100));

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var book = EBookFixtures.Epub();
        var source = new ByteArrayMediaSource(book);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EBookMetadataPlugin().ExtractAsync(
            new MediaItem(".epub", null, book.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task WholeSourceFormat_ReadsTheSourceExactlyOncePastTheSignature()
    {
        var book = System.Text.Encoding.UTF8.GetBytes(EBookFixtures.Fb2Xml());
        var source = new ByteArrayMediaSource(book);

        await new EBookMetadataPlugin().ExtractAsync(
            new MediaItem(".fb2", null, book.Length), source, null, CancellationToken.None);

        Assert.Equal(book.Length + 68, source.ChargedBytes);
        Assert.Equal(2, source.ReadCount);
    }

    [Fact]
    public async Task EveryReturnedValue_ConformsToTheDeclaredSchema()
    {
        var schema = EBookSchema.Metadata;
        var book = EBookFixtures.Epub(cover: EBookFixtures.Jpeg(600, 900, SkiaSharp.SKColors.Teal));

        var result = await EBookFixtures.ExtractMetadataAsync(
            book, settings: EBookFixtures.Settings(
                tagAuthors: true, nameSource: EBookNameSource.Author, extractCovers: true));

        Assert.NotEmpty(result.Values);
        foreach (var value in result.Values)
        {
            var field = schema.Fields.SingleOrDefault(candidate => candidate.Key == value.Key);
            Assert.NotNull(field);
            Assert.Equal(field!.Type, value.Type);
        }

        Assert.Equal(result.Values.Count,
            result.Values.Select(value => value.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Extraction_LeavesTheSourceBytesUnchanged()
    {
        var book = EBookFixtures.Mobi(cover: EBookFixtures.Jpeg(300, 450, SkiaSharp.SKColors.Teal));
        var before = (byte[])book.Clone();

        await EBookFixtures.ExtractMetadataAsync(book, ".mobi");

        Assert.Equal(before, book);
    }

    [Fact]
    public async Task Progress_IsReportedAndEndsAtCompletion()
    {
        var reports = new List<double>();
        var book = EBookFixtures.Epub();

        await new EBookMetadataPlugin().ExtractAsync(
            new MediaItem(".epub", null, book.Length),
            new ByteArrayMediaSource(book),
            new CollectingProgress(reports),
            CancellationToken.None);

        Assert.NotEmpty(reports);
        Assert.Equal(1, reports[^1]);
        Assert.All(reports, value => Assert.InRange(value, 0, 1));
    }

    private sealed class CollectingProgress : IProgress<MediaProgress>
    {
        private readonly List<double> values;

        public CollectingProgress(List<double> values)
        {
            this.values = values;
        }

        public void Report(MediaProgress value) => values.Add(value.Fraction);
    }
}
