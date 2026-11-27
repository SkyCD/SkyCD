using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SkyCD.Plugin.Feature.MotionDetector.Models;
using SkyCD.SDK.Capabilities.Sources;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.MotionDetector.Capture;

/// <summary>
/// Watches one source's physical destination folder and hands every captured image to the host for catalog
/// import. Files that already existed when the watcher started are ignored, so restarting a source never
/// re-imports an old folder. Imports are sequential, which preserves capture order and bounds DB pressure.
/// </summary>
internal sealed class DestinationWatcher : IDisposable
{
    private const int MaxBatch = 20;
    private const int StableChecks = 20;

    private readonly SourceConfiguration source;
    private readonly ISourceFactoryHostApi host;
    private readonly Action<string> report;
    private readonly FileSystemWatcher watcher;
    private readonly Channel<string> pending = Channel.CreateUnbounded<string>();
    private readonly HashSet<string> known = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task pump;
    private bool disposed;

    public DestinationWatcher(SourceConfiguration source, ISourceFactoryHostApi host, Action<string> report)
    {
        this.source = source;
        this.host = host;
        this.report = report;
        Directory.CreateDirectory(source.DestinationFolder);
        SeedExisting();
        watcher = new FileSystemWatcher(source.DestinationFolder)
        {
            IncludeSubdirectories = true,
            Filter = "*.*",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        watcher.Created += OnFile;
        watcher.Renamed += OnRenamed;
        watcher.EnableRaisingEvents = true;
        pump = Task.Run(PumpAsync);
    }

    public string DestinationFolder => source.DestinationFolder;

    private void SeedExisting()
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(source.DestinationFolder, "*", SearchOption.AllDirectories))
            {
                if (IsImage(path))
                {
                    known.Add(path);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            report(LocalizationService.Current.Format(MotionDetectorKeys.DestinationMissing, source.DestinationFolder));
        }
    }

    private void OnFile(object sender, FileSystemEventArgs args) => TryEnqueue(args.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs args) => TryEnqueue(args.FullPath);

    private void TryEnqueue(string path)
    {
        if (!IsImage(path))
        {
            return;
        }

        lock (known)
        {
            if (!known.Add(path))
            {
                return;
            }
        }

        pending.Writer.TryWrite(path);
    }

    private static bool IsImage(string path) =>
        CaptureSourceKinds.ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    private async Task PumpAsync()
    {
        try
        {
            while (await pending.Reader.WaitToReadAsync(cancellation.Token).ConfigureAwait(false))
            {
                var batch = new List<SourceFile>(MaxBatch);
                while (batch.Count < MaxBatch && pending.Reader.TryRead(out var path))
                {
                    var file = await BuildAsync(path, cancellation.Token).ConfigureAwait(false);
                    if (file is not null)
                    {
                        batch.Add(file);
                    }
                }

                if (batch.Count == 0)
                {
                    continue;
                }

                try
                {
                    await host.ImportSourceFilesAsync(batch, cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    report(LocalizationService.Current.Format(MotionDetectorKeys.ImportFailed,
                        source.DestinationFolder));
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Shutdown.
        }
    }

    private static async Task<SourceFile?> BuildAsync(string path, CancellationToken cancellationToken)
    {
        var info = await WaitForStableAsync(path, cancellationToken).ConfigureAwait(false);
        if (info is null)
        {
            return null;
        }

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = info.Name,
            ["Size"] = info.Length.ToString(CultureInfo.InvariantCulture),
            ["LastWriteTime"] = info.LastWriteTime.ToString("O", CultureInfo.InvariantCulture),
            ["LastAccessTime"] = info.LastAccessTime.ToString("O", CultureInfo.InvariantCulture),
            ["CreationTime"] = info.CreationTime.ToString("O", CultureInfo.InvariantCulture),
            ["IsReadOnly"] = info.IsReadOnly.ToString(CultureInfo.InvariantCulture),
            ["Attributes"] = info.Attributes.ToString(),
            ["FullName"] = info.FullName
        };
        return new SourceFile(
            "Motion",
            info.LastWriteTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            info.Name,
            info.Length,
            properties);
    }

    /// <summary>
    /// Waits until the writer has finished flushing: the size must stay unchanged across two checks. A file
    /// that vanishes or never grows is dropped instead of imported half-written.
    /// </summary>
    private static async Task<FileInfo?> WaitForStableAsync(string path, CancellationToken cancellationToken)
    {
        long previous = -1;
        for (var attempt = 0; attempt < StableChecks; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            if (info.Length > 0 && info.Length == previous)
            {
                return info;
            }

            previous = info.Length;
            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        watcher.EnableRaisingEvents = false;
        watcher.Created -= OnFile;
        watcher.Renamed -= OnRenamed;
        watcher.Dispose();
        cancellation.Cancel();
        pending.Writer.TryComplete();
        try
        {
            pump.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Cancellation races are expected during shutdown.
        }

        cancellation.Dispose();
    }
}
