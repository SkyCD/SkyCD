using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Discovery;
using System.Linq;

namespace SkyCD.Plugin.Host;

/// <summary>
/// In-memory plugin catalog used by host services.
/// </summary>
public sealed class PluginCatalog
{
    private readonly List<DiscoveredPlugin> _plugins = [];

    public IReadOnlyCollection<DiscoveredPlugin> Plugins => _plugins;

    public void SetPlugins(IEnumerable<DiscoveredPlugin> discovered)
    {
        _plugins.Clear();
        _plugins.AddRange(discovered);
    }

    public IReadOnlyList<TCapability> GetCapabilities<TCapability>()
        where TCapability : class, IPluginCapability
    {
        return _plugins
            .SelectMany(plugin => plugin.Capabilities)
            .OfType<TCapability>()
            .ToList();
    }

    public IReadOnlyList<FilePickerFileType> GetFileTypeChoices(bool allowRead, bool allowWrite)
    {
        var fileFormatCapabilities = _plugins
            .SelectMany(plugin => plugin.Capabilities)
            .OfType<IFileFormatPluginCapability>()
            .ToList();

        var formats = new List<FileFormatDescriptor>();

        foreach (var capability in fileFormatCapabilities)
        {
            foreach (var format in capability.SupportedFormats)
            {
                var canUse = (allowRead && format.CanRead) || (allowWrite && format.CanWrite);
                if (canUse)
                {
                    formats.Add(format);
                }
            }
        }

        var distinctFormats = formats.DistinctBy(f => f.FormatId).ToList();

        if (distinctFormats.Count == 0)
        {
            return new List<FilePickerFileType>
            {
                new FilePickerFileType("All files")
                {
                    Patterns = ["*.*"]
                }
            };
        }

        var allExtensions = distinctFormats.SelectMany(f => f.Extensions).Select(ext => $"*{ext}").Distinct().ToList();

        var fileTypeChoices = new List<FilePickerFileType>
        {
            new FilePickerFileType("All supported formats")
            {
                Patterns = allExtensions
            }
        };

        foreach (var format in distinctFormats)
        {
            var patterns = format.Extensions.Select(ext => $"*{ext}").ToArray();
            fileTypeChoices.Add(new FilePickerFileType(format.DisplayName)
            {
                Patterns = patterns
            });
        }

        fileTypeChoices.Add(new FilePickerFileType("All files")
        {
            Patterns = ["*.*"]
        });

        return fileTypeChoices;
    }
}
