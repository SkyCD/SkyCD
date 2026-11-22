using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.IWork.Exceptions;
using Xunit;
using SkyCD.Plugin.Metadata.IWork.Models;

namespace SkyCD.Plugin.Metadata.IWork.Tests;

public class IWorkMetadataPluginTests
{
    [Fact]
    public void Descriptor_ClaimsTheThreeModernIWorkTypesAndAVersionedSchema()
    {
        var descriptor = new IWorkMetadataPlugin().Descriptor;

        Assert.Equal([".pages", ".numbers", ".key"], descriptor.Extensions);
        Assert.Equal(
            [
                "application/x-iwork-pages-sffpages",
                "application/x-iwork-numbers-sffnumbers",
                "application/x-iwork-keynote-sffkey"
            ],
            descriptor.ContentTypes);
        Assert.Equal("skycd.iwork.metadata", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
    }

    [Fact]
    public async Task FlatPagesPackage_ReportsInventoryFormatVersionAndProducerBuild()
    {
        var result = await ExtractAsync(IWorkFixtures.Build());

        Assert.Equal("iwork", Text(result, IWorkSchema.Container));
        Assert.Equal("pages", Text(result, IWorkSchema.DocumentType));
        Assert.False(Boolean(result, IWorkSchema.IsPasswordProtected));
        Assert.Equal(IWorkFixtures.DefaultFormatVersion, Text(result, IWorkSchema.FormatVersion));
        Assert.Equal(IWorkFixtures.DefaultProducerBuild, Text(result, IWorkSchema.ProducerBuild));
        Assert.Equal(IWorkFixtures.DefaultDocumentIdentifier, Text(result, IWorkSchema.DocumentIdentifier));
        Assert.Equal("en", Text(result, IWorkSchema.Language));
        Assert.Equal("en_US", Text(result, IWorkSchema.Locale));
        Assert.True(Boolean(result, IWorkSchema.IsMultiPage));
        Assert.False(Boolean(result, IWorkSchema.HasExternalData));
        Assert.True(Boolean(result, IWorkSchema.HasPreview));
        Assert.Equal(2L, Integer(result, IWorkSchema.IwaComponentCount));
        Assert.Equal(1L, Integer(result, IWorkSchema.DataAssetCount));
        Assert.Equal(IWorkFixtures.PreviewWidth, Integer(result, IWorkSchema.PreviewWidth));
        Assert.Equal(IWorkFixtures.PreviewHeight, Integer(result, IWorkSchema.PreviewHeight));
    }

    [Fact]
    public async Task NumbersPackage_ReportsItsTypeWithoutASlideCount()
    {
        var result = await ExtractAsync(
            IWorkFixtures.Build(new PackageOptions
            {
                Kind = FixtureDocumentKind.Numbers,
                TableCount = 2
            }), ".numbers");

        Assert.Equal("numbers", Text(result, IWorkSchema.DocumentType));
        Assert.DoesNotContain(result.Values, value => value.Key == IWorkSchema.SlideCount);
    }

    [Fact]
    public async Task KeynotePackage_CountsItsSlides()
    {
        var result = await ExtractAsync(
            IWorkFixtures.Build(new PackageOptions
            {
                Kind = FixtureDocumentKind.Keynote,
                SlideCount = 3
            }), ".key");

        Assert.Equal("keynote", Text(result, IWorkSchema.DocumentType));
        Assert.Equal(3L, Integer(result, IWorkSchema.SlideCount));
        Assert.Equal(2L, Integer(result, IWorkSchema.IwaComponentCount));
    }

    [Fact]
    public async Task BundleLayout_IsReadThroughTheNestedIndex()
    {
        var result = await ExtractAsync(IWorkFixtures.Build(new PackageOptions
        {
            Kind = FixtureDocumentKind.Keynote,
            NestedIndex = true,
            SlideCount = 2
        }), ".key");

        Assert.Equal("keynote", Text(result, IWorkSchema.DocumentType));
        Assert.Equal(2L, Integer(result, IWorkSchema.SlideCount));
        Assert.Equal(2L, Integer(result, IWorkSchema.IwaComponentCount));
        Assert.Equal(IWorkFixtures.DefaultProducerBuild, Text(result, IWorkSchema.ProducerBuild));
    }

    [Theory]
    [InlineData(FixtureDocumentKind.Pages, "pages")]
    [InlineData(FixtureDocumentKind.Numbers, "numbers")]
    [InlineData(FixtureDocumentKind.Keynote, "keynote")]
    public async Task MislabelledSource_IsIdentifiedFromTheComponentArchive(
        FixtureDocumentKind kind,
        string expected)
    {
        var result = await ExtractAsync(IWorkFixtures.Build(new PackageOptions { Kind = kind }), ".dat");

        Assert.Equal(expected, Text(result, IWorkSchema.DocumentType));
    }

    [Fact]
    public async Task IdentifierEntry_IsCanonicalised()
    {
        var result = await ExtractAsync(IWorkFixtures.Build(new PackageOptions
        {
            DocumentIdentifierText = IWorkFixtures.DefaultDocumentIdentifier.ToUpperInvariant()
        }));

        Assert.Equal(IWorkFixtures.DefaultDocumentIdentifier, Text(result, IWorkSchema.DocumentIdentifier));
    }

    [Fact]
    public async Task MissingIdentifierEntry_FallsBackToThePropertiesUuid()
    {
        const string fallback = "1f0e2d3c-4b5a-6978-8796-a5b4c3d2e1f0";

        var result = await ExtractAsync(IWorkFixtures.Build(new PackageOptions
        {
            IncludeIdentifier = false,
            DocumentUuidInProperties = fallback.ToUpperInvariant()
        }));

        Assert.Equal(fallback, Text(result, IWorkSchema.DocumentIdentifier));
    }

    [Fact]
    public async Task ProducerBuild_SkipsTemplateEntriesAtEitherEndOfTheHistory()
    {
        var result = await ExtractAsync(IWorkFixtures.Build(new PackageOptions
        {
            Kind = FixtureDocumentKind.Numbers,
            ProducerBuild = "Numbers 13.2"
        }), ".numbers");

        Assert.Equal("Numbers 13.2", Text(result, IWorkSchema.ProducerBuild));
    }

    [Fact]
    public async Task JpegPreview_ReportsItsDimensions()
    {
        var result = await ExtractAsync(IWorkFixtures.Build(new PackageOptions { JpegPreview = true }));

        Assert.Equal(IWorkFixtures.JpegPreviewWidth, Integer(result, IWorkSchema.PreviewWidth));
        Assert.Equal(IWorkFixtures.JpegPreviewHeight, Integer(result, IWorkSchema.PreviewHeight));
    }

    [Fact]
    public async Task ProtectedPackage_PublishesPlainMetadataAndNoPreviewDimensions()
    {
        var result = await ExtractAsync(IWorkFixtures.Build(new PackageOptions { PasswordProtected = true }));

        Assert.True(Boolean(result, IWorkSchema.IsPasswordProtected));
        Assert.Equal(IWorkFixtures.DefaultFormatVersion, Text(result, IWorkSchema.FormatVersion));
        Assert.Equal(IWorkFixtures.DefaultDocumentIdentifier, Text(result, IWorkSchema.DocumentIdentifier));
        Assert.True(Boolean(result, IWorkSchema.HasPreview));

        // The build history and the preview payload are encrypted, so neither is decoded.
        Assert.DoesNotContain(result.Values, value => value.Key == IWorkSchema.ProducerBuild);
        Assert.DoesNotContain(result.Values, value => value.Key == IWorkSchema.PreviewWidth);
        Assert.DoesNotContain(result.Values, value => value.Key == IWorkSchema.PreviewHeight);
    }

    [Fact]
    public async Task AbsentMetadataMembers_AreOmittedRatherThanPublishedAsEmptyValues()
    {
        var result = await ExtractAsync(IWorkFixtures.Build(new PackageOptions
        {
            IncludeProperties = false,
            IncludeBuildHistory = false,
            IncludeIdentifier = false,
            IncludePreview = false,
            DataAssets = 0
        }));

        Assert.Equal("iwork", Text(result, IWorkSchema.Container));
        Assert.Equal("pages", Text(result, IWorkSchema.DocumentType));
        Assert.Equal(2L, Integer(result, IWorkSchema.IwaComponentCount));
        Assert.Equal(0L, Integer(result, IWorkSchema.DataAssetCount));
        Assert.False(Boolean(result, IWorkSchema.HasPreview));

        foreach (var key in new[]
                 {
                     IWorkSchema.FormatVersion, IWorkSchema.ProducerBuild, IWorkSchema.DocumentIdentifier,
                     IWorkSchema.Language, IWorkSchema.Locale, IWorkSchema.IsMultiPage, IWorkSchema.HasExternalData,
                     IWorkSchema.PreviewWidth, IWorkSchema.PreviewHeight
                 })
        {
            Assert.DoesNotContain(result.Values, value => value.Key == key);
        }
    }

    [Fact]
    public async Task LegacyPackage_FailsAsUnsupported()
    {
        var exception = await Assert.ThrowsAsync<IWorkUnsupportedFormatException>(
            () => ExtractAsync(IWorkFixtures.Build(new PackageOptions { Legacy = true })));

        Assert.Contains("legacy", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing-index")]
    [InlineData("notzip")]
    [InlineData("truncated")]
    public async Task UnreadableSources_FailWithADecodeError(string kind)
    {
        var package = kind switch
        {
            "missing-index" => IWorkFixtures.MissingIndex(),
            "notzip" => IWorkFixtures.NotZip(),
            _ => IWorkFixtures.Truncated(IWorkFixtures.Build(new PackageOptions
            {
                Kind = FixtureDocumentKind.Keynote,
                SlideCount = 2
            }))
        };

        await Assert.ThrowsAsync<IWorkDecodeException>(() => ExtractAsync(package));
    }

    [Fact]
    public async Task EmptySource_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<IWorkDecodeException>(() => ExtractAsync([]));
    }

    [Fact]
    public async Task OversizedSource_IsRejectedBeforeAnyByteIsRead()
    {
        var package = IWorkFixtures.Build();
        var source = new ByteArrayMediaSource(package);
        var plugin = new IWorkMetadataPlugin(new IWorkPluginOptions(maxSourceBytes: package.Length - 1));

        await Assert.ThrowsAsync<IWorkSourceTooLargeException>(() => plugin.ExtractAsync(
            new MediaItem(".pages", "application/x-iwork-pages-sffpages", package.Length),
            source,
            null,
            CancellationToken.None));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var package = IWorkFixtures.Build();
        var source = new ByteArrayMediaSource(package);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new IWorkMetadataPlugin().ExtractAsync(
            new MediaItem(".pages", "application/x-iwork-pages-sffpages", package.Length),
            source,
            null,
            cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task EveryReturnedValue_ConformsToTheDeclaredSchema()
    {
        var schema = IWorkSchema.Metadata;
        var result = await ExtractAsync(IWorkFixtures.Build(new PackageOptions
        {
            Kind = FixtureDocumentKind.Keynote,
            SlideCount = 2,
            DocumentUuidInProperties = IWorkFixtures.DefaultDocumentIdentifier
        }), ".key");

        Assert.NotEmpty(result.Values);
        foreach (var value in result.Values)
        {
            var field = schema.Fields.SingleOrDefault(candidate => candidate.Key == value.Key);
            Assert.NotNull(field);
            Assert.Equal(field!.Type, value.Type);
        }

        Assert.Equal(
            result.Values.Count,
            result.Values.Select(value => value.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Extraction_LeavesTheSourceBytesUnchanged()
    {
        var package = IWorkFixtures.Build(new PackageOptions
        {
            Kind = FixtureDocumentKind.Keynote,
            SlideCount = 2
        });
        var before = (byte[])package.Clone();

        await ExtractAsync(package);

        Assert.Equal(before, package);
    }

    [Fact]
    public async Task ReadingNeverTouchesTheFilesystem_OnlyTheBoundedSource()
    {
        var package = IWorkFixtures.Build(new PackageOptions
        {
            Kind = FixtureDocumentKind.Keynote,
            SlideCount = 5,
            DataAssets = 3
        });
        var source = new ByteArrayMediaSource(package);

        await new IWorkMetadataPlugin().ExtractAsync(
            new MediaItem(".key", "application/x-iwork-keynote-sffkey", package.Length),
            source,
            null,
            CancellationToken.None);

        Assert.Equal(package.Length, source.ChargedBytes);
        Assert.True(source.ReadCount < 64, $"A {package.Length} byte package took {source.ReadCount} host reads.");
    }

    internal static Task<MediaExtractionResult> ExtractAsync(byte[] package, string extension = ".pages") =>
        new IWorkMetadataPlugin().ExtractAsync(
            new MediaItem(extension, ContentTypeFor(extension), package.Length),
            new ByteArrayMediaSource(package),
            progress: null,
            CancellationToken.None);

    private static string? ContentTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".pages" => "application/x-iwork-pages-sffpages",
        ".numbers" => "application/x-iwork-numbers-sffnumbers",
        ".key" => "application/x-iwork-keynote-sffkey",
        _ => null
    };

    private static string? Text(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Text;

    private static long? Integer(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Integer;

    private static bool? Boolean(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Boolean;
}
