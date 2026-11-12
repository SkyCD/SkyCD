using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.App;

public sealed class CatalogLoadService(FileFormatManager formats)
{
    public Task<T> LoadAsync<T>(string path, Func<object?, IProgress<int>, T> import,
        IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        // Some legacy readers and database/tree operations are synchronous. Keep
        // the entire pipeline off the dispatcher, not just its file reads.
        return Task.Run(async () =>
        {
            var readProgress = new StageProgress(progress, 0, 50);
            readProgress.Report(0);
            await using var source = File.OpenRead(path);
            var format = formats.GetInstanceFor(path, source).SupportedFormat;
            source.Position = 0;
            var result = await formats.ReadAsync(new FileFormatReadRequest
            {
                FormatId = format.FormatId,
                Source = source,
                FileName = Path.GetFileName(path),
                Progress = readProgress
            }, cancellationToken);
            readProgress.Report(100);
            cancellationToken.ThrowIfCancellationRequested();
            var importProgress = new StageProgress(progress, 50, 100);
            var catalog = import(result.Payload, importProgress);
            importProgress.Report(100);
            return catalog;
        }, cancellationToken);
    }

    private sealed class StageProgress(IProgress<int> target, int minimum, int maximum) : IProgress<int>
    {
        private int last = -1;
        private readonly object gate = new();

        public void Report(int value)
        {
            lock (gate)
            {
                var mapped = minimum + Math.Clamp(value, 0, 100) * (maximum - minimum) / 100;
                if (mapped <= last) return;
                last = mapped;
                target.Report(mapped);
            }
        }
    }
}
