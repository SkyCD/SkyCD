using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.CdPlayer.Ini.Parsing;

namespace SkyCD.Plugin.CdPlayer.Ini;

/// <summary>
/// File format capability plugin for importing CDPlayer.ini Audio CD databases.
/// </summary>
public sealed class CdPlayerIniPlugin : IFileFormatPluginCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "cdplayer-ini",
            DisplayName: "Windows CD Player Database",
            Extensions: [".ini"],
            MimeTypes: ["text/plain", "application/x-ini"],
            CanRead: true,
            CanWrite: false);

    public Task<FileFormatReadResult> ReadAsync(
        FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!request.Source.CanRead)
            {
                return Task.FromResult(new FileFormatReadResult
                {
                    Success = false,
                    Error = "Source stream is not readable."
                });
            }

            var document = CdPlayerIniParser.Parse(request.Source);
            request.Progress?.Report(100);

            return Task.FromResult(new FileFormatReadResult
            {
                Success = true,
                Payload = document
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new FileFormatReadResult
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    public Task<FileFormatWriteResult> WriteAsync(
        FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "Writing CDPlayer.ini files is not supported."
        });
    }
}
