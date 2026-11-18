namespace SkyCD.Plugin.DiskCatalogMaker;

/// <summary>
/// An unknown table preserved as opaque bytes so a future parser revision can recover it.
/// </summary>
public sealed class DiskCatalogMakerV5UnknownTable
{
    public required uint TableId { get; init; }

    public required uint HeaderValue { get; init; }

    public required int Offset { get; init; }

    public required int Length { get; init; }

    public required uint RecordCount { get; init; }

    public byte[] Payload { get; init; } = [];
}
