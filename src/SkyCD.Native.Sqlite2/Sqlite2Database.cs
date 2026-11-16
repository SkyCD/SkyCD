using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;

namespace SkyCD.Native.Sqlite2;

/// <summary>Read-only access through the bundled SQLite 2 engine, always on a private snapshot.</summary>
public sealed class Sqlite2Database : IDisposable
{
    public static ReadOnlySpan<byte> Signature => "** This file contains an SQLite 2.1 database **\0"u8;
    public static string EngineVersion => Marshal.PtrToStringUTF8(Sqlite2NativeMethods.Version())!;
    private readonly string directory = Path.Combine(Path.GetTempPath(), "skycd-sqlite2-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationToken token;
    private readonly Sqlite2NativeMethods.Authorizer authorizer;
    private readonly Sqlite2NativeMethods.Progress progress;
    private IntPtr database;
    private bool disposed;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private long remainingOperations = 100_000;

    public Sqlite2Database(Stream source, CancellationToken cancellationToken = default)
    {
        token = cancellationToken;
        authorizer = (_, action, first, _, _, _) => action is 20 or 21 ||
            (action == 19 && Marshal.PtrToStringUTF8(first) == "integrity_check") ? 0 : 1;
        progress = _ => token.IsCancellationRequested || Interlocked.Decrement(ref remainingOperations) <= 0 ? 1 : 0;
        try
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "catalog.db");
            if (source.CanSeek) source.Position = 0;
            using (var copy = File.Create(path))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) != 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (copy.Length + read > 256L * 1024 * 1024) throw new Sqlite2Exception("catalog exceeds the 256 MiB safety limit");
                    copy.Write(buffer, 0, read);
                }
            }
            using (var input = File.OpenRead(path))
            {
                Span<byte> header = stackalloc byte[68];
                if (input.Length < 2048 || input.Length % 1024 != 0 || input.ReadAtLeast(header, 68, false) != 68 || !header.StartsWith(Signature))
                    throw new Sqlite2Exception("invalid signature or truncated 1024-byte pages");
                var magic = BinaryPrimitives.ReadUInt32LittleEndian(header[48..52]);
                if (magic != 0xdae37528 && magic != 0x2875e3da) throw new Sqlite2Exception("invalid byte-order marker");
                var format = magic == 0xdae37528 ? BinaryPrimitives.ReadUInt32LittleEndian(header[64..]) : BinaryPrimitives.ReadUInt32BigEndian(header[64..]);
                if (format is < 2 or > 4) throw new Sqlite2UnsupportedFormatException(format);
            }
            if (EngineVersion != "2.8.17") throw new Sqlite2Exception("unexpected bundled engine version");
            database = Sqlite2NativeMethods.Open(path, 0, out var error);
            var message = TakeError(error);
            if (database == IntPtr.Zero) throw new Sqlite2Exception(message ?? "cannot open catalog snapshot");
            if (Sqlite2NativeMethods.SetAuthorizer(database, authorizer, IntPtr.Zero) != 0) throw new Sqlite2Exception("cannot restrict database access");
            Sqlite2NativeMethods.SetProgress(database, 1000, progress, IntPtr.Zero);
            var integrity = Query("PRAGMA integrity_check");
            if (integrity.Count != 1 || integrity[0].Length != 1 || integrity[0][0] != "ok") throw new Sqlite2Exception("native integrity check failed");
        }
        catch { Dispose(); throw; }
    }

    public IReadOnlyList<Sqlite2Row> ReadTable(string table)
    {
        if (string.IsNullOrEmpty(table) || table.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
            throw new Sqlite2Exception("invalid table identifier");
        var records = Query($"SELECT rowid,* FROM \"{table}\"");
        return records.Select(row =>
        {
            if (!int.TryParse(row[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) throw new Sqlite2Exception("invalid table rowid");
            return new Sqlite2Row(id, row[1..]);
        }).ToArray();
    }

    private List<string?[]> Query(string sql)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        token.ThrowIfCancellationRequested();
        var code = Sqlite2NativeMethods.Compile(database, sql, out _, out var statement, out var error);
        var message = TakeError(error);
        if (code != 0) throw new Sqlite2Exception(message ?? $"compile error {code}");
        var rows = new List<string?[]>();
        try
        {
            while ((code = Sqlite2NativeMethods.Step(statement, out var count, out var pointers, out _)) == 100)
            {
                token.ThrowIfCancellationRequested();
                if (count is < 1 or > 129 || rows.Count >= 1_000_000) throw new Sqlite2Exception("record or column count exceeds safety limits");
                var row = new string?[count];
                var bytes = 0;
                for (var i = 0; i < count; i++) row[i] = ReadText(Marshal.ReadIntPtr(pointers, i * IntPtr.Size), ref bytes);
                rows.Add(row);
            }
            token.ThrowIfCancellationRequested();
            if (code != 101) throw new Sqlite2Exception($"native read error {code}");
            return rows;
        }
        finally
        {
            Sqlite2NativeMethods.FinalizeStatement(statement, out var finalError);
            TakeError(finalError);
        }
    }

    private static string? ReadText(IntPtr pointer, ref int recordBytes)
    {
        if (pointer == IntPtr.Zero) return null;
        var length = 0;
        while (Marshal.ReadByte(pointer, length) != 0)
        {
            if (++recordBytes > 16 * 1024 * 1024) throw new Sqlite2Exception("record exceeds the 16 MiB safety limit");
            length++;
        }
        var bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);
        try { return Utf8.GetString(bytes); }
        catch (DecoderFallbackException) { throw new Sqlite2Exception("catalog text is not UTF-8"); }
    }

    private static string? TakeError(IntPtr error)
    {
        if (error == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(error); }
        finally { Sqlite2NativeMethods.Free(error); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (database != IntPtr.Zero) { Sqlite2NativeMethods.Close(database); database = IntPtr.Zero; }
        GC.KeepAlive(authorizer);
        GC.KeepAlive(progress);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
