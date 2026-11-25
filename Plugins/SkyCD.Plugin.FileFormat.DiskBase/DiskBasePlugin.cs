using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.DiskBase.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskBase;

public sealed class DiskBasePlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    private readonly int codePage;

    public DiskBasePlugin() : this(1252)
    {
    }

    public DiskBasePlugin(int codePage)
    {
        this.codePage = codePage;
    }

    public FileFormatDescriptor SupportedFormat => new(
        "diskbase-qdr", "DiskBase catalog (*.qdr)", [".qdr"], ["application/x-diskbase-qdr"], true, false);

    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead || !stream.CanSeek ||
            (fileName is not null && !fileName.EndsWith(".qdr", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var position = stream.Position;
        try
        {
            DiskBaseReader.ValidateHeader(stream);
            return true;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            return false;
        }
        finally
        {
            stream.Position = position;
        }
    }

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await Task.Run(() => DiskBaseReader.Read(request.Source, codePage, cancellationToken), cancellationToken);
            request.Progress?.Report(100);
            return new FileFormatReadResult { Success = true, Payload = rows };
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException or DecoderFallbackException)
        {
            return new FileFormatReadResult { Success = false, Error = exception.Message };
        }
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult { Success = false, Error = "DiskBase QDR import is read-only." });
    }
}
