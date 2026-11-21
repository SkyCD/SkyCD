using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Vvv.Exceptions;
using SkyCD.Plugin.Vvv.Models;
using SkyCD.Plugin.Vvv.Parsing;

namespace SkyCD.Plugin.Vvv.Import;

/// <summary>
/// Bounded, disposable import workspace: a private copy of the embedded
/// engine, the backup stream, the restored database and the engine temp
/// directory. The backup stream is copied, never modified, and the workspace
/// is removed even when the import fails.
/// </summary>
internal sealed class VvvBackupWorkspace : IDisposable
{
    private const string BackupFileName = "backup.vvvbk";

    private VvvBackupWorkspace(string root, VvvBackupEngineLayout engine)
    {
        RootPath = root;
        EngineRootPath = Path.Combine(root, "engine");
        TempPath = Path.Combine(root, "temp");
        BackupPath = Path.Combine(root, BackupFileName);
        DatabasePath = Path.Combine(root, "database", "catalog.fdb");
        ClientLibraryPath = engine.ClientLibraryPath.StartsWith(engine.RootPath, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(EngineRootPath, Path.GetRelativePath(engine.RootPath, engine.ClientLibraryPath))
            : Path.Combine(EngineRootPath, Path.GetFileName(engine.ClientLibraryPath));
    }

    internal string RootPath { get; }

    internal string EngineRootPath { get; }

    internal string TempPath { get; }

    internal string BackupPath { get; }

    internal string DatabasePath { get; }

    internal string ClientLibraryPath { get; }

    internal static async Task<VvvBackupWorkspace> CreateAsync(
        VvvBackupEngineLayout engine,
        Stream source,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), "SkyCD", "vvvbk", Guid.NewGuid().ToString("N"));
        VvvBackupWorkspace? workspace = null;
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "database"));
            Directory.CreateDirectory(Path.Combine(root, "temp"));
            workspace = new VvvBackupWorkspace(root, engine);

            StageEngine(engine, workspace.EngineRootPath, cancellationToken);
            WriteFirebirdConfiguration(workspace);
            await CopyBackupAsync(source, workspace.BackupPath, cancellationToken);
            return workspace;
        }
        catch
        {
            workspace?.Dispose();
            if (workspace is null)
            {
                TryDelete(root);
            }

            throw;
        }
    }

    public void Dispose()
    {
        TryDelete(RootPath);
    }

    private static void StageEngine(
        VvvBackupEngineLayout engine,
        string targetRoot,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetRoot);
        foreach (var directory in Directory.EnumerateDirectories(
                     engine.RootPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(engine.RootPath, directory);
            Directory.CreateDirectory(Path.Combine(targetRoot, relative));
        }

        foreach (var file in Directory.EnumerateFiles(engine.RootPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(engine.RootPath, file);
            if (string.Equals(relative, "firebird.conf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(file, Path.Combine(targetRoot, relative), overwrite: true);
        }
    }

    private static void WriteFirebirdConfiguration(VvvBackupWorkspace workspace)
    {
        var configuration = string.Join(Environment.NewLine, new[]
        {
            "DatabaseAccess = Restrict " + NativePath(workspace.RootPath),
            "RemoteAccess = false",
            "ExternalFileAccess = None",
            "UdfAccess = None",
            "TempDirectories = " + NativePath(workspace.TempPath),
            "Providers = Engine13",
            "WireCrypt = Disabled",
            string.Empty
        });
        File.WriteAllText(
            Path.Combine(workspace.EngineRootPath, "firebird.conf"),
            configuration,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static async Task CopyBackupAsync(
        Stream source,
        string targetPath,
        CancellationToken cancellationToken)
    {
        if (source.CanSeek && source.Length - source.Position > VvvBackupLimits.MaxBackupBytes)
        {
            throw new VvvBackupLimitExceededException("the backup is larger than 4 GiB");
        }

        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            await using var target = new FileStream(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > VvvBackupLimits.MaxBackupBytes)
                {
                    throw new VvvBackupLimitExceededException("the backup is larger than 4 GiB");
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (total == 0)
            {
                throw new VvvBackupMalformedException("the backup stream is empty");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string NativePath(string path) =>
        Path.DirectorySeparatorChar == '\\' ? path : path.Replace('\\', '/');

    private static void TryDelete(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 4)
                {
                    return;
                }

                Thread.Sleep(200);
            }
        }
    }
}
