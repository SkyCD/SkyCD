using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using OfficeIMO.Access;
using SkyCD.Plugin.Metadata.Office.Models;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Reads the catalog of an Access database: what it is called and what it contains.
/// </summary>
/// <remarks>
/// Only the database properties and the object lists are decoded. No row is read, no query is run and no macro
/// or VBA project is executed, because opening a catalog is a metadata question while running one is not. The
/// load bounds switch application-object decoding off for the same reason, so a database full of forms still
/// costs a catalog read.
/// </remarks>
internal static class OfficeAccessReader
{
    public static OfficeMetadata Read(
        byte[] data,
        OfficePluginOptions options,
        CancellationToken cancellationToken) =>
        OfficeProtection.Read(OfficeContainers.Jet, () => ReadOpened(data, options, cancellationToken));

    private static OfficeMetadata ReadOpened(
        byte[] data,
        OfficePluginOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = new MemoryStream(data, writable: false);
        using var document = AccessDocument.Load(stream, OfficeLoadBounds.Database(options));

        var protectedDatabase = OfficeProtection.FromDiagnostics(
            OfficeContainers.Jet,
            OfficeGuard.Try(() => document.Diagnostics?.Select(diagnostic => diagnostic.Code)));

        var metadata = new OfficeMetadata
        {
            Container = OfficeContainers.Jet,
            DocumentType = OfficeFamilyResolver.DocumentType(OfficeFamily.Database),
            FormatProfile = OfficeGuard.Try(() => document.Profile.ToString().ToLowerInvariant()),
            TableCount = OfficeGuard.TryCount(() => document.Tables.Count),
            QueryCount = OfficeGuard.TryCount(() => document.Queries.Count)
        };

        ApplyProperties(metadata, document);
        return protectedDatabase ?? metadata;
    }

    private static void ApplyProperties(OfficeMetadata metadata, AccessDocument document)
    {
        var properties = OfficeGuard.Try(() => document.Properties);
        if (properties is null)
        {
            return;
        }

        // The library reports the database caption under its stored name, which is not the plain word a reader
        // would guess, so both spellings are accepted.
        metadata.Title = Lookup(properties, "AppTitle") ?? Lookup(properties, "Title");
        metadata.Author = Lookup(properties, "Author") ?? Lookup(properties, "Owner");
        metadata.Subject = Lookup(properties, "Subject");
        metadata.Comments = Lookup(properties, "Comments");
        metadata.Company = Lookup(properties, "Company");
        metadata.Manager = Lookup(properties, "Manager");
    }

    private static string? Lookup(IReadOnlyDictionary<string, object?> properties, string name)
    {
        var entry = properties.FirstOrDefault(
            candidate => candidate.Key.Equals(name, StringComparison.OrdinalIgnoreCase));

        return entry.Value is null
            ? null
            : OfficeText.Normalize(Convert.ToString(entry.Value, CultureInfo.InvariantCulture));
    }
}
