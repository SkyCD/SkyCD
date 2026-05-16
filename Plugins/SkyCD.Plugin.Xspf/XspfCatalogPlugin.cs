using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.Xspf;

public sealed class XspfCatalogPlugin : IFileFormatPluginCapability
{
    private static readonly XNamespace XspfNamespace = "http://xspf.org/ns/0/";

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-xspf",
            DisplayName: "XSPF Playlist",
            Extensions: [".xspf"],
            MimeTypes: ["application/xspf+xml"],
            CanRead: true,
            CanWrite: true);

    public Task<FileFormatReadResult> ReadAsync(
        FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = XDocument.Load(request.Source, LoadOptions.None);
            var root = document.Root;
            if (root is null || root.Name != XspfNamespace + "playlist")
            {
                return Task.FromResult(new FileFormatReadResult
                {
                    Success = false,
                    Error = "XSPF root element must be <playlist xmlns='http://xspf.org/ns/0/'>."
                });
            }

            var entries = root
                .Element(XspfNamespace + "trackList")?
                .Elements(XspfNamespace + "track")
                .Select(ParseTrack)
                .Where(static entry => entry is not null)
                .Select(static entry => entry!)
                .ToList() ?? [];

            return Task.FromResult(new FileFormatReadResult
            {
                Success = true,
                Payload = new XspfPlaylistDocument(entries)
            });
        }
        catch (Exception exception)
        {
            return Task.FromResult(new FileFormatReadResult
            {
                Success = false,
                Error = exception.Message
            });
        }
    }

    public async Task<FileFormatWriteResult> WriteAsync(
        FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = ResolveEntries(request.Payload);
            var xml = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement(
                    XspfNamespace + "playlist",
                    new XAttribute("version", "1"),
                    new XElement(
                        XspfNamespace + "trackList",
                        entries.Select(BuildTrackElement))));

            await using var writer = new StreamWriter(request.Target, new UTF8Encoding(false), leaveOpen: true);
            await writer.WriteAsync(xml.ToString(SaveOptions.None).AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);

            return new FileFormatWriteResult { Success = true };
        }
        catch (Exception exception)
        {
            return new FileFormatWriteResult
            {
                Success = false,
                Error = exception.Message
            };
        }
    }

    private static XElement BuildTrackElement(XspfPlaylistEntry entry)
    {
        var track = new XElement(XspfNamespace + "track");
        track.Add(new XElement(XspfNamespace + "location", entry.Location));
        if (!string.IsNullOrWhiteSpace(entry.Title))
        {
            track.Add(new XElement(XspfNamespace + "title", entry.Title));
        }

        if (!string.IsNullOrWhiteSpace(entry.Creator))
        {
            track.Add(new XElement(XspfNamespace + "creator", entry.Creator));
        }

        if (entry.DurationMilliseconds.HasValue)
        {
            track.Add(new XElement(XspfNamespace + "duration", entry.DurationMilliseconds.Value));
        }

        return track;
    }

    private static XspfPlaylistEntry? ParseTrack(XElement track)
    {
        var location = track.Element(XspfNamespace + "location")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        var title = track.Element(XspfNamespace + "title")?.Value?.Trim();
        var creator = track.Element(XspfNamespace + "creator")?.Value?.Trim();
        var durationValue = track.Element(XspfNamespace + "duration")?.Value?.Trim();
        var duration = int.TryParse(durationValue, out var parsedDuration) ? Math.Max(0, parsedDuration) : (int?)null;

        return new XspfPlaylistEntry(location, EmptyAsNull(title), EmptyAsNull(creator), duration);
    }

    private static string? EmptyAsNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static IReadOnlyList<XspfPlaylistEntry> ResolveEntries(object? payload)
    {
        if (payload is XspfPlaylistDocument document)
        {
            return document.Entries;
        }

        throw new InvalidOperationException("XSPF payload must be an XSPF playlist document.");
    }
}

public sealed record XspfPlaylistDocument(IReadOnlyList<XspfPlaylistEntry> Entries);

public sealed record XspfPlaylistEntry(string Location, string? Title, string? Creator, int? DurationMilliseconds);
