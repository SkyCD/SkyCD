using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;

namespace SkyCD.Plugin.MotionDetector;

public sealed class MotionDetectorMenuPlugin : IMenuPluginCapability
{
    public const string CommandId = "motion-detector.capture";
    private readonly string directory;
    private readonly Func<string, CancellationToken, Task> capture;

    public MotionDetectorMenuPlugin() : this(Path.Combine(Path.GetDirectoryName(typeof(MotionDetectorMenuPlugin).Assembly.Location)!, "motion"), RunAsync) { }

    public MotionDetectorMenuPlugin(string directory, Func<string, CancellationToken, Task> capture)
    {
        this.directory = directory;
        this.capture = capture;
    }

    public IReadOnlyCollection<MenuContribution> GetMenuContributions() => [new(CommandId, "Motion Detector", "Tools", Order: 130)];

    public static string? Classify(string extension) => extension.ToLowerInvariant() switch
    {
        ".avi" or ".mpg" or ".wmv" => "Movies",
        ".jpg" or ".bmp" => "Pictures",
        _ => null
    };

    public async Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context, CancellationToken cancellationToken = default)
    {
        if (!commandId.Equals(CommandId, StringComparison.OrdinalIgnoreCase)) return;
        if (context.HostApi is not ICatalogCaptureHostApi host)
        {
            if (context.HostApi is not null) await context.HostApi.NotifyAsync("Open a writable catalog in a host supporting capture imports.", cancellationToken);
            return;
        }

        var executable = Path.Combine(directory, "motion.exe");
        if (!File.Exists(executable))
        {
            await host.NotifyAsync($"Motion Detector executable missing. Install it at '{executable}'.", cancellationToken);
            return;
        }

        try
        {
            var known = Directory.GetFiles(directory).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var detected = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            void Scan()
            {
                foreach (var path in Directory.GetFiles(directory))
                    if (!known.Contains(path) && Classify(Path.GetExtension(path)) is not null)
                        detected.TryAdd(path, DateTime.Now);
            }
            using var captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var running = capture(executable, captureCancellation.Token);
            try
            {
                while (!running.IsCompleted)
                {
                    Scan();
                    await Task.WhenAny(running, Task.Delay(250, cancellationToken));
                    cancellationToken.ThrowIfCancellationRequested();
                }
                await running;
                Scan();
            }
            finally
            {
                await captureCancellation.CancelAsync();
                try { await running; } catch (OperationCanceledException) when (captureCancellation.IsCancellationRequested) { }
            }
            var files = detected.Select(pair =>
            {
                var file = new FileInfo(pair.Key);
                var properties = new Dictionary<string, string>
                {
                    ["Name"] = file.Name,
                    ["Size"] = file.Length.ToString(CultureInfo.InvariantCulture),
                    ["LastWriteTime"] = file.LastWriteTime.ToString("O"),
                    ["LastAccessTime"] = file.LastAccessTime.ToString("O"),
                    ["CreationTime"] = file.CreationTime.ToString("O"),
                    ["IsReadOnly"] = file.IsReadOnly.ToString(),
                    ["Attributes"] = file.Attributes.ToString(),
                    ["FullName"] = file.FullName
                };
                return new CapturedCatalogFile(Classify(file.Extension)!, pair.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), file.Name, file.Length, properties);
            }).ToArray();
            await host.ImportCapturedFilesAsync(files, cancellationToken);
            await host.NotifyAsync($"Imported {files.Length} motion capture files.", cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            await host.NotifyAsync($"Motion Detector capture/import failed: {exception.Message}", cancellationToken);
        }
    }

    private static async Task RunAsync(string executable, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false } };
        process.Start();
        try { await process.WaitForExitAsync(cancellationToken); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }
}
