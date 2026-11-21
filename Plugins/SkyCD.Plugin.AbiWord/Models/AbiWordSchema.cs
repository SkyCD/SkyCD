using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.AbiWord.Models;

public static class AbiWordSchema
{
    public const string MetadataSchemaId = "skycd.abiword.metadata";
    public const int MetadataSchemaVersion = 1;
    public const string Container = "Container";
    public const string Title = "Title";
    public const string Author = "Author";
    public const string Subject = "Subject";
    public const string Description = "Description";
    public const string Keywords = "Keywords";
    public const string Language = "Language";
    public const string Publisher = "Publisher";
    public const string Contributor = "Contributor";
    public const string Rights = "Rights";
    public const string Type = "Type";
    public const string Source = "Source";
    public const string Relation = "Relation";
    public const string Identifier = "Identifier";
    public const string Coverage = "Coverage";
    public const string Date = "Date";
    public const string LastChanged = "LastChanged";
    public const string Format = "Format";
    public const string Generator = "Generator";
    public const string FormatVersion = "FormatVersion";

    public static readonly IReadOnlyList<string> Extensions = [".abw", ".zabw"];
    public static readonly IReadOnlyList<string> ContentTypes = ["application/x-abiword"];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new(Container, MediaMetadataType.Text),
        new(Title, MediaMetadataType.Text),
        new(Author, MediaMetadataType.Text),
        new(Subject, MediaMetadataType.Text),
        new(Description, MediaMetadataType.Text),
        new(Keywords, MediaMetadataType.Text),
        new(Language, MediaMetadataType.Text),
        new(Publisher, MediaMetadataType.Text),
        new(Contributor, MediaMetadataType.Text),
        new(Rights, MediaMetadataType.Text),
        new(Type, MediaMetadataType.Text),
        new(Source, MediaMetadataType.Text),
        new(Relation, MediaMetadataType.Text),
        new(Identifier, MediaMetadataType.Text),
        new(Coverage, MediaMetadataType.Text),
        new(Date, MediaMetadataType.Text),
        new(LastChanged, MediaMetadataType.Text),
        new(Format, MediaMetadataType.Text),
        new(Generator, MediaMetadataType.Text),
        new(FormatVersion, MediaMetadataType.Text)
    ]);
}
