using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.OpenOffice.Models;
using Xunit;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.OpenOffice.Tests;

/// <summary>
/// Coverage for the two readers the plugin serves a package with: the OfficeIMO reader, which accepts the
/// OpenDocument text, spreadsheet, presentation and graphics families, and the ZIP member reader, which serves
/// the families it rejects and the packages it cannot parse. Both must publish the same metadata and the same
/// preview, so no field disappears because one reader exposes it and the other does not.
/// </summary>
public class OpenOfficePackageTests
{
    private static readonly string[] ContainerFacts =
        [OpenOfficeSchema.Container, OpenOfficeSchema.DocumentType];

    [Fact]
    public void AcceptedFamily_IsServedByTheOfficeImoReader()
    {
        using var package = OpenOfficePackage.Open(
            OpenOfficeFixtures.OdfDocument(), OpenOfficePluginOptions.Default);

        Assert.Equal(OpenOfficePackageSource.OfficeImo, package.Source);
    }

    [Fact]
    public void RejectedFamily_IsServedByTheZipMemberReader()
    {
        using var package = OpenOfficePackage.Open(
            OpenOfficeFixtures.OdfDocument(contentType: OpenOfficeFixtures.OdfTextTemplateContentType),
            OpenOfficePluginOptions.Default);

        Assert.Equal(OpenOfficePackageSource.ZipMembers, package.Source);
    }

    [Fact]
    public void LegacyPackage_IsServedByTheZipMemberReader()
    {
        using var package = OpenOfficePackage.Open(
            OpenOfficeFixtures.LegacyDocument(), OpenOfficePluginOptions.Default);

        Assert.Equal(OpenOfficePackageSource.ZipMembers, package.Source);
    }

    [Fact]
    public void ProtectedPackage_IsServedByTheZipMemberReader()
    {
        using var package = OpenOfficePackage.Open(
            OpenOfficeFixtures.OdfDocument(manifestXml: OpenOfficeFixtures.Manifest(true)),
            OpenOfficePluginOptions.Default);

        Assert.Equal(OpenOfficePackageSource.ZipMembers, package.Source);
    }

    [Fact]
    public async Task BothReaders_PublishTheSameDocumentMetadata()
    {
        var accepted = await ExtractAsync(OpenOfficeFixtures.OdfDocument());
        var rejected = await ExtractAsync(
            OpenOfficeFixtures.OdfDocument(contentType: OpenOfficeFixtures.OdfTextTemplateContentType),
            ".ott", OpenOfficeFixtures.OdfTextTemplateContentType);

        // Only the container facts differ, because the two packages declare different document types.
        Assert.Equal(DocumentValues(accepted), DocumentValues(rejected));
        Assert.Equal("text", accepted.Values.Single(value => value.Key == OpenOfficeSchema.DocumentType).Text);
        Assert.Equal("text-template", rejected.Values.Single(value => value.Key == OpenOfficeSchema.DocumentType).Text);
    }

    [Fact]
    public async Task BothReaders_PublishTheSamePreview()
    {
        var accepted = await PreviewAsync(OpenOfficeFixtures.OdfDocumentWithThumbnail());
        var rejected = await PreviewAsync(
            OpenOfficeFixtures.WithBinaryEntry(
                OpenOfficeFixtures.OdfDocument(contentType: OpenOfficeFixtures.OdfTextTemplateContentType),
                "Thumbnails/thumbnail.png",
                OpenOfficeFixtures.Png(96, 64)),
            new MediaItem(".ott", OpenOfficeFixtures.OdfTextTemplateContentType, 0));

        Assert.Equal(accepted.Artifacts.Count, rejected.Artifacts.Count);
        Assert.Equal(
            accepted.Artifacts[0].Data.ToArray(),
            rejected.Artifacts[0].Data.ToArray());
    }

    private static IReadOnlyList<MediaMetadataValue> DocumentValues(MediaExtractionResult result) =>
        result.Values.Where(value => !ContainerFacts.Contains(value.Key)).ToList();

    private static Task<MediaExtractionResult> ExtractAsync(
        byte[] document,
        string extension = ".odt",
        string? contentType = null)
    {
        return new OpenOfficeMetadataPlugin().ExtractAsync(
            new MediaItem(extension, contentType, document.Length),
            new ByteArrayMediaSource(document),
            progress: null,
            CancellationToken.None);
    }

    private static Task<MediaPreviewResult> PreviewAsync(byte[] document, MediaItem? item = null) =>
        new OpenOfficePreviewPlugin().GeneratePreviewAsync(
            item ?? new MediaItem(".odt", null, document.Length),
            new ByteArrayMediaSource(document),
            progress: null,
            CancellationToken.None);
}
