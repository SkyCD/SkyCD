using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.Capabilities.Menu;

/// <summary>Optional host capability bound to the catalog open when the command starts.</summary>
public interface ICatalogCaptureHostApi : IHostCommandApi
{
    /// <summary>Imports captured files and refreshes the catalog view.</summary>
    Task ImportCapturedFilesAsync(IReadOnlyList<CapturedCatalogFile> files, CancellationToken cancellationToken = default);
}

/// <summary>A captured file, its virtual folder and metadata. Size is measured in bytes.</summary>
public sealed record CapturedCatalogFile(string Folder, string Date, string Name, long Size,
    IReadOnlyDictionary<string, string> Properties);
