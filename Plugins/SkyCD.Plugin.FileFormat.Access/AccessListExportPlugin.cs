using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using UCanAccess.File;
using SkyCD.Plugin.FileFormat.Access.Exceptions;
using SkyCD.Plugin.FileFormat.Access.Models;
using FileDataType = UCanAccess.File.DataType;

namespace SkyCD.Plugin.FileFormat.Access;

public sealed class AccessListExportPlugin : IFileFormatPluginCapability, IDataLossAwareFileFormatCapability
{
    private const int MaxTextCharacters = 255;

    public FileFormatDescriptor SupportedFormat => new("skycd-access-list", "Access list export (*.mdb)",
        [".mdb"], ["application/x-msaccess"], CanRead: false, CanWrite: true);

    public bool PreservesCatalogScope => false;

    public FileFormatDataLossAssessment AssessDataLoss(FileFormatDataLossRequest request) =>
        new(FileFormatDataLossAssessmentBuilder.AssessEntries(request,
            FileFormatEntryFieldSupport.Kind | FileFormatEntryFieldSupport.Size));

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatReadResult { Success = false, Error = "Access list export is write-only." });

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
    {
        string? temporary = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var export = request.Payload as AccessListExportRequest ?? new AccessListExportRequest { Rows = CoerceRows(request.Payload) };
            var directory = request.Target is FileStream file
                ? Path.GetDirectoryName(Path.GetFullPath(file.Name))! : Path.GetTempPath();
            temporary = Path.Combine(directory, $".skycd-access-{Guid.NewGuid():N}.mdb");
            await Task.Run(() => WriteDatabase(temporary, export, request.Progress, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await using var source = File.OpenRead(temporary);
            await source.CopyToAsync(request.Target, cancellationToken);
            request.Progress?.Report(100);
            return new FileFormatWriteResult { Success = true };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            return new FileFormatWriteResult { Success = false, Error = exception.Message };
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static IReadOnlyList<string> NormalizeColumnNames(IReadOnlyList<AccessListColumn> columns)
    {
        if (columns.Count is < 1 or > 255) throw new ArgumentOutOfRangeException(nameof(columns));
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>(columns.Count);
        for (var index = 0; index < columns.Count; index++)
        {
            var name = columns[index].DisplayName?.Trim() ?? string.Empty;
            if (name.Length is 0 or > 64 || name.Any(character => char.IsControl(character) || ".![]`".Contains(character)))
                name = $"Column{index + 1}";
            var candidate = name;
            for (var suffix = 2; !used.Add(candidate); suffix++)
            {
                var ending = $"_{suffix}";
                candidate = name[..Math.Min(name.Length, 64 - ending.Length)] + ending;
            }
            names.Add(candidate);
        }
        return names;
    }

    public static string FormatValue(object? value) => value switch
    {
        null => string.Empty,
        DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        JsonElement element when element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonElement element => element.GetRawText(),
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    private static IEnumerable<IReadOnlyDictionary<string, object?>> CoerceRows(object payload)
    {
        if (payload is IEnumerable<IReadOnlyDictionary<string, object?>> rows) return rows;
        if (payload is JsonElement { ValueKind: JsonValueKind.Array } array)
            return array.EnumerateArray().Select(row => (IReadOnlyDictionary<string, object?>)row.EnumerateObject()
                .ToDictionary(property => property.Name, property => (object?)property.Value.Clone(), StringComparer.OrdinalIgnoreCase));
        throw new ArgumentException("Access export requires ordered catalog-list rows.", nameof(payload));
    }

    private static void WriteDatabase(string path, AccessListExportRequest export,
        IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var names = NormalizeColumnNames(export.Columns);
        var operation = "creating the Jet database";
        try
        {
            using var database = Database.Create(path, version: "2003");
            operation = "creating the ItemList table";
            var table = database.CreateTable("ItemList", names.Select(name =>
                new ColumnBuilder(name, FileDataType.Text)
                    .WithLength(MaxTextCharacters * 2)
                    .WithRequired()).ToArray());

            operation = "inserting rows";
            var written = 0;
            foreach (var row in export.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = new object[names.Count];
                for (var index = 0; index < names.Count; index++)
                {
                    var key = export.Columns[index].Key;
                    var value = row.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
                    var text = FormatValue(value);
                    if (text.Length > MaxTextCharacters) throw new AccessExportValueTooLongException(names[index]);
                    values[index] = text;
                }
                table.AddRow(values);
                if (++written % 64 == 0) progress?.Report(Math.Min(99, written / 64));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not AccessExportValueTooLongException)
        {
            throw new AccessExportOperationException(operation, exception);
        }
    }
}
