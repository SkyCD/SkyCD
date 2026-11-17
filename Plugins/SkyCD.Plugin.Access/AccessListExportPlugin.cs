using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.Access;

public sealed class AccessListExportPlugin : IFileFormatPluginCapability
{
    public FileFormatDescriptor SupportedFormat => new("skycd-access-list", "Access list export (*.mdb)",
        [".mdb"], ["application/x-msaccess"], CanRead: false, CanWrite: true);

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatReadResult { Success = false, Error = "Access list export is write-only." });

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
    {
        string? temporary = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows()) throw new AccessExportProviderUnavailableException();
            var provider = FindProvider();
            var export = request.Payload as AccessListExportRequest ?? new AccessListExportRequest { Rows = CoerceRows(request.Payload) };
            var directory = request.Target is FileStream file
                ? Path.GetDirectoryName(Path.GetFullPath(file.Name))! : Path.GetTempPath();
            temporary = Path.Combine(directory, $".skycd-access-{Guid.NewGuid():N}.mdb");
            await Task.Run(() =>
            {
                if (!OperatingSystem.IsWindows()) throw new AccessExportProviderUnavailableException();
                WriteDatabase(temporary, provider, export, request.Progress, cancellationToken);
            }, cancellationToken);
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

    [SupportedOSPlatform("windows")]
    private static string FindProvider()
    {
        if (Type.GetTypeFromProgID("ADOX.Catalog") is null || Type.GetTypeFromProgID("ADODB.Command") is null)
            throw new AccessExportProviderUnavailableException();
        foreach (var provider in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0", "Microsoft.Jet.OLEDB.4.0" })
            if (Type.GetTypeFromProgID(provider) is not null) return provider;
        throw new AccessExportProviderUnavailableException();
    }

    [SupportedOSPlatform("windows")]
    private static void WriteDatabase(string path, string provider, AccessListExportRequest export,
        IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var names = NormalizeColumnNames(export.Columns);
        var connectionString = new DbConnectionStringBuilder
        {
            ["Provider"] = provider,
            ["Data Source"] = path,
            ["Jet OLEDB:Engine Type"] = 5
        };
        if (export.UseWindows1252Compatibility) connectionString["Locale Identifier"] = 1033;
        object? catalogObject = null;
        object? connectionObject = null;
        object? commandObject = null;
        var parameters = new List<object>();
        var transactionActive = false;
        var operation = "creating the Jet database";
        try
        {
            catalogObject = Activator.CreateInstance(Type.GetTypeFromProgID("ADOX.Catalog")!);
            dynamic catalog = catalogObject!;
            catalog.Create(connectionString.ConnectionString);
            connectionObject = catalog.ActiveConnection;
            dynamic connection = connectionObject;
            operation = "creating the ItemList table";
            connection.Execute($"CREATE TABLE [ItemList] ({string.Join(", ", names.Select(name => $"[{name}] TEXT(255) NOT NULL"))})");
            // Jet TEXT NOT NULL accepts empty strings only when this column property is enabled.
            foreach (var name in names)
            {
                operation = $"allowing empty text in column '{name}'";
                catalog.Tables["ItemList"].Columns[name].Properties["Jet OLEDB:Allow Zero Length"].Value = true;
            }

            operation = "preparing parameterized inserts";
            commandObject = Activator.CreateInstance(Type.GetTypeFromProgID("ADODB.Command")!);
            dynamic command = commandObject!;
            command.ActiveConnection = connection;
            command.CommandType = 1; // adCmdText
            command.CommandText = $"INSERT INTO [ItemList] ({string.Join(", ", names.Select(name => $"[{name}]"))}) VALUES ({string.Join(", ", names.Select(_ => "?"))})";
            for (var index = 0; index < names.Count; index++)
            {
                operation = $"creating parameter '{names[index]}'";
                object parameter = command.CreateParameter($"p{index}", 202, 1, 255); // adVarWChar, adParamInput
                command.Parameters.Append(parameter);
                parameters.Add(parameter);
            }
            var written = 0;
            connection.BeginTrans();
            transactionActive = true;
            foreach (var row in export.Rows)
            {
                operation = $"inserting row {written + 1}";
                cancellationToken.ThrowIfCancellationRequested();
                for (var index = 0; index < names.Count; index++)
                {
                    var key = export.Columns[index].Key;
                    var value = row.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
                    var text = FormatValue(value);
                    if (text.Length > 255) throw new AccessExportValueTooLongException(names[index]);
                    ((dynamic)parameters[index]).Value = text;
                }
                command.Execute();
                if (++written % 64 == 0) progress?.Report(Math.Min(99, written / 64));
            }
            cancellationToken.ThrowIfCancellationRequested();
            connection.CommitTrans();
            transactionActive = false;
            operation = "closing the database";
            connection.Close();
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not AccessExportValueTooLongException)
        {
            throw new AccessExportOperationException(operation, exception);
        }
        finally
        {
            if (connectionObject is not null)
            {
                if (transactionActive)
                {
                    try { ((dynamic)connectionObject).RollbackTrans(); } catch (COMException) { }
                }
                try { ((dynamic)connectionObject).Close(); } catch (COMException) { }
            }
            foreach (var parameter in parameters) Release(parameter);
            Release(commandObject);
            Release(catalogObject);
            Release(connectionObject);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}
