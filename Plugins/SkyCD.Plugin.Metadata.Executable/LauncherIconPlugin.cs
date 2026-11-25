using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Executable.Export;
using SkyCD.Plugin.Metadata.Executable.Models;
using SkyCD.Plugin.Metadata.Executable.Parsing;

namespace SkyCD.Plugin.Metadata.Executable;

/// <summary>
/// Publishes the icon a freedesktop launcher names, read from the launcher's folder or from the icon theme
/// stored on the same volume.
/// </summary>
/// <remarks>
/// A <c>.desktop</c> icon lives beside the entry or in an XDG icon folder, not in the entry's bytes, so this
/// capability is addressed by the host-owned local path: it is executed only while the launcher is available
/// locally, contributes nothing while it is offline, and never persists or logs the path. An unresolved theme
/// name, a path that does not hold a decodable PNG or JPEG, and an image above the existing byte and pixel
/// bounds all produce the explicit "no icon" state.
/// </remarks>
public sealed class LauncherIconPlugin : ILocalMediaPreviewCapability
{
    private const int ProviderPriority = 20;

    private readonly ExecutablePluginOptions options;

    public LauncherIconPlugin()
        : this(ExecutablePluginOptions.Default)
    {
    }

    public LauncherIconPlugin(ExecutablePluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(ExecutableSchema.LauncherExtensions, ExecutableSchema.LauncherContentTypes, ExecutableSchema.Preview,
            Priority: ProviderPriority, IndexTimePreview: true);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(LocalMediaContext source,
        IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        cancellationToken.ThrowIfCancellationRequested();
        var image = await LauncherIconReader.TryReadAsync(source, options, cancellationToken).ConfigureAwait(false);
        progress?.Report(new MediaProgress(1));
        return ExecutableIconPublisher.Publish(image, options);
    }
}
